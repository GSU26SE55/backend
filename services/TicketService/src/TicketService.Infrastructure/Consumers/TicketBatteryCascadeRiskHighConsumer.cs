using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SharedContracts.Events;
using SharedContracts.Interfaces;
using TicketService.Application.IntegrationEvents;
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
///
/// <para><b>Sprint Bonus NS-13 (#657, R2+R6):</b> fallback (không có RelatedTicketId) CHỈ chọn ticket
/// incident (không nâng nhầm ticket bảo trì định kỳ). Không có ticket active nào → auto-tạo ticket P1
/// <c>Origin=System</c> + SlaTimer, để event High không rơi vào hư không.</para>
/// </summary>
public class TicketBatteryCascadeRiskHighConsumer : IConsumer<BatteryCascadeRiskHighEvent>
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
    private readonly ISlaCalculator _slaCalculator;   // Sprint Bonus NS-12 (#656)
    private readonly ITicketCodeGenerator _codeGenerator;   // Sprint Bonus NS-13 (#657)
    private readonly IIntegrationEventOutboxWriter _outboxWriter;     // Sprint Bonus NS-13 (#657)
    private readonly ILogger<TicketBatteryCascadeRiskHighConsumer> _logger;

    public TicketBatteryCascadeRiskHighConsumer(
        ITicketUnitOfWork uow,
        IActivityLogger activityLogger,
        ISlaCalculator slaCalculator,
        ITicketCodeGenerator codeGenerator,
        IIntegrationEventOutboxWriter producer,
        ILogger<TicketBatteryCascadeRiskHighConsumer> logger)
    {
        _uow = uow;
        _activityLogger = activityLogger;
        _slaCalculator = slaCalculator;
        _codeGenerator = codeGenerator;
        _outboxWriter = producer;
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

        // Sprint Bonus NS-13 (#657, R6) — fallback CHỈ chọn ticket incident (IsIncident hoặc
        // Origin auto/system). Trước fix, lấy "ticket active mới nhất bất kỳ" → nâng P1 nhầm cả
        // ticket bảo trì định kỳ (P3), audit ghi "safety override" cho việc thay dầu mỡ.
        ticket ??= await _uow.Tickets.GetAllAsync()
            .Where(t => !t.IsDeleted
                && t.BatteryAssetId == evt.BatteryAssetId
                && ActiveStatuses.Contains(t.Status)
                && (t.IsIncident
                    || t.Origin == TicketOriginEnum.AutoFromAlert
                    || t.Origin == TicketOriginEnum.System))
            .OrderByDescending(t => t.CreatedAt)
            .FirstOrDefaultAsync(ct);

        // Sprint Bonus NS-13 (#657, R2) — không có ticket incident active → AUTO-TẠO ticket P1
        // (Origin=System). Nếu không, event High rơi vào hư không và guard transition (oldScore<0.7)
        // khiến nó không bắn lại chừng nào score còn ≥ 0.7 → cơ hội xử lý biến mất.
        if (ticket is null)
        {
            await AutoCreateP1TicketAsync(evt, ct);
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

        // Sprint Bonus NS-12 (#656, R1) — recompute DueAt của SlaTimer từ mốc start với giờ SLA P1
        // (4h). Không đổi DueAt thì deadline vẫn là của priority cũ (vd 72h P3) — "P1 = SLA 4h" vô
        // nghĩa. Reset WarningSentAt để background service đánh giá lại mốc 80% theo deadline mới.
        var timer = await _uow.SlaTimers.GetAllAsync()
            .FirstOrDefaultAsync(t => t.TicketId == ticket.Id, ct);
        if (timer is not null && timer.Status == SlaTimerStatusEnum.Running)
        {
            timer.Priority = TicketPriorityEnum.P1Critical;
            timer.DueAt = _slaCalculator.CalculateDueDate(timer.StartedAt, TicketPriorityEnum.P1Critical);
            timer.WarningSentAt = null;
            _uow.SlaTimers.UpdateAsync(timer);
        }

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

    /// <summary>
    /// Sprint Bonus NS-13 (#657, R2) — auto-tạo ticket P1 (Origin=System) khi pin có cascade risk
    /// High mà không có ticket incident active. SLA clock chạy ngay từ lúc tạo (P1=4h) — tạo SlaTimer
    /// Running luôn (NS-12 dependency: ticket mới cần timer đúng).
    /// </summary>
    private async Task AutoCreateP1TicketAsync(BatteryCascadeRiskHighEvent evt, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var code = await _codeGenerator.GenerateAsync();

        var ticket = new Ticket
        {
            Id = Guid.NewGuid(),
            Code = code,
            Title = $"High cascade risk — battery {evt.AssetSerialNumber}",
            Description = $"Auto-created by the system because CascadeRiskScore={evt.CascadeRiskScore:F3} ≥ 0.7 "
                        + $"(propagation risk) and the battery has no ticket handling it yet. Isolation/urgent inspection required.",
            Category = TicketCategoryEnum.Repair,
            CustomerId = evt.CustomerId,
            BatteryAssetId = evt.BatteryAssetId,
            Status = TicketStatusEnum.Open,
            Origin = TicketOriginEnum.System,
            ImpactScope = ImpactScopeEnum.Site,
            UrgencyLevel = UrgencyLevelEnum.High,
            Priority = TicketPriorityEnum.P1Critical,
            IsIncident = true
        };
        await _uow.Tickets.AddAsync(ticket);

        await _uow.SlaTimers.AddAsync(new SlaTimer
        {
            Id = Guid.NewGuid(),
            TicketId = ticket.Id,
            Priority = TicketPriorityEnum.P1Critical,
            StartedAt = now,
            DueAt = _slaCalculator.CalculateDueDate(now, TicketPriorityEnum.P1Critical),
            OriginalDueAt = _slaCalculator.CalculateDueDate(now, TicketPriorityEnum.P1Critical),
            Status = SlaTimerStatusEnum.Running
        });

        await _activityLogger.LogAsync(
            ticket.Id,
            actorUserId: null,
            ActorRoleEnum.System,
            actorDisplayName: "Cascade Risk Detection",
            ActivityActionEnum.Created,
            newValue: $"Auto-created P1 (Origin=System) — CascadeRiskHigh score={evt.CascadeRiskScore:F3}, asset={evt.AssetSerialNumber}");

        await _outboxWriter.WriteAsync(
            new TicketCreatedEvent(ticket.Id, ticket.Code, ticket.CustomerId, ticket.Priority?.ToString()), ct);

        await _uow.SaveChangesAsync(ct);

        _logger.LogWarning(
            "Auto-tạo ticket P1 {TicketId} ({Code}) do cascade risk {Score} — pin {Serial} chưa có ticket active.",
            ticket.Id, ticket.Code, evt.CascadeRiskScore, evt.AssetSerialNumber);
    }
}
