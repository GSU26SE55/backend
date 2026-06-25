using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using TicketService.Application.Common.Helpers;
using TicketService.Application.CQRS.Command.ChatPin;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Application.Interfaces.Helpers;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Interfaces.Services;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Handler.Chats;

public class ChatPinCommandHandler : IRequestHandler<ChatPinCommand, TicketActionResponse>
{
    private const int MaxPinnedPerTicket = 3;

    private readonly ITicketUnitOfWork _uow;
    private readonly IActivityLogger _activityLogger;
    private readonly IChatAuthorizationService _chatAuthorizationService;

    public ChatPinCommandHandler(ITicketUnitOfWork uow, IActivityLogger activityLogger, IChatAuthorizationService chatAuthorizationService)
    {
        _uow = uow;
        _activityLogger = activityLogger;
        _chatAuthorizationService = chatAuthorizationService;
    }

    public async Task<TicketActionResponse> Handle(ChatPinCommand request, CancellationToken ct)
    {
        if (!_chatAuthorizationService.CanPinChat(request.UserPermissions))
            return Fail(403, "Không có quyền pin bình luận.");

        var chat = await _uow.TicketChats.GetByIdAsync(request.ChatId);
        if (chat == null || chat.IsDeleted)
            return Fail(404, "Không tìm thấy bình luận.");

        if (chat.TicketId != request.TicketId)
            return Fail(404, "Không tìm thấy bình luận.");

        var ticket = await _uow.Tickets.GetByIdAsync(request.TicketId);
        if (ticket == null)
            return Fail(404, "Không tìm thấy Ticket.");

        if (chat.IsPinned)
            return Fail(400, "Bình luận đã được pin.");

        await _uow.BeginTransactionAsync();
        try
        {
            // Đếm lại ngay trước khi pin, trong cùng transaction — thu hẹp race window khi có request pin đồng thời.
            var pinnedCount = await _uow.TicketChats.GetAllAsync()
                .AsNoTracking()
                .CountAsync(c => c.TicketId == request.TicketId && c.IsPinned && !c.IsDeleted, ct);

            if (pinnedCount >= MaxPinnedPerTicket)
            {
                await _uow.RollbackTransactionAsync();
                return Fail(400, $"Đã đạt giới hạn tối đa {MaxPinnedPerTicket} bình luận pin/ticket.");
            }

            chat.IsPinned = true;
            chat.PinnedAt = DateTime.UtcNow;
            chat.PinnedByUserId = request.UserId;
            _uow.TicketChats.UpdateAsync(chat);

            await _activityLogger.LogAsync(
                ticket.Id,
                request.UserId,
                request.UserRole,
                request.UserDisplayName,
                ActivityActionEnum.ChatPinned,
                null,
                ChatTextHelper.Truncate(chat.Body));

            await _uow.CommitTransactionAsync();
        }
        catch
        {
            await _uow.RollbackTransactionAsync();
            throw;
        }

        return new TicketActionResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Pin bình luận thành công.",
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
