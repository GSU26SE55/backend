using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Events;
using SharedContracts.Interfaces;
using TicketService.Application.CQRS.Command.Tickets;
using TicketService.Application.CQRS.Notification.Audit;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Application.IntegrationEvents;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Interfaces.Utils;
using TicketService.Domain.Enums;
using TicketEntity = TicketService.Domain.Entities.Ticket;

namespace TicketService.Application.CQRS.Handler.Tickets;

public class TicketAutoCreateFromAlertCommandHandler : IRequestHandler<TicketAutoCreateFromAlertCommand, TicketActionResponse>
{
    private readonly ITicketUnitOfWork _uow;
    private readonly ITicketCodeGenerator _codeGenerator;
    private readonly IPriorityCalculator _priorityCalculator;
    private readonly IActivityLogger _activityLogger;
    private readonly IMessageProducerService _producer;
    private readonly IPublisher _publisher;   // Sprint audit #AUDIT-27

    public TicketAutoCreateFromAlertCommandHandler(
        ITicketUnitOfWork uow,
        ITicketCodeGenerator codeGenerator,
        IPriorityCalculator priorityCalculator,
        IActivityLogger activityLogger,
        IMessageProducerService producer,
        IPublisher publisher)
    {
        _uow = uow;
        _codeGenerator = codeGenerator;
        _priorityCalculator = priorityCalculator;
        _activityLogger = activityLogger;
        _producer = producer;
        _publisher = publisher;
    }

    public async Task<TicketActionResponse> Handle(TicketAutoCreateFromAlertCommand request, CancellationToken ct)
    {
        var (impact, urgency) = MapAnomalyToB3(request.AnomalyCategory);
        var priority = _priorityCalculator.Calculate(impact, urgency);
        var code = await _codeGenerator.GenerateAsync();

        var ticket = new TicketEntity
        {
            Id = Guid.NewGuid(),
            Code = code,
            Title = request.Title,
            Description = request.Description,
            Category = MapAnomalyToCategory(request.AnomalyCategory),
            CustomerId = request.CustomerId,
            BatteryAssetId = request.BatteryAssetId,
            Status = TicketStatusEnum.Open,
            Origin = TicketOriginEnum.AutoFromAlert,
            OriginAlertId = request.OriginAlertId,
            // Thời điểm anomaly được phát hiện + serial pin — để FE hiện panel "Bằng chứng
            // cảnh báo (lúc phát hiện)" và cột Serial giống ticket do Customer tạo.
            DetectedAt = request.DetectedAt,
            BatterySerialNumber = request.BatterySerialNumber,
            ImpactScope = impact,
            UrgencyLevel = urgency,
            Priority = priority,
            IsIncident = impact >= ImpactScopeEnum.Site || request.AnomalyCategory == "EnvironmentalIncident"
        };

        await _uow.Tickets.AddAsync(ticket);

        await _activityLogger.LogAsync(
            ticket.Id,
            null,
            ActorRoleEnum.System,
            "System",
            ActivityActionEnum.Created,
            newValue: $"Auto-created from alert {request.OriginAlertId}");

        // Outbox: Ticket Created
        await _producer.PublishAsync(new TicketCreatedEvent(ticket.Id, ticket.Code), ct);

        // #AUDIT-27 — causation_id = OriginAlertId (anomaly event → ticket chain).
        await _publisher.Publish(TicketAuditTrailNotification.For(
            TicketAuditActionEnum.AutoCreatedFromAnomaly, ticket.Id, targetDisplay: ticket.Code,
            causationId: request.OriginAlertId), ct);

        await _uow.SaveChangesAsync(ct);

        return new TicketActionResponse
        {
            IsSuccess = true,
            StatusCode = 201,
            Message = "Ticket auto-created from alert.",
            Data = new TicketActionDTO
            {
                Id = ticket.Id.ToString(),
                Code = ticket.Code,
                Status = ticket.Status
            }
        };
    }

    /// <summary>
    /// Map loại anomaly → <see cref="TicketCategoryEnum"/>.
    ///
    /// FIX duplicate-detection: trước đây hardcode <c>Repair</c> cho MỌI ticket auto, nên
    /// ticket auto "Overheat" mang category Repair — không bao giờ khớp với ticket Customer
    /// chọn "Quá nhiệt" (Overheat) ⇒ AI dò trùng bỏ sót (không cộng điểm cùng category),
    /// và index ux_tickets_active_auto_per_asset_category cũng gom nhầm mọi loại anomaly
    /// vào chung 1 slot.
    ///
    /// Anomaly không map được sang category nghiệp vụ (DeviceOffline, SensorMismatch, …)
    /// vẫn về <c>Repair</c> — đúng bản chất "cần kỹ thuật viên xử lý".
    /// </summary>
    public static TicketCategoryEnum MapAnomalyToCategory(string anomalyCategory) => anomalyCategory switch
    {
        "Overheat" or "HighAmbientTemp" or "HighTempHumidityCombo" => TicketCategoryEnum.Overheat,
        "AbnormalCharging" => TicketCategoryEnum.Charging,
        "Undervoltage" or "LowSoc" => TicketCategoryEnum.NoPower,
        "SohDegradation" or "RapidDischarge" or "HighInternalResistance" or "CellImbalance"
            => TicketCategoryEnum.Performance,
        _ => TicketCategoryEnum.Repair
    };

    private static (ImpactScopeEnum, UrgencyLevelEnum) MapAnomalyToB3(string category)
    {
        return category switch
        {
            "EnvironmentalIncident" => (ImpactScopeEnum.Site, UrgencyLevelEnum.High),
            "Overheat" => (ImpactScopeEnum.SingleAsset, UrgencyLevelEnum.High),
            "HighAmbientTemp" => (ImpactScopeEnum.Site, UrgencyLevelEnum.Medium),
            "SohDegradation" => (ImpactScopeEnum.SingleAsset, UrgencyLevelEnum.Low),
            "SensorMismatch" => (ImpactScopeEnum.SingleAsset, UrgencyLevelEnum.Medium),
            _ => (ImpactScopeEnum.SingleAsset, UrgencyLevelEnum.Medium)
        };
    }

    private static TicketActionResponse Fail(int statusCode, string message)
    {
        return new TicketActionResponse
        {
            IsSuccess = false,
            StatusCode = statusCode,
            Message = message,
        };
    }
}
