using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.Logging;
using SharedContracts.Common.Responses;
using SharedContracts.Events.Chats;
using SharedContracts.Interfaces;
using TicketService.Application.CQRS.Command.Chats;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Interfaces.Services;
using TicketService.Application.Interfaces.Utils;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Handler.Chats;

/// <summary>
/// Admin override Add — bypass block Closed/ClosedPendingRate (#517). Mirror logic của
/// <see cref="ChatAddCommandHandler"/> nhưng không qua <c>ChatClosedStateHelper</c> và không
/// check permission create.public/internal (Admin luôn có đủ quyền theo seed mapping #516).
/// </summary>
public class ChatOverrideAddCommandHandler : IRequestHandler<ChatOverrideAddCommand, TicketActionResponse>
{
    private readonly ITicketUnitOfWork _uow;
    private readonly IActivityLogger _activityLogger;
    private readonly ITicketChatRealtimeNotifier _realtimeNotifier;
    private readonly IMarkdownRenderer _markdownRenderer;
    private readonly ILogger<ChatOverrideAddCommandHandler> _logger;
    private readonly IIntegrationEventOutboxWriter _outboxWriter;
    private readonly IChatRecipientResolver _recipientResolver;
    private readonly IPublisher _publisher;   // Sprint audit #AUDIT-26

    public ChatOverrideAddCommandHandler(
        ITicketUnitOfWork uow,
        IActivityLogger activityLogger,
        ITicketChatRealtimeNotifier realtimeNotifier,
        IMarkdownRenderer markdownRenderer,
        ILogger<ChatOverrideAddCommandHandler> logger,
        IIntegrationEventOutboxWriter outboxWriter,
        IChatRecipientResolver recipientResolver,
        IPublisher publisher)
    {
        _uow = uow;
        _activityLogger = activityLogger;
        _realtimeNotifier = realtimeNotifier;
        _markdownRenderer = markdownRenderer;
        _logger = logger;
        _outboxWriter = outboxWriter;
        _recipientResolver = recipientResolver;
        _publisher = publisher;
    }

    public async Task<TicketActionResponse> Handle(ChatOverrideAddCommand request, CancellationToken ct)
    {
        if (request.UserRole != ActorRoleEnum.Admin)
            return Fail(403, "Chỉ Admin được override khi ticket đã đóng.");

        var ticket = await _uow.Tickets.GetByIdAsync(request.TicketId);
        if (ticket == null)
            return Fail(404, "Không tìm thấy Ticket.");

        var chat = new TicketChat
        {
            Id = Guid.NewGuid(),
            TicketId = ticket.Id,
            Ticket = ticket,
            AuthorUserId = request.UserId,
            AuthorRole = request.UserRole,
            AuthorDisplayName = request.UserDisplayName,
            Body = request.Body,
            IsInternal = request.IsInternal,
            BodyFormat = request.BodyFormat,
            AttachmentFileIds = request.Attachments?.Select(a => a.FileId).ToList() ?? new List<Guid>()
        };

        if (chat.BodyFormat == ChatBodyFormatEnum.Markdown)
            chat.BodyHtml = _markdownRenderer.RenderToHtml(chat.Body, chat.AttachmentFileIds);

        await _uow.TicketChats.AddAsync(chat);

        if (request.Attachments != null && request.Attachments.Any())
        {
            foreach (var att in request.Attachments)
            {
                var attachment = new TicketAttachment
                {
                    Id = Guid.NewGuid(),
                    TicketId = ticket.Id,
                    Ticket = ticket,
                    UploadedByUserId = request.UserId,
                    FileId = att.FileId,
                    FileName = att.FileName,
                    ContentType = att.ContentType,
                    SizeBytes = att.SizeBytes,
                    Source = AttachmentSourceEnum.StaffWork,
                    Url = att.Url
                };
                await _uow.TicketAttachments.AddAsync(attachment);
            }
        }

        await _activityLogger.LogAsync(
            ticket.Id,
            request.UserId,
            request.UserRole,
            request.UserDisplayName,
            ActivityActionEnum.Chatted,
            null,
            request.IsInternal ? "[Nội bộ — Admin override]" : "[Công khai — Admin override]",
            request.OverrideReason);

        // #AUDIT-26
        await _publisher.Publish(TicketService.Application.CQRS.Notification.Audit.TicketAuditTrailNotification.For(
            TicketService.Domain.Enums.TicketAuditActionEnum.CommentAdded, ticket.Id, targetDisplay: ticket.Code), ct);

        // Đường override trước đây chỉ bắn SignalR nên ai không mở sẵn ticket thì không hề biết.
        // Ghi ChatCreatedEvent như ChatAdd để notification đi đủ mọi người liên quan.
        var recipientIds = await _recipientResolver.ResolveAsync(
            ticket.Id, ticket.CustomerId, chat.AuthorUserId, chat.IsInternal, ct);

        await _outboxWriter.WriteAsync(new ChatCreatedEvent(
            chat.Id,
            chat.TicketId,
            chat.AuthorUserId,
            (int)chat.AuthorRole,
            chat.AuthorDisplayName,
            chat.Body,
            chat.IsInternal,
            chat.AttachmentFileIds,
            ticket.CustomerId,
            ticket.PrimaryHandlerStaffId,
            recipientIds), ct);

        await _uow.SaveChangesAsync(ct);

        try
        {
            var chatDto = new TicketChatDTO
            {
                Id = chat.Id.ToString(),
                TicketId = chat.TicketId.ToString(),
                AuthorUserId = chat.AuthorUserId.ToString(),
                AuthorRole = chat.AuthorRole,
                AuthorDisplayName = chat.AuthorDisplayName,
                Body = chat.Body,
                IsInternal = chat.IsInternal,
                AttachmentFileIds = chat.AttachmentFileIds.Select(id => id.ToString()).ToList(),
                CreatedAt = chat.CreatedAt
            };
            await _realtimeNotifier.NotifyChatAddedAsync(chatDto, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ChatOverrideAdd] Failed to broadcast ChatAdded SignalR event for ticket {TicketId}", ticket.Id);
        }

        return new TicketActionResponse
        {
            IsSuccess = true,
            StatusCode = 201,
            Message = "Thêm tin nhắn chat (override) thành công.",
            Data = new TicketActionDTO
            {
                Id = chat.Id.ToString(),
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
            Message = message
        };
    }
}
