using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedContracts.Common.Responses;
using SharedContracts.Events.Chats;
using SharedContracts.Interfaces;
using TicketService.Application.Common.Helpers;
using TicketService.Application.Common.Models;
using TicketService.Application.CQRS.Command.ChatAdd;
using TicketService.Application.DTOs.Response.Chats;
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
    private readonly IChatAuthorizationService _chatAuthorizationService;
    private readonly ISpamDetector _spamDetector;
    private readonly IProfanityFilter _profanityFilter;
    private readonly IPiiDetector _piiDetector;
    private readonly ChatOptions _chatOptions;
    private readonly ILogger<ChatAddCommandHandler> _logger;
    private readonly IIntegrationEventOutboxWriter _outboxWriter;

    public ChatAddCommandHandler(
        ITicketUnitOfWork uow,
        IActivityLogger activityLogger,
        ITicketChatRealtimeNotifier realtimeNotifier,
        IMarkdownRenderer markdownRenderer,
        IChatAuthorizationService chatAuthorizationService,
        ISpamDetector spamDetector,
        IProfanityFilter profanityFilter,
        IPiiDetector piiDetector,
        IOptions<ChatOptions> chatOptions,
        ILogger<ChatAddCommandHandler> logger,
        IIntegrationEventOutboxWriter outboxWriter)
    {
        _uow = uow;
        _activityLogger = activityLogger;
        _realtimeNotifier = realtimeNotifier;
        _markdownRenderer = markdownRenderer;
        _chatAuthorizationService = chatAuthorizationService;
        _spamDetector = spamDetector;
        _profanityFilter = profanityFilter;
        _piiDetector = piiDetector;
        _chatOptions = chatOptions.Value;
        _logger = logger;
        _outboxWriter = outboxWriter;
    }

    public async Task<TicketActionResponse> Handle(ChatAddCommand request, CancellationToken ct)
    {
        var ticket = await _uow.Tickets.GetByIdAsync(request.TicketId);
        if (ticket == null)
            return Fail(404, "Không tìm thấy Ticket.");

        var blockReason = ChatClosedStateHelper.GetBlockReason(
            ticket.Status, request.UserRole, ChatClosedStateHelper.ChatAction.Add, _chatOptions.BlockEditOnClosed);
        if (blockReason != null)
            return Fail(400, blockReason);

        if (!_chatAuthorizationService.CanCreateChat(request.IsInternal, request.UserPermissions))
            return Fail(403, request.IsInternal ? "Không có quyền tạo bình luận nội bộ." : "Không có quyền tạo bình luận.");

        if (await _spamDetector.IsSpamAsync(request.TicketId, request.UserId, request.Body, ct))
        {
            await _activityLogger.LogAsync(
                ticket.Id, request.UserId, request.UserRole, request.UserDisplayName,
                ActivityActionEnum.ChatFlagged, null, null, "Spam detected — cùng nội dung lặp ≥3 lần trong 5 phút.");
            await _uow.SaveChangesAsync(ct);
            return Fail(400, "Phát hiện spam — cùng nội dung đã được gửi lặp lại nhiều lần trong thời gian ngắn.");
        }

        var warnings = new List<string>();
        if (_profanityFilter.ContainsProfanity(request.Body, out var profanityMatches))
            warnings.Add($"Nội dung có thể chứa từ ngữ không phù hợp: {string.Join(", ", profanityMatches)}.");
        if (_piiDetector.ContainsPii(request.Body, out var piiMatches))
            warnings.Add($"Nội dung có thể chứa thông tin cá nhân: {string.Join(", ", piiMatches)}.");

        List<TicketParticipant> activeParticipants = new();
        if (request.Mentions != null && request.Mentions.Any())
        {
            activeParticipants = await _uow.TicketParticipants.GetAllAsync()
                .AsNoTracking()
                .Where(p => p.TicketId == ticket.Id && p.RemovedAt == null && !p.IsDeleted)
                .ToListAsync(ct);

            for (int i = 0; i < request.Mentions.Count; i++)
            {
                var mentionInput = request.Mentions[i];
                if (!activeParticipants.Any(p => p.UserId == mentionInput.UserId))
                {
                    var response = new TicketActionResponse
                    {
                        IsSuccess = false,
                        StatusCode = 400,
                        Message = "Dữ liệu đầu vào không hợp lệ."
                    };
                    response.ListErrors.Add(new Errors
                    {
                        Field = $"Mentions[{i}].UserId",
                        Detail = "User được mention phải là participant active của ticket."
                    });
                    return response;
                }
            }
        }

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

        var createdMentions = new List<TicketChatMention>();
        if (request.Mentions != null && request.Mentions.Any())
        {
            foreach (var mentionInput in request.Mentions)
            {
                var participant = activeParticipants.First(p => p.UserId == mentionInput.UserId);
                var mention = new TicketChatMention
                {
                    Id = Guid.NewGuid(),
                    ChatId = chat.Id,
                    Chat = chat,
                    MentionedUserId = mentionInput.UserId,
                    MentionedUserRole = participant.UserRole,
                    MentionedDisplayName = mentionInput.DisplayName,
                    IsAcknowledged = false
                };
                await _uow.TicketChatMentions.AddAsync(mention);
                createdMentions.Add(mention);

                await _outboxWriter.WriteAsync(new ChatMentionedEvent(
                    chat.Id,
                    chat.TicketId,
                    mentionInput.UserId,
                    (int)participant.UserRole,
                    mentionInput.DisplayName,
                    request.UserId,
                    false), ct);
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

        if (warnings.Count > 0)
        {
            await _activityLogger.LogAsync(
                ticket.Id, request.UserId, request.UserRole, request.UserDisplayName,
                ActivityActionEnum.ChatFlagged, null, null, string.Join(" | ", warnings));
        }

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
            ticket.AssignedStaffId), ct);

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
                CreatedAt = chat.CreatedAt,
                Mentions = createdMentions.Select(m => new TicketChatMentionDTO
                {
                    Id = m.Id.ToString(),
                    ChatId = m.ChatId.ToString(),
                    MentionedUserId = m.MentionedUserId.ToString(),
                    MentionedUserRole = m.MentionedUserRole,
                    MentionedDisplayName = m.MentionedDisplayName,
                    IsAcknowledged = m.IsAcknowledged,
                    AcknowledgedAt = m.AcknowledgedAt,
                    CreatedAt = m.CreatedAt
                }).ToList()
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
                Status = ticket.Status,
                Warnings = warnings.Count > 0 ? warnings : null
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
