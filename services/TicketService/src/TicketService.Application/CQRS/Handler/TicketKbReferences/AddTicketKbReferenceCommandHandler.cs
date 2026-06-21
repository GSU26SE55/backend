using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using TicketService.Application.CQRS.Command.TicketKbReferences;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Handler.TicketKbReferences;

public class AddTicketKbReferenceCommandHandler : IRequestHandler<AddTicketKbReferenceCommand, CommonResponse<object>>
{
    private readonly ITicketUnitOfWork _uow;

    public AddTicketKbReferenceCommandHandler(ITicketUnitOfWork uow)
    {
        _uow = uow;
    }

    public async Task<CommonResponse<object>> Handle(AddTicketKbReferenceCommand command, CancellationToken ct)
    {
        var ticket = await _uow.Tickets.GetAllAsync()
            .FirstOrDefaultAsync(t => t.Id == command.TicketId, ct);
        if (ticket == null)
            return Fail(404, "Không tìm thấy Ticket.");

        // KIỂM TRA LOCK LOGIC: Không cho gán bài viết khi đã báo Resolved hoặc đã Closed
        if (ticket.Status == TicketStatusEnum.Resolved ||
            ticket.Status == TicketStatusEnum.ClosedPendingRate ||
            ticket.Status == TicketStatusEnum.Closed)
        {
            return Fail(403, "Ticket đã ở trạng thái chờ phê duyệt hoặc đã hoàn thành. Không thể gán thêm tài liệu tham khảo.");
        }

        var article = await _uow.KnowledgeBaseArticles.GetAllAsync()
            .FirstOrDefaultAsync(a => a.Id == command.KbArticleId, ct);
        if (article == null)
            return Fail(404, "Không tìm thấy bài viết Knowledge Base.");

        var isDuplicate = await _uow.TicketKbReferences.AnyAsync(
            r => r.TicketId == command.TicketId &&
                 r.KbArticleId == command.KbArticleId &&
                 r.ReferenceType == command.ReferenceType &&
                 !r.IsDeleted);
        if (isDuplicate)
            return Fail(400, "Bài viết này đã được gán vào Ticket với loại tham chiếu tương tự.");

        var reference = new TicketKbReference
        {
            Id = Guid.NewGuid(),
            TicketId = command.TicketId,
            KbArticleId = command.KbArticleId,
            KbArticleCode = article.Code,
            ReferencedByUserId = command.CurrentUserId,
            ReferenceType = command.ReferenceType,
            Note = command.Note
        };

        await _uow.TicketKbReferences.AddAsync(reference);
        await _uow.SaveChangesAsync(ct);

        return new CommonResponse<object> { IsSuccess = true, StatusCode = 200, Message = "Đã gán bài viết vào Ticket thành công." };
    }

    private static CommonResponse<object> Fail(int statusCode, string message)
    {
        return new CommonResponse<object> { IsSuccess = false, StatusCode = statusCode, Message = message };
    }
}
