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
            return Fail(404, "Không tìm thấy Ticket.");

        // KIỂM TRA PHÂN QUYỀN:
        // - Admin/Manager được gán bài viết cho bất kỳ ticket nào.
        // - Staff phải là người được gán vào Ticket (AssignedStaffId == CurrentUserId).
        // - Các trường hợp khác bị chặn.
        var userRole = _currentUser.Role;
        if (userRole != "Admin" && userRole != "Manager")
        {
            if (userRole == "Staff")
            {
                if (ticket.AssignedStaffId != command.CurrentUserId)
                {
                    return Fail(403, "Chỉ nhân viên kỹ thuật được phân công xử lý Ticket này mới được phép gán tài liệu tham khảo.");
                }
            }
            else
            {
                return Fail(403, "Bạn không có quyền thực hiện hành động này.");
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
            return Fail(409, "Ticket đã ở trạng thái chờ phê duyệt hoặc đã hoàn thành. Không thể gán thêm tài liệu tham khảo.");
        }

        var article = await _uow.KnowledgeBaseArticles.GetAllAsync()
            .FirstOrDefaultAsync(a => a.Id == command.KbArticleId, ct);
        if (article == null)
            return Fail(404, "Không tìm thấy bài viết Knowledge Base.");

        // HÀNG RÀO NGHIỆP VỤ: bài viết nội bộ (IsInternalOnly) không được cung cấp cho khách hàng.
        // 422: request đúng format nhưng dữ liệu vi phạm rule nghiệp vụ (không phải lỗi quyền/field).
        if (command.ReferenceType == KbReferenceTypeEnum.ProvidedToCustomer && article.IsInternalOnly)
            return Fail(422, "Bài viết nội bộ không thể gán với loại tham chiếu 'Cung cấp cho khách hàng'.");

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

        return new CommonResponse<object> { IsSuccess = true, StatusCode = 200, Message = "Đã gán bài viết vào Ticket thành công." };
    }

    private static CommonResponse<object> Fail(int statusCode, string message)
    {
        return new CommonResponse<object> { IsSuccess = false, StatusCode = statusCode, Message = message };
    }
}
