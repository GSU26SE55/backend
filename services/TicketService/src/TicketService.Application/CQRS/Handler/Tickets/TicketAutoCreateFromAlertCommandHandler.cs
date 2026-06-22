using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;
using TicketService.Application.CQRS.Command.Tickets;
using TicketService.Application.DTOs.Response.Tickets;
using SharedContracts.Events;
using TicketService.Application.IntegrationEvents;
using TicketService.Application.Interfaces.Helpers;
using TicketService.Application.Interfaces.Repositories;
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

    public TicketAutoCreateFromAlertCommandHandler(
        ITicketUnitOfWork uow,
        ITicketCodeGenerator codeGenerator,
        IPriorityCalculator priorityCalculator,
        IActivityLogger activityLogger,
        IMessageProducerService producer)
    {
        _uow = uow;
        _codeGenerator = codeGenerator;
        _priorityCalculator = priorityCalculator;
        _activityLogger = activityLogger;
        _producer = producer;
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
            Category = TicketCategoryEnum.Repair,
            CustomerId = request.CustomerId,
            BatteryAssetId = request.BatteryAssetId,
            Status = TicketStatusEnum.Open,
            Origin = TicketOriginEnum.AutoFromAlert,
            OriginAlertId = request.OriginAlertId,
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
