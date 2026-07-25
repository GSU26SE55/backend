using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using SharedContracts.Events;
using SharedContracts.Interfaces;
using TicketService.Application.CQRS.Command.Tickets;
using TicketService.Application.CQRS.Notification.Audit;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Application.IntegrationEvents;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Interfaces.Services;
using TicketService.Application.Interfaces.Utils;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;
using TicketEntity = TicketService.Domain.Entities.Ticket;

namespace TicketService.Application.CQRS.Handler.Tickets;

public class TicketCreateCommandHandler : IRequestHandler<TicketCreateCommand, TicketActionResponse>
{
    private readonly ITicketUnitOfWork _uow;
    private readonly ITicketCodeGenerator _codeGenerator;
    private readonly IActivityLogger _activityLogger;
    private readonly IMessageProducerService _producer;
    private readonly IPublisher _publisher;   // Sprint audit #AUDIT-26
    private readonly IBatteryLookupClient _batteryLookup;

    public TicketCreateCommandHandler(
        ITicketUnitOfWork uow,
        ITicketCodeGenerator codeGenerator,
        IActivityLogger activityLogger,
        IMessageProducerService producer,
        IPublisher publisher,
        IBatteryLookupClient batteryLookup)
    {
        _uow = uow;
        _codeGenerator = codeGenerator;
        _activityLogger = activityLogger;
        _producer = producer;
        _publisher = publisher;
        _batteryLookup = batteryLookup;
    }

    public async Task<TicketActionResponse> Handle(TicketCreateCommand request, CancellationToken ct)
    {
        // Validate Customer
        var customer = await _uow.CustomerAccounts.GetAllAsync()
            .FirstOrDefaultAsync(c => c.AccountId == request.CustomerId, ct);

        if (customer == null)
            return Fail(404, "Không tìm thấy thông tin khách hàng trong hệ thống Ticket.");

        if (customer.Status != AccountStatusEnum.Active)
            return Fail(403, "Tài khoản khách hàng đang bị khóa hoặc vô hiệu hóa.");

        var code = await _codeGenerator.GenerateAsync();

        var ticketId = Guid.NewGuid();
        var primaryBatteryAssetId = request.BatteryAssetIds.Count > 0 ? request.BatteryAssetIds[0] : Guid.Empty;

        // Serial snapshot — lookup ĐỒNG BỘ dùng JWT của Customer request. Fail → null (KHÔNG chặn tạo ticket).
        string? batterySerialNumber = null;
        if (primaryBatteryAssetId != Guid.Empty)
            batterySerialNumber = await _batteryLookup.GetSerialAsync(primaryBatteryAssetId, ct);

        var ticket = new TicketEntity
        {
            Id = ticketId,
            Code = code,
            Title = request.Title,
            Description = request.Description,
            Category = request.Category,
            CustomerId = request.CustomerId,
            BatteryAssetId = primaryBatteryAssetId,
            DetectedAt = request.DetectedAt,
            BatterySerialNumber = batterySerialNumber,
            Status = TicketStatusEnum.Open,
            Origin = TicketOriginEnum.ManualByCustomer,
            ReopenCount = 0,
            IsIncident = false,
            IncidentDetectedFrom = request.IncidentDetectedFrom,
            IncidentDetectedTo = request.IncidentDetectedTo ?? DateTime.UtcNow
        };

        await _uow.Tickets.AddAsync(ticket);

        foreach (var batteryId in request.BatteryAssetIds)
        {
            await _uow.TicketBatteryAssets.AddAsync(new TicketBatteryAsset
            {
                Id = Guid.NewGuid(),
                TicketId = ticketId,
                BatteryAssetId = batteryId
            });
        }

        // Auto-tạo participant Owner cho Customer tạo ticket (#528)
        await _uow.TicketParticipants.AddAsync(new TicketParticipant
        {
            Id = Guid.NewGuid(),
            TicketId = ticket.Id,
            Ticket = ticket,
            UserId = request.CustomerId,
            UserRole = ActorRoleEnum.Customer,
            ParticipantType = ParticipantTypeEnum.Owner,
            CanPost = true,
            CanViewInternal = false,
            AddedByUserId = request.CustomerId,
            AddedAt = DateTime.UtcNow
        });

        // Outbox: Ticket Created
        await _producer.PublishAsync(new TicketCreatedEvent(ticket.Id, ticket.Code), ct);

        await _activityLogger.LogAsync(
            ticket.Id,
            request.CustomerId,
            ActorRoleEnum.Customer,
            "Customer",
            ActivityActionEnum.Created);

        // #AUDIT-26
        await _publisher.Publish(TicketAuditTrailNotification.For(
            TicketAuditActionEnum.TicketCreated, ticket.Id, targetDisplay: ticket.Code), ct);

        await _uow.SaveChangesAsync(ct);

        return new TicketActionResponse
        {
            IsSuccess = true,
            StatusCode = 201,
            Message = "Ticket created successfully.",
            Data = new TicketActionDTO
            {
                Id = ticket.Id.ToString(),
                TicketId = ticket.Id.ToString(),
                Code = ticket.Code,
                Status = ticket.Status
            }
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
