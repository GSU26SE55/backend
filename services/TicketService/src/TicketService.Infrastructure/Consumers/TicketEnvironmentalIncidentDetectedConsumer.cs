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
/// Sprint Bonus NS-22 (#662, E2) — consume <see cref="EnvironmentalIncidentDetectedEvent"/> →
/// auto-tạo ticket site-level gắn <c>EnvironmentalIncidentId</c>, Priority theo Severity.
///
/// <para>Trước fix, TicketService KHÔNG consume event này → sự cố môi trường Critical (khói/ngập)
/// chỉ dừng ở notification: không ticket, không assign, không SLA. Nghịch lý: pin hơi nóng quá ngưỡng
/// → auto-ticket đầy đủ; nhà kho đang cháy → chỉ notify.</para>
///
/// <para>Idempotent theo <c>EnvironmentalIncidentId</c> (MassTransit retry / re-publish không tạo đôi).
/// SlaTimer chạy ngay (NS-12 dependency). Ticket site-level không gắn pin cụ thể →
/// <c>BatteryAssetId = Guid.Empty</c>.</para>
/// </summary>
public class TicketEnvironmentalIncidentDetectedConsumer : IConsumer<EnvironmentalIncidentDetectedEvent>
{
    // AlertSeverityEnum (BatteryService): Info=1 · Warning=2 · Critical=3.
    private const int SeverityCritical = 3;
    private const int SeverityWarning = 2;

    private readonly ITicketUnitOfWork _uow;
    private readonly ITicketCodeGenerator _codeGenerator;
    private readonly ISlaCalculator _slaCalculator;
    private readonly IActivityLogger _activityLogger;
    private readonly IIntegrationEventOutboxWriter _outboxWriter;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<TicketEnvironmentalIncidentDetectedConsumer> _logger;

    public TicketEnvironmentalIncidentDetectedConsumer(
        ITicketUnitOfWork uow,
        ITicketCodeGenerator codeGenerator,
        ISlaCalculator slaCalculator,
        IActivityLogger activityLogger,
        IIntegrationEventOutboxWriter producer,
        TimeProvider timeProvider,
        ILogger<TicketEnvironmentalIncidentDetectedConsumer> logger)
    {
        _uow = uow;
        _codeGenerator = codeGenerator;
        _slaCalculator = slaCalculator;
        _activityLogger = activityLogger;
        _outboxWriter = producer;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<EnvironmentalIncidentDetectedEvent> context)
    {
        var evt = context.Message;
        var ct = context.CancellationToken;

        var exists = await _uow.Tickets.GetAllAsync()
            .AnyAsync(t => !t.IsDeleted && t.EnvironmentalIncidentId == evt.IncidentId, ct);
        if (exists)
        {
            _logger.LogInformation(
                "Environmental incident {IncidentId} đã có ticket — bỏ qua (idempotent).", evt.IncidentId);
            return;
        }

        var priority = MapSeverityToPriority(evt.Severity);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var dueAt = _slaCalculator.CalculateResponseDueDate(now, priority);
        var code = await _codeGenerator.GenerateAsync();

        var ticket = new Ticket
        {
            Id = Guid.NewGuid(),
            Code = code,
            Title = $"Environmental incident at {evt.SiteName}",
            Description = $"Auto-created by the system from an environmental incident (type {evt.IncidentType}, severity {evt.Severity}) "
                        + $"at site {evt.SiteName}. {evt.Description}".Trim(),
            Category = TicketCategoryEnum.Repair,
            CustomerId = evt.CustomerId,
            BatteryAssetId = Guid.Empty, // site-level — không gắn pin cụ thể
            EnvironmentalIncidentId = evt.IncidentId,
            Status = TicketStatusEnum.Open,
            Origin = TicketOriginEnum.System,
            ImpactScope = ImpactScopeEnum.Site,
            UrgencyLevel = priority == TicketPriorityEnum.P1Critical ? UrgencyLevelEnum.High : UrgencyLevelEnum.Medium,
            Priority = priority,
            IsIncident = true
        };
        await _uow.Tickets.AddAsync(ticket);

        await _uow.SlaTimers.AddAsync(new SlaTimer
        {
            Id = Guid.NewGuid(),
            TicketId = ticket.Id,
            Priority = priority,
            StartedAt = now,
            DueAt = dueAt,
            OriginalDueAt = dueAt,
            Status = SlaTimerStatusEnum.Running
        });

        await _activityLogger.LogAsync(
            ticket.Id,
            actorUserId: null,
            ActorRoleEnum.System,
            actorDisplayName: "Environmental Incident",
            ActivityActionEnum.Created,
            newValue: $"Auto-created from environmental incident {evt.IncidentId} (site {evt.SiteName})");

        await _outboxWriter.WriteAsync(
            new TicketCreatedEvent(ticket.Id, ticket.Code, ticket.CustomerId, ticket.Priority?.ToString()), ct);

        await _uow.SaveChangesAsync(ct);

        _logger.LogWarning(
            "Auto-tạo ticket {Priority} {Code} từ sự cố môi trường {IncidentId} tại site {Site}.",
            priority, ticket.Code, evt.IncidentId, evt.SiteName);
    }

    private static TicketPriorityEnum MapSeverityToPriority(int severity) => severity switch
    {
        SeverityCritical => TicketPriorityEnum.P1Critical,
        SeverityWarning => TicketPriorityEnum.P2High,
        _ => TicketPriorityEnum.P3Normal
    };
}
