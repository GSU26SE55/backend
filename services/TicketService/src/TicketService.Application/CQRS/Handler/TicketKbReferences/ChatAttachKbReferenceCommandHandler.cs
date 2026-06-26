using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using TicketService.Application.CQRS.Command.ChatAttachKbReference;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Interfaces.Services;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Handler.TicketKbReferences;

public class ChatAttachKbReferenceCommandHandler : IRequestHandler<ChatAttachKbReferenceCommand, CommonResponse<object>>
{
    private readonly ITicketUnitOfWork _uow;
    private readonly ITicketCurrentUserService _currentUser;

    public ChatAttachKbReferenceCommandHandler(ITicketUnitOfWork uow, ITicketCurrentUserService currentUser)
    {
        _uow = uow;
        _currentUser = currentUser;
    }

    public async Task<CommonResponse<object>> Handle(ChatAttachKbReferenceCommand command, CancellationToken ct)
    {
        var role = _currentUser.Role;
        if (role != "Staff" && role != "Manager" && role != "Admin")
            return Fail(403, "Chỉ Staff, Manager hoặc Admin mới được gắn tài liệu KB vào chat.");

        var chat = await _uow.TicketChats.GetAllAsync()
            .FirstOrDefaultAsync(c => c.Id == command.ChatId && c.TicketId == command.TicketId && !c.IsDeleted, ct);
        if (chat == null)
            return Fail(404, "Không tìm thấy chat.");

        var ticket = await _uow.Tickets.GetAllAsync()
            .FirstOrDefaultAsync(t => t.Id == command.TicketId && !t.IsDeleted, ct);
        if (ticket == null)
            return Fail(404, "Không tìm thấy Ticket.");

        if (ticket.Status == TicketStatusEnum.Closed)
            return Fail(403, "Ticket đã đóng, không thể gắn tài liệu KB.");

        var article = await _uow.KnowledgeBaseArticles.GetAllAsync()
            .FirstOrDefaultAsync(a => a.Id == command.KbArticleId && !a.IsDeleted, ct);
        if (article == null)
            return Fail(404, "Không tìm thấy bài viết Knowledge Base.");

        var existing = await _uow.TicketKbReferences.GetAllAsync()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(r => r.ChatId == command.ChatId && r.KbArticleId == command.KbArticleId, ct);

        if (existing != null)
        {
            existing.IsDeleted = false;
            existing.DeletedAt = null;
            existing.KbArticleCode = article.Code;
            existing.ReferencedByUserId = command.CurrentUserId;
            existing.ReferenceType = command.ReferenceType;
            existing.Note = command.Note;
            _uow.TicketKbReferences.UpdateAsync(existing);
        }
        else
        {
            var reference = new TicketKbReference
            {
                Id = Guid.NewGuid(),
                TicketId = command.TicketId,
                ChatId = command.ChatId,
                KbArticleId = command.KbArticleId,
                KbArticleCode = article.Code,
                ReferencedByUserId = command.CurrentUserId,
                ReferenceType = command.ReferenceType,
                Note = command.Note
            };
            await _uow.TicketKbReferences.AddAsync(reference);
        }

        await _uow.SaveChangesAsync(ct);

        return new CommonResponse<object>
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Đã gắn bài viết KB vào chat thành công."
        };
    }

    private static CommonResponse<object> Fail(int statusCode, string message)
        => new() { IsSuccess = false, StatusCode = statusCode, Message = message };
}
