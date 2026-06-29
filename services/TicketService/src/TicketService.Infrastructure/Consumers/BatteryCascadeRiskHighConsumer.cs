using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SharedContracts.Events;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Interfaces.Utils;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;

namespace TicketService.Application.Consumers;

/// <summary>
/// Sprint 7 B4 (§31.7) — consume <see cref="BatteryCascadeRiskHighEvent"/> (CascadeRiskScore ≥ 0.7):
/// auto-upgrade Priority ticket liên quan lên <b>P1</b> + ghi audit vào TicketActivity.
///
/// <para><b>Cơ sở safety-override (design.md Priority Policy):</b> Priority bất biến trong vòng đời ticket,
/// ngoại lệ DUY NHẤT là safety override — phải ghi reason vào TicketActivity. Cascade risk cao = nguy cơ
/// lan truyền (an toàn) → đủ điều kiện override, và được audit đầy đủ qua activity này.</para>
///
/// <para><b>Idempotent:</b> nếu ticket đã P1Critical → bỏ qua (không ghi activity trùng). Event chỉ publish
/// khi score cross 0.7 (transition-based guard ở BatteryService) nên hiếm khi trùng.</para>
/// </summary>
public class BatteryCascadeRiskHighConsumer : IConsumer<BatteryCascadeRiskHighEvent>
{
    private static readonly TicketStatusEnum[] ActiveStatuses =
    {
        TicketStatusEnum.New,
        TicketStatusEnum.Open,
        TicketStatusEnum.Assigned,
        TicketStatusEnum.InProgress,
        TicketStatusEnum.WaitingCustomer,
        TicketStatusEnum.WaitingParts,
        TicketStatusEnum.WaitingOnsiteSchedule
    };

    private readonly ITicketUnitOfWork _uow;
    private readonly IActivityLogger _activityLogger;
    private readonly ILogger<BatteryCascadeRiskHighConsumer> _logger;

    public BatteryCascadeRiskHighConsumer(
        ITicketUnitOfWork uow,
        IActivityLogger activityLogger,
        ILogger<BatteryCascadeRiskHighConsumer> logger)
    {
        _uow = uow;
        _activityLogger = activityLogger;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<BatteryCascadeRiskHighEvent> context)
    {
        var evt = context.Message;
        var ct = context.CancellationToken;

        // Ưu tiên ticket được trỏ trực tiếp (RelatedTicketId), fallback theo BatteryAssetId.
        Ticket? ticket = null;

        if (evt.RelatedTicketId.HasValue)
        {
            ticket = await _uow.Tickets.GetAllAsync()
                .FirstOrDefaultAsync(t => t.Id == evt.RelatedTicketId.Value && !t.IsDeleted, ct);
        }

        ticket ??= await _uow.Tickets.GetAllAsync()
            .Where(t => !t.IsDeleted
                && t.BatteryAssetId == evt.BatteryAssetId
                && ActiveStatuses.Contains(t.Status))
            .OrderByDescending(t => t.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (ticket is null)
        {
            _logger.LogInformation(
                "CascadeRiskHigh cho asset {Serial} ({AssetId}) nhưng không có ticket active — bỏ qua.",
                evt.AssetSerialNumber, evt.BatteryAssetId);
            return;
        }

        // Idempotent — đã P1 thì không override lại + không ghi activity trùng.
        if (ticket.Priority == TicketPriorityEnum.P1Critical)
        {
            _logger.LogInformation(
                "Ticket {TicketId} đã P1Critical — bỏ qua cascade-risk override.", ticket.Id);
            return;
        }

        var oldPriority = ticket.Priority;
        ticket.Priority = TicketPriorityEnum.P1Critical;

        await _activityLogger.LogAsync(
            ticket.Id,
            actorUserId: null,
            ActorRoleEnum.System,
            actorDisplayName: "Cascade Risk Detection",
            ActivityActionEnum.PriorityAssigned,
            oldValue: oldPriority?.ToString(),
            newValue: TicketPriorityEnum.P1Critical.ToString(),
            reason: $"CascadeRiskHigh safety override — score={evt.CascadeRiskScore:F3}, asset={evt.AssetSerialNumber}");

        await _uow.SaveChangesAsync(ct);

        _logger.LogWarning(
            "Ticket {TicketId} upgrade Priority {Old} → P1Critical do cascade risk {Score} (asset {Serial}).",
            ticket.Id, oldPriority, evt.CascadeRiskScore, evt.AssetSerialNumber);
    }
}
