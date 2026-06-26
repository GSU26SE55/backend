using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using TicketService.Application.CQRS.Command.ChatEraseUserData;
using TicketService.Application.Interfaces.Repositories;

namespace TicketService.Application.CQRS.Handler.Chats;

/// <summary>
/// GDPR right-to-erasure handler: bulk-redact all non-deleted, non-already-redacted chats by the user.
/// Soft-delete flag (IsDeleted) is NOT set — row stays in DB for audit trail, only body is overwritten.
/// #569.
/// </summary>
public class ChatEraseUserDataCommandHandler : IRequestHandler<ChatEraseUserDataCommand, CommonResponse<object>>
{
    private const string RedactedBody = "[REDACTED — GDPR erasure]";

    private readonly ITicketUnitOfWork _uow;

    public ChatEraseUserDataCommandHandler(ITicketUnitOfWork uow)
    {
        _uow = uow;
    }

    public async Task<CommonResponse<object>> Handle(ChatEraseUserDataCommand request, CancellationToken ct)
    {
        var chats = await _uow.TicketChats.GetAllAsync()
            .Where(c => c.AuthorUserId == request.RequestingUserId && !c.IsDeleted && !c.IsRedacted)
            .ToListAsync(ct);

        if (chats.Count == 0)
            return new CommonResponse<object> { IsSuccess = true, StatusCode = 200, Message = "Không có dữ liệu chat cần xóa." };

        var now = DateTime.UtcNow;
        await _uow.BeginTransactionAsync();
        try
        {
            foreach (var chat in chats)
            {
                chat.Body = RedactedBody;
                chat.BodyHtml = null;
                chat.IsRedacted = true;
                chat.RedactedAt = now;
                _uow.TicketChats.UpdateAsync(chat);
            }
            await _uow.CommitTransactionAsync();
        }
        catch
        {
            await _uow.RollbackTransactionAsync();
            throw;
        }

        return new CommonResponse<object>
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = $"Đã xóa dữ liệu GDPR: {chats.Count} tin nhắn chat đã được ẩn danh hóa."
        };
    }
}
