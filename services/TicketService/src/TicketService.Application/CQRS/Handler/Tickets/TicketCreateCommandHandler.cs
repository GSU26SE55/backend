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
    private readonly IIntegrationEventOutboxWriter _outboxWriter;
    private readonly IPublisher _publisher;   // Sprint audit #AUDIT-26
    private readonly IBatteryLookupClient _batteryLookup;
    private readonly ISlaCalculator _slaCalculator;

    public TicketCreateCommandHandler(
        ITicketUnitOfWork uow,
        ITicketCodeGenerator codeGenerator,
        IActivityLogger activityLogger,
        IIntegrationEventOutboxWriter producer,
        IPublisher publisher,
        IBatteryLookupClient batteryLookup,
        ISlaCalculator slaCalculator)
    {
        _uow = uow;
        _codeGenerator = codeGenerator;
        _activityLogger = activityLogger;
        _outboxWriter = producer;
        _publisher = publisher;
        _batteryLookup = batteryLookup;
        _slaCalculator = slaCalculator;
    }

    public async Task<TicketActionResponse> Handle(TicketCreateCommand request, CancellationToken ct)
    {
        // Validate Customer
        var customer = await _uow.CustomerAccounts.GetAllAsync()
            .FirstOrDefaultAsync(c => c.AccountId == request.CustomerId, ct);

        if (customer == null)
            return Fail(404, "Customer information not found in the Ticket system.");

        if (customer.Status != AccountStatusEnum.Active)
            return Fail(403, "The customer account is locked or disabled.");

        var batterySerialNumbers = new Dictionary<Guid, string>();
        // Site của ticket lấy từ pin đầu tiên tra được. Ticket nhiều pin trên thực tế đều cùng một
        // cabinet, nên site đầu tiên là đủ; nếu lookup không trả site thì để null chứ không chặn
        // tạo ticket — SiteId chỉ dùng để gom ticket, không phải dữ liệu bắt buộc.
        Guid? siteId = null;
        foreach (var batteryAssetId in request.BatteryAssetIds)
        {
            var snapshot = await _batteryLookup.GetSnapshotAsync(batteryAssetId, ct);
            if (string.IsNullOrWhiteSpace(snapshot.SerialNumber))
                return Fail(403, "The selected battery does not exist or you do not have access to it.");

            batterySerialNumbers[batteryAssetId] = snapshot.SerialNumber;
            siteId ??= snapshot.SiteId;
        }

        var code = await _codeGenerator.GenerateAsync();

        var ticketId = Guid.NewGuid();
        var primaryBatteryAssetId = request.BatteryAssetIds.Count > 0 ? request.BatteryAssetIds[0] : Guid.Empty;

        var batterySerialNumber = batterySerialNumbers[primaryBatteryAssetId];

        var ticket = new TicketEntity
        {
            Id = ticketId,
            Code = code,
            Title = request.Title,
            Description = request.Description,
            Category = request.Category,
            CustomerId = request.CustomerId,
            BatteryAssetId = primaryBatteryAssetId,
            BatterySerialNumber = batterySerialNumber,
            // Cho phép gom ticket này với ticket environmental cùng cabinet.
            SiteId = siteId,
            Status = TicketStatusEnum.Open,
            Priority = TicketPriorityEnum.P1Critical,
            Origin = TicketOriginEnum.ManualByCustomer,
            ReopenCount = 0,
            IsIncident = false,
            DetectedAt = request.IncidentDetectedAt
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

        foreach (var attachment in request.Attachments)
        {
            await _uow.TicketAttachments.AddAsync(new TicketAttachment
            {
                Id = Guid.NewGuid(),
                TicketId = ticketId,
                UploadedByUserId = request.CustomerId,
                FileId = attachment.FileId,
                FileName = attachment.FileName,
                ContentType = attachment.ContentType,
                SizeBytes = attachment.SizeBytes,
                Url = attachment.Url,
                Source = AttachmentSourceEnum.CustomerSubmission,
                IsInline = false,
                Ticket = ticket
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

        // Stage 1: Khởi tạo Response SLA Timer khi tạo ticket ở Open
        var nowUtc = DateTime.UtcNow;
        var priority = ticket.Priority ?? TicketPriorityEnum.P1Critical;
        var responseDueAt = _slaCalculator.CalculateResponseDueDate(nowUtc, priority);
        await _uow.SlaTimers.AddAsync(new SlaTimer
        {
            Id = Guid.NewGuid(),
            TicketId = ticket.Id,
            Priority = priority,
            StartedAt = nowUtc,
            DueAt = responseDueAt,
            OriginalDueAt = responseDueAt,
            Status = SlaTimerStatusEnum.Running
        });

        await _outboxWriter.WriteAsync(
            new TicketCreatedEvent(ticket.Id, ticket.Code, ticket.CustomerId, ticket.Priority?.ToString()), ct);

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
