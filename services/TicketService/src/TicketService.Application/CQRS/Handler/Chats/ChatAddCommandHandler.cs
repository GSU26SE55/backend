using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.Logging;
using SharedContracts.Common.Responses;
using TicketService.Application.CQRS.Command.ChatAdd;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Application.Interfaces.Helpers;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Interfaces.Services;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Handler.Chats;

public class ChatAddCommandHandler : IRequestHandler<ChatAddCommand, TicketActionResponse>
{
    private readonly ITicketUnitOfWork _uow;
    private readonly IActivityLogger _activityLogger;
    private readonly ITicketChatRealtimeNotifier _realtimeNotifier;
    private readonly IMarkdownRenderer _markdownRenderer;
    private readonly ILogger<ChatAddCommandHandler> _logger;

    public ChatAddCommandHandler(
        ITicketUnitOfWork uow,
        IActivityLogger activityLogger,
        ITicketChatRealtimeNotifier realtimeNotifier,
        IMarkdownRenderer markdownRenderer,
        ILogger<ChatAddCommandHandler> logger)
    {
        _uow = uow;
        _activityLogger = activityLogger;
        _realtimeNotifier = realtimeNotifier;
        _markdownRenderer = markdownRenderer;
        _logger = logger;
    }

    public async Task<TicketActionResponse> Handle(ChatAddCommand request, CancellationToken ct)
    {
        var ticket = await _uow.Tickets.GetByIdAsync(request.TicketId);
        if (ticket == null)
            return Fail(404, "Không tìm thấy Ticket.");

        if (ticket.Status == TicketStatusEnum.Closed)
            return Fail(400, "Không thể thêm bình luận khi ticket đã đóng.");

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
                    Source = request.UserRole == ActorRoleEnum.Customer
                        ? AttachmentSourceEnum.CustomerSubmission
                        : AttachmentSourceEnum.StaffWork
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
            request.IsInternal ? "[Nội bộ]" : "[Công khai]",
            $"Đã thêm tin nhắn chat: {request.Body[..Math.Min(request.Body.Length, 50)]}...");

        await _uow.SaveChangesAsync(ct);

        // Broadcast chat via SignalR
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
            _logger.LogError(ex, "[ChatAdd] Failed to broadcast ChatAdded SignalR event for ticket {TicketId}", ticket.Id);
        }

        return new TicketActionResponse
        {
            IsSuccess = true,
            StatusCode = 201,
            Message = "Thêm tin nhắn chat thành công.",
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
