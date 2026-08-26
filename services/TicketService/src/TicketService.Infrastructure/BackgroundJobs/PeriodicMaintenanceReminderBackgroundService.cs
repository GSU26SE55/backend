using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using SharedContracts.Events;
using SharedContracts.Events.Root;
using SharedContracts.Interfaces;
using TicketService.Application.Common.Models;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;

namespace TicketService.Infrastructure.BackgroundJobs;

/// <summary>
/// Nhắc khách chọn giờ cho ticket bảo trì định kỳ, và bàn lại cho Manager khi khách im lặng.
/// </summary>
/// <remarks>
/// <para>
/// Ba mốc, mỗi mốc một lần duy nhất: nhắc lần đầu vào <see cref="PeriodicMaintenanceOptions.ReminderTime"/>
/// của ngày mở ticket, nhắc lần hai ngày kế, và sang ngày thứ ba thì chuyển cho Manager tự
/// sắp. Không có mốc thứ ba thì một khách không trả lời sẽ treo việc vô thời hạn.
/// </para>
/// <para>
/// Mốc tính theo <b>ngày địa phương</b> chứ không theo số giờ trôi qua: "08:00 sáng hôm sau"
/// là thứ khách hiểu, còn "sau 24 giờ" thì rơi vào giữa đêm với ticket mở lúc 2 giờ sáng.
/// </para>
/// <para>
/// Mỗi mốc đã gửi được ghi lại trên ticket, nên worker khởi động lại không nhắc lại từ đầu.
/// Sự kiện mang Id tất định theo (ticket, mốc) — hai replica cùng chạy thì lớp duy nhất của
/// bảng outbox loại bớt, và va khoá được nuốt chứ không thành lỗi.
/// </para>
/// </remarks>
public class PeriodicMaintenanceReminderBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<PeriodicMaintenanceOptions> _options;
    private readonly ILogger<PeriodicMaintenanceReminderBackgroundService> _logger;
    private readonly TimeProvider _timeProvider;

    public PeriodicMaintenanceReminderBackgroundService(
        IServiceScopeFactory scopeFactory,
        IOptions<PeriodicMaintenanceOptions> options,
        ILogger<PeriodicMaintenanceReminderBackgroundService> logger,
        TimeProvider? timeProvider = null)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Value.Enabled)
        {
            _logger.LogInformation("Periodic maintenance reminder worker is disabled.");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Periodic maintenance reminder tick failed.");
            }

            await Task.Delay(
                TimeSpan.FromSeconds(_options.Value.PollIntervalSeconds), stoppingToken);
        }
    }

    /// <summary>Một lượt quét — tách riêng để test gọi thẳng, không phải chờ vòng lặp.</summary>
    public async Task RunOnceAsync(CancellationToken ct)
    {
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;

        List<Guid> candidates;
        await using (var scope = _scopeFactory.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<ITicketUnitOfWork>();
            candidates = await uow.Tickets.GetAllAsync()
                .AsNoTracking()
                .Where(ticket =>
                    !ticket.IsDeleted &&
                    ticket.Status == TicketStatusEnum.Open &&
                    ticket.PeriodicMaintenanceDueAtUtc.HasValue &&
                    ticket.PeriodicMaintenanceScheduleDeadlineAtUtc.HasValue &&
                    ticket.ScheduledStartAtUtc == null &&
                    ticket.PeriodicMaintenanceManagerEscalatedAtUtc == null)
                .OrderBy(ticket => ticket.CreatedAt)
                .Select(ticket => ticket.Id)
                .Take(_options.Value.BatchSize)
                .ToListAsync(ct);
        }

        foreach (var ticketId in candidates)
            await PublishNextReminderAsync(ticketId, nowUtc, ct);
    }

    private async Task PublishNextReminderAsync(Guid ticketId, DateTime nowUtc, CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<ITicketUnitOfWork>();
        var outboxWriter = scope.ServiceProvider.GetRequiredService<IIntegrationEventOutboxWriter>();
        var timeZone = ResolveTimeZone(_options.Value.TimeZoneId);

        try
        {
            await uow.ExecuteInTransactionAsync(async transactionCt =>
            {
                var ticket = await uow.Tickets.GetAllAsync().FirstOrDefaultAsync(candidate =>
                    candidate.Id == ticketId &&
                    !candidate.IsDeleted &&
                    candidate.Status == TicketStatusEnum.Open &&
                    candidate.ScheduledStartAtUtc == null,
                    transactionCt);

                if (ticket?.PeriodicMaintenanceDueAtUtc is null ||
                    ticket.PeriodicMaintenanceScheduleDeadlineAtUtc is null)
                    return;

                var stage = GetDueReminderStage(ticket, nowUtc, timeZone, _options.Value.ReminderTime);
                if (!stage.HasValue)
                    return;

                var evt = new PeriodicMaintenanceReminderDueEvent(
                    ticket.Id,
                    ticket.Code,
                    ticket.BatteryAssetId,
                    ticket.CustomerId,
                    ticket.PeriodicMaintenanceDueAtUtc.Value,
                    ticket.PeriodicMaintenanceScheduleDeadlineAtUtc.Value,
                    stage.Value,
                    ticket.PeriodicMaintenanceDueAtUtc.Value < nowUtc)
                {
                    Id = DeterministicEventId.From(
                        ticket.Id, $"periodic-maintenance-reminder:{(int)stage.Value}")
                };

                await outboxWriter.WriteAsync(evt, transactionCt);

                switch (stage.Value)
                {
                    case PeriodicMaintenanceReminderStage.CustomerFirstReminder:
                        ticket.PeriodicMaintenanceReminder1SentAtUtc = nowUtc;
                        break;
                    case PeriodicMaintenanceReminderStage.CustomerSecondReminder:
                        ticket.PeriodicMaintenanceReminder2SentAtUtc = nowUtc;
                        break;
                    case PeriodicMaintenanceReminderStage.ManagerEscalation:
                        ticket.PeriodicMaintenanceManagerEscalatedAtUtc = nowUtc;
                        break;
                }

                await uow.SaveChangesAsync(transactionCt);
            }, ct);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Replica khác đã ghi đúng mốc này — Id tất định nên hai bên va vào nhau ở outbox.
            _logger.LogInformation(
                "Periodic reminder already persisted for ticket {TicketId}.", ticketId);
        }
    }

    /// <summary>
    /// Mốc kế tiếp còn nợ, hoặc <c>null</c> nếu chưa tới giờ hoặc đã gửi đủ ba mốc.
    /// </summary>
    internal static PeriodicMaintenanceReminderStage? GetDueReminderStage(
        Ticket ticket,
        DateTime nowUtc,
        TimeZoneInfo timeZone,
        TimeSpan reminderTime)
    {
        // Khách đã chọn giờ ⇒ không còn gì để nhắc.
        if (ticket.ScheduledStartAtUtc.HasValue)
            return null;

        var createdLocalDate = TimeZoneInfo.ConvertTimeFromUtc(EnsureUtc(ticket.CreatedAt), timeZone).Date;
        var nowUtcValue = EnsureUtc(nowUtc);

        bool IsDue(int dayOffset)
        {
            var local = DateTime.SpecifyKind(
                createdLocalDate.AddDays(dayOffset).Add(reminderTime), DateTimeKind.Unspecified);
            return TimeZoneInfo.ConvertTimeToUtc(local, timeZone) <= nowUtcValue;
        }

        if (!ticket.PeriodicMaintenanceReminder1SentAtUtc.HasValue && IsDue(0))
            return PeriodicMaintenanceReminderStage.CustomerFirstReminder;
        if (!ticket.PeriodicMaintenanceReminder2SentAtUtc.HasValue && IsDue(1))
            return PeriodicMaintenanceReminderStage.CustomerSecondReminder;
        if (!ticket.PeriodicMaintenanceManagerEscalatedAtUtc.HasValue && IsDue(2))
            return PeriodicMaintenanceReminderStage.ManagerEscalation;

        return null;
    }

    /// <summary>
    /// Múi giờ cấu hình sai không được làm chết worker — rơi về UTC và ghi cảnh báo, vì nhắc
    /// lệch vài giờ vẫn hơn là không nhắc ai.
    /// </summary>
    private TimeZoneInfo ResolveTimeZone(string id)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception exception) when (
            exception is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            _logger.LogWarning(
                exception, "Unknown time zone '{TimeZoneId}' — falling back to UTC.", id);
            return TimeZoneInfo.Utc;
        }
    }

    private static DateTime EnsureUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };
}
