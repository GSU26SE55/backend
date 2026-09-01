using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedContracts.Events;
using SharedContracts.Events.Root;
using SharedContracts.Interfaces;
using SharedInfrastructure.Idempotency;
using TicketService.Application.Common.Models;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Interfaces.Utils;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;

namespace TicketService.Infrastructure.Consumers;

/// <summary>
/// Mở ticket bảo trì khi BatteryService báo một cục pin đã tới kỳ.
/// </summary>
/// <remarks>
/// <para>
/// Lịch bảo trì thuộc về tài sản, nên BatteryService giữ lịch và ghi nhật ký kỳ. Nhưng ghi
/// nhật ký thì không ai được cử đi — ticket mới là thứ đưa công việc vào hàng chờ của
/// Manager và mang theo SLA, phân công, chat, nhật ký hoạt động đã có sẵn. Đó là việc của
/// consumer này.
/// </para>
/// <para>
/// Ticket mở ở trạng thái <c>Open</c> với priority cố định <c>P3Normal</c>: kỳ bảo trì là
/// việc theo lịch, không phải sự cố, nên không có Impact × Urgency để triage. Manager vẫn
/// nâng/hạ được sau nếu hiện trường cho thấy khác.
/// </para>
/// <para>
/// <b>Chống trùng hai lớp.</b> Inbox chặn theo Id sự kiện — mà Id là tất định theo (pin, hạn
/// kỳ) nên message giao lại hay hai replica cùng phát đều quy về một. Lớp thứ hai là truy vấn
/// theo (pin, hạn kỳ) ngay trước khi ghi, phòng trường hợp sự kiện tới lại sau khi bản ghi
/// inbox đã hết hạn.
/// </para>
/// </remarks>
public class TicketMaintenanceCycleDueConsumer : IConsumer<MaintenanceCycleDueEvent>
{
    private readonly ITicketUnitOfWork _uow;
    private readonly ITicketCodeGenerator _codeGenerator;
    private readonly IOptions<PeriodicMaintenanceOptions> _options;
    private readonly IInboxStore _inboxStore;
    private readonly IIntegrationEventOutboxWriter _outboxWriter;
    private readonly ILogger<TicketMaintenanceCycleDueConsumer> _logger;

    public TicketMaintenanceCycleDueConsumer(
        ITicketUnitOfWork uow,
        ITicketCodeGenerator codeGenerator,
        IOptions<PeriodicMaintenanceOptions> options,
        IInboxStore inboxStore,
        IIntegrationEventOutboxWriter outboxWriter,
        ILogger<TicketMaintenanceCycleDueConsumer> logger)
    {
        _uow = uow;
        _codeGenerator = codeGenerator;
        _options = options;
        _inboxStore = inboxStore;
        _outboxWriter = outboxWriter;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<MaintenanceCycleDueEvent> context)
    {
        await context.ProcessOnceAsync(_inboxStore, nameof(TicketMaintenanceCycleDueConsumer), async () =>
        {
            var evt = context.Message;
            var ct = context.CancellationToken;
            var nowUtc = DateTime.UtcNow;

            var alreadyRaised = await _uow.Tickets.GetAllAsync()
                .AnyAsync(t =>
                    !t.IsDeleted &&
                    t.BatteryAssetId == evt.BatteryAssetId &&
                    t.PeriodicMaintenanceDueAtUtc == evt.DueAtUtc, ct);

            if (alreadyRaised)
            {
                _logger.LogInformation(
                    "Periodic maintenance ticket already exists for battery {BatteryAssetId} due {DueAtUtc}.",
                    evt.BatteryAssetId, evt.DueAtUtc);
                return;
            }

            var ticketId = Guid.NewGuid();
            var code = await _codeGenerator.GenerateAsync();

            // Kỳ đã quá hạn lúc mở ticket thì khách vẫn cần một khoảng để chọn giờ — đếm từ
            // bây giờ. Kỳ chưa tới hạn thì hạn chót chính là hạn kỳ.
            var isOverdue = evt.DueAtUtc < nowUtc;
            var deadlineAtUtc = isOverdue
                ? nowUtc.AddDays(_options.Value.OverdueScheduleWindowDays)
                : evt.DueAtUtc;

            await _uow.Tickets.AddAsync(new Ticket
            {
                Id = ticketId,
                Code = code,
                BatteryAssetId = evt.BatteryAssetId,
                CustomerId = evt.CustomerId,
                Title = "Periodic battery maintenance",
                Description =
                    $"Scheduled {evt.IntervalMonths}-month maintenance cycle #{evt.CycleNo} "
                    + $"for battery {evt.SerialNumber ?? evt.BatteryAssetId.ToString()}.",
                Category = TicketCategoryEnum.Repair,
                Priority = TicketPriorityEnum.P3Normal,
                Status = TicketStatusEnum.Open,
                Origin = TicketOriginEnum.System,
                ReopenCount = 0,
                IsIncident = false,
                BatterySerialNumber = evt.SerialNumber,
                PeriodicMaintenanceDueAtUtc = evt.DueAtUtc,
                PeriodicMaintenanceScheduleDeadlineAtUtc = deadlineAtUtc,
                CreatedAt = nowUtc
            });

            await _uow.TicketBatteryAssets.AddAsync(new TicketBatteryAsset
            {
                Id = Guid.NewGuid(),
                TicketId = ticketId,
                BatteryAssetId = evt.BatteryAssetId,
                CreatedAt = nowUtc
            });

            // Báo ngược cho BatteryService để nối kỳ với ticket vừa mở. Ghi outbox TRƯỚC
            // SaveChanges để sự kiện nằm cùng transaction với ticket: không bao giờ có ticket
            // mà quên báo, cũng không báo về một ticket ghi hụt.
            //
            // Id tất định theo kỳ: sự kiện giao lại hay consumer chạy lại đều quy về một, nên
            // phía nhận không ghi đè lung tung.
            await _outboxWriter.WriteAsync(
                new PeriodicMaintenanceTicketRaisedEvent(
                    evt.MaintenanceCycleId,
                    evt.BatteryAssetId,
                    ticketId,
                    code,
                    evt.DueAtUtc)
                {
                    Id = DeterministicEventId.From(
                        evt.MaintenanceCycleId, "periodic-maintenance-ticket-raised")
                },
                ct);

            await _uow.SaveChangesAsync(ct);

            _logger.LogInformation(
                "Raised periodic maintenance ticket {Code} for battery {BatteryAssetId}, cycle #{CycleNo} due {DueAtUtc}.",
                code, evt.BatteryAssetId, evt.CycleNo, evt.DueAtUtc);
        });
    }
}
