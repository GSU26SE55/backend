using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using TicketService.Application.CQRS.Command.TicketKbReferences;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Interfaces.Services;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Handler.TicketKbReferences;

public class AddTicketKbReferenceCommandHandler : IRequestHandler<AddTicketKbReferenceCommand, CommonResponse<object>>
{
    private readonly ITicketUnitOfWork _uow;
    private readonly ITicketCurrentUserService _currentUser;

    public AddTicketKbReferenceCommandHandler(ITicketUnitOfWork uow, ITicketCurrentUserService currentUser)
    {
        _uow = uow;
        _currentUser = currentUser;
    }

    public async Task<CommonResponse<object>> Handle(AddTicketKbReferenceCommand command, CancellationToken ct)
    {
        var ticket = await _uow.Tickets.GetAllAsync()
            .FirstOrDefaultAsync(t => t.Id == command.TicketId, ct);
        if (ticket == null)
            return Fail(404, "Ticket not found.");

        if (ticket.PrimaryHandlerStaffId == null && _uow.TicketAssignments != null)
        {
            ticket.PrimaryHandlerStaffId = await _uow.TicketAssignments.GetAllAsync()
                .Where(a => a.TicketId == ticket.Id && !a.IsDeleted && a.Role == AssignmentRoleEnum.PrimaryHandler)
                .Select(a => (Guid?)a.StaffId)
                .FirstOrDefaultAsync(ct);
        }

        // KIỂM TRA PHÂN QUYỀN:
        // - Admin/Manager được gán bài viết cho bất kỳ ticket nào.
        // - Staff phải là PrimaryHandler của Ticket.
        // - Các trường hợp khác bị chặn.
        var userRole = _currentUser.Role;
        if (userRole != "Admin" && userRole != "Manager")
        {
            if (userRole == "Staff")
            {
                if (ticket.PrimaryHandlerStaffId != command.CurrentUserId)
                {
                    return Fail(403, "Only staff assigned to handle this Ticket may attach reference documents.");
                }
            }
            else
            {
                return Fail(403, "You do not have permission to perform this action.");
            }
        }

        // KIỂM TRA LOCK LOGIC: Không cho gán bài viết khi đã báo Resolved hoặc đã Closed.
        // Ngoại lệ: 2 type "after-resolve" (GeneratedAfterResolve, ProvidedToCustomer) về ngữ nghĩa
        // xảy ra lúc/sau khi Resolved nên vẫn cho gán ở state Resolved; từ ClosedPendingRate trở đi chặn tất cả.
        var isAfterResolveType = command.ReferenceType == KbReferenceTypeEnum.GeneratedAfterResolve ||
                                 command.ReferenceType == KbReferenceTypeEnum.ProvidedToCustomer;
        if (ticket.Status == TicketStatusEnum.ClosedPendingRate ||
            ticket.Status == TicketStatusEnum.Closed ||
            (ticket.Status == TicketStatusEnum.Resolved && !isAfterResolveType))
        {
            // 409: xung đột với trạng thái hiện tại của ticket (không phải lỗi quyền)
            return Fail(409, "Ticket is pending approval or already completed. Cannot attach more reference documents.");
        }

        var article = await _uow.KnowledgeBaseArticles.GetAllAsync()
            .FirstOrDefaultAsync(a => a.Id == command.KbArticleId, ct);
        if (article == null)
            return Fail(404, "Knowledge Base article not found.");

        var existing = await _uow.TicketKbReferences.GetAllAsync()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(r => r.TicketId == command.TicketId && r.KbArticleId == command.KbArticleId, ct);

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
                KbArticleId = command.KbArticleId,
                KbArticleCode = article.Code,
                ReferencedByUserId = command.CurrentUserId,
                ReferenceType = command.ReferenceType,
                Note = command.Note
            };

            await _uow.TicketKbReferences.AddAsync(reference);
        }

        await _uow.SaveChangesAsync(ct);

        return new CommonResponse<object> { IsSuccess = true, StatusCode = 200, Message = "Article attached to Ticket successfully." };
    }

    private static CommonResponse<object> Fail(int statusCode, string message)
    {
        return new CommonResponse<object> { IsSuccess = false, StatusCode = statusCode, Message = message };
    }
}
