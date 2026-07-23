using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SharedContracts.Events.Chats;
using SharedContracts.Interfaces;
using TicketService.Application.Common.Utils;
using TicketService.Application.CQRS.Command.ChatTemplates;
using TicketService.Application.DTOs.Response.Chats;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Interfaces.Services;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Handler.ChatTemplates;

public class ChatFromTemplateCommandHandler : IRequestHandler<ChatFromTemplateCommand, TicketActionResponse>
{
    private readonly ITicketUnitOfWork _uow;
    private readonly ITemplateRenderer _renderer;
    private readonly ITicketChatRealtimeNotifier _realtimeNotifier;
    private readonly IIntegrationEventOutboxWriter _outboxWriter;
    private readonly IChatCacheService _chatCache;
    private readonly ILogger<ChatFromTemplateCommandHandler> _logger;

    public ChatFromTemplateCommandHandler(
        ITicketUnitOfWork uow,
        ITemplateRenderer renderer,
        ITicketChatRealtimeNotifier realtimeNotifier,
        IIntegrationEventOutboxWriter outboxWriter,
        IChatCacheService chatCache,
        ILogger<ChatFromTemplateCommandHandler> logger)
    {
        _uow = uow;
        _renderer = renderer;
        _realtimeNotifier = realtimeNotifier;
        _outboxWriter = outboxWriter;
        _chatCache = chatCache;
        _logger = logger;
    }

    public async Task<TicketActionResponse> Handle(ChatFromTemplateCommand request, CancellationToken ct)
    {
        var template = await _uow.ChatTemplates.GetByIdAsync(request.TemplateId);
        if (template == null || template.IsDeleted || !template.IsActive)
            return Fail(404, "Không tìm thấy template.");

        var ticket = await _uow.Tickets.GetByIdAsync(request.TicketId);
        if (ticket == null || ticket.IsDeleted)
            return Fail(404, "Không tìm thấy Ticket.");

        if (ticket.PrimaryHandlerStaffId == null && _uow.TicketAssignments != null)
        {
            ticket.PrimaryHandlerStaffId = await _uow.TicketAssignments.GetAllAsync()
                .Where(a => a.TicketId == ticket.Id && !a.IsDeleted && a.Role == AssignmentRoleEnum.PrimaryHandler)
                .Select(a => (Guid?)a.StaffId)
                .FirstOrDefaultAsync(ct);
        }

        var activeParticipantIds = await _uow.TicketParticipants.GetAllAsync()
            .AsNoTracking()
            .Where(p => p.TicketId == request.TicketId && p.RemovedAt == null && !p.IsDeleted)
            .Select(p => p.UserId)
            .ToListAsync(ct);

        if (!TicketQueryHelper.CanAccessTicket(ticket.CustomerId, ticket.PrimaryHandlerStaffId,
                request.ActorUserId, request.ActorRoles, activeParticipantIds))
            return Fail(403, "Không có quyền truy cập Ticket này.");

        var renderedContent = _renderer.Render(template.Content, request.Variables);
        var isInternal = request.IsInternal ?? template.IsInternalDefault;

        var chat = new TicketChat
        {
            Id = Guid.NewGuid(),
            TicketId = ticket.Id,
            Ticket = ticket,
            AuthorUserId = request.ActorUserId,
            AuthorRole = request.ActorRole,
            AuthorDisplayName = request.ActorDisplayName,
            Body = renderedContent,
            IsInternal = isInternal
        };

        await _uow.BeginTransactionAsync();
        try
        {
            await _uow.TicketChats.AddAsync(chat);

            template.UsageCount += 1;
            _uow.ChatTemplates.UpdateAsync(template);

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
                ticket.PrimaryHandlerStaffId), ct);

            await _uow.CommitTransactionAsync();
        }
        catch (Exception ex)
        {
            await _uow.RollbackTransactionAsync();
            _logger.LogError(ex, "[ChatFromTemplate] Transaction failed for ticket {TicketId}", ticket.Id);
            return Fail(500, "Lỗi khi tạo chat từ template.");
        }

        await _chatCache.InvalidateAsync(ticket.Id, ct);

        try
        {
            await _realtimeNotifier.NotifyChatAddedAsync(new TicketChatDTO
            {
                Id = chat.Id.ToString(),
                TicketId = chat.TicketId.ToString(),
                AuthorUserId = chat.AuthorUserId.ToString(),
                AuthorRole = chat.AuthorRole,
                AuthorDisplayName = chat.AuthorDisplayName,
                Body = chat.Body,
                IsInternal = chat.IsInternal,
                CreatedAt = chat.CreatedAt
            }, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ChatFromTemplate] Failed to broadcast ChatAdded for ticket {TicketId}", ticket.Id);
        }

        return new TicketActionResponse
        {
            IsSuccess = true,
            StatusCode = 201,
            Message = "Gửi chat từ template thành công.",
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
        => new() { IsSuccess = false, StatusCode = statusCode, Message = message };
}
