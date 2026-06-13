using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using TicketService.Application.CQRS.Query.Ticket;
using TicketService.Application.DTOs.Response.Ticket;
using TicketService.Application.Helpers;
using TicketService.Application.Interfaces.Repositories;

namespace TicketService.Application.CQRS.Handler.Comments;

public class TicketCommentsQueryHandler : IRequestHandler<TicketCommentsQuery, CommonResponse<PaginationResponse<TicketCommentDTO>>>
{
    private readonly ITicketUnitOfWork _unitOfWork;

    public TicketCommentsQueryHandler(ITicketUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<CommonResponse<PaginationResponse<TicketCommentDTO>>> Handle(TicketCommentsQuery request, CancellationToken cancellationToken)
    {
        // 1. Kiểm tra ticket có tồn tại không và check quyền truy cập ticket
        var ticket = await _unitOfWork.Tickets.GetAllAsync()
            .AsNoTracking()
            .Where(t => t.Id == request.TicketId && !t.IsDeleted)
            .Select(t => new { t.CustomerId, t.AssignedStaffId })
            .FirstOrDefaultAsync(cancellationToken);

        if (ticket is null)
            return new CommonResponse<PaginationResponse<TicketCommentDTO>> { IsSuccess = false, StatusCode = 404, Message = "Ticket not found" };

        if (!TicketQueryHelper.CanAccessTicket(ticket.CustomerId, ticket.AssignedStaffId, request.ActorUserId, request.ActorRoles))
            return new CommonResponse<PaginationResponse<TicketCommentDTO>> { IsSuccess = false, StatusCode = 403, Message = "Forbidden" };

        // 2. Xác định xem actor có quyền xem comment nội bộ không
        var canViewInternalComments = TicketQueryHelper.CanViewInternalComments(request.ActorRoles);

        // 3. Query comments
        var query = _unitOfWork.TicketComments.GetAllAsync()
            .AsNoTracking()
            .Where(c => c.TicketId == request.TicketId && !c.IsDeleted);

        // 4. Lọc comment nội bộ nếu là Customer
        if (!canViewInternalComments)
        {
            query = query.Where(c => !c.IsInternal);
        }

        var total = await query.CountAsync(cancellationToken);
        var rawComments = await query
            .OrderByDescending(c => c.CreatedAt)
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        // 5. Lấy danh sách PublicUrl từ TicketAttachments dựa trên FileIds
        var allFileIds = rawComments.SelectMany(c => c.AttachmentFileIds).Distinct().ToList();
        var attachmentsMap = new Dictionary<Guid, string>();

        if (allFileIds.Any())
        {
            var attachmentData = await _unitOfWork.TicketAttachments.GetAllAsync()
                .AsNoTracking()
                .Where(a => allFileIds.Contains(a.FileId) && a.TicketId == request.TicketId)
                .Select(a => new { a.FileId, a.PublicUrl })
                .ToListAsync(cancellationToken);

            attachmentsMap = attachmentData
                .Where(a => !string.IsNullOrEmpty(a.PublicUrl))
                .GroupBy(a => a.FileId)
                .ToDictionary(g => g.Key, g => g.First().PublicUrl!);
        }

        var items = rawComments.Select(c => new TicketCommentDTO
        {
            Id = c.Id.ToString(),
            TicketId = c.TicketId.ToString(),
            AuthorUserId = c.AuthorUserId.ToString(),
            AuthorRole = c.AuthorRole,
            AuthorDisplayName = c.AuthorDisplayName,
            Body = c.Body,
            IsInternal = c.IsInternal,
            AttachmentUrls = c.AttachmentFileIds
                .Select(id => attachmentsMap.TryGetValue(id, out var url) ? url : null)
                .Where(url => !string.IsNullOrEmpty(url))
                .Select(url => url!)
                .ToList(),
            CreatedAt = c.CreatedAt
        }).ToList();

        return new CommonResponse<PaginationResponse<TicketCommentDTO>>
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = new PaginationResponse<TicketCommentDTO>
            {
                Items = items,
                TotalItems = total,
                PageNumber = request.PageNumber,
                PageSize = request.PageSize
            }
        };
    }
}
