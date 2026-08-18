using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using SharedInfrastructure.Extensions;
using SharedInfrastructure.Services;
using TicketService.Application.Common.Utils;
using TicketService.Application.CQRS.Query.Ticket;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Interfaces.Utils;

namespace TicketService.Application.CQRS.Handler.Ticket;

public class MyTicketsAsCustomerQueryHandler : IRequestHandler<MyTicketsAsCustomerQuery, CommonResponse<PaginationResponse<TicketDTO>>>
{
    private readonly ITicketUnitOfWork _unitOfWork;
    private readonly ICurrentUserService _currentUserService;
    private readonly ISlaCalculator _slaCalculator;

    public MyTicketsAsCustomerQueryHandler(
        ITicketUnitOfWork unitOfWork,
        ICurrentUserService currentUserService,
        ISlaCalculator slaCalculator)
    {
        _unitOfWork = unitOfWork;
        _currentUserService = currentUserService;
        _slaCalculator = slaCalculator;
    }

    public async Task<CommonResponse<PaginationResponse<TicketDTO>>> Handle(MyTicketsAsCustomerQuery request, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(_currentUserService.UserId, out var customerId))
        {
            return new CommonResponse<PaginationResponse<TicketDTO>>
            {
                IsSuccess = false,
                StatusCode = 401,
                Message = "Not logged in."
            };
        }

        var query = _unitOfWork.Tickets.GetAllAsync()
            .AsNoTracking()
            .Include(t => t.SlaTimer)
            .Include(t => t.BatteryAssets)
            // Thiếu Include này thì t.Assignments luôn rỗng → mọi card danh sách
            // đều hiện "Chưa phân công" dù ticket đã gán Staff (detail thì đúng
            // vì TicketGetByIdQueryHandler có Include). Lọc !IsDeleted như bên detail.
            .Include(t => t.Assignments.Where(a => !a.IsDeleted))
            .Where(t => !t.IsDeleted && t.CustomerId == customerId);

        if (request.Status.HasValue)
            query = query.Where(t => t.Status == request.Status.Value);

        query = query.OrderByDescending(t => t.CreatedAt)
            .ThenBy(t => t.Id); // tie-breaker cố định — pagination ổn định

        // Phân trang trên entity: sau đó còn phải truy vấn phụ (chat chưa đọc) rồi mới dựng DTO,
        // nên không chiếu sang DTO trong SQL được.
        var page = await query.ToPagedEntityListAsync(request.PageNumber, request.PageSize, cancellationToken);
        var rawItems = page.Items;

        var ticketIds = rawItems.Select(t => t.Id).ToList();
        HashSet<Guid> unreadTicketIds;
        if (ticketIds.Count == 0)
        {
            unreadTicketIds = new HashSet<Guid>();
        }
        else
        {
            var readChatIds = _unitOfWork.TicketChatReads.GetAllAsync().AsNoTracking()
                .Where(r => r.UserId == customerId && !r.IsDeleted).Select(r => r.ChatId);
            var unreadList = await _unitOfWork.TicketChats.GetAllAsync().AsNoTracking()
                .Where(c => ticketIds.Contains(c.TicketId) && !c.IsDeleted
                    && c.AuthorUserId != customerId && !c.IsInternal
                    && !readChatIds.Contains(c.Id))
                .Select(c => c.TicketId).Distinct()
                .ToListAsync(cancellationToken);
            unreadTicketIds = unreadList.ToHashSet();
        }

        // Tên staff cho phần "phụ trách": gom StaffId của cả trang rồi tra 1 lần
        // (không phải mỗi ticket 1 query). Nhờ vậy MỌI role đọc được tên mà không
        // cần gọi /api/staff — endpoint đó chỉ mở cho Admin/Manager.
        var staffIds = page.Items
            .SelectMany(t => t.Assignments.Where(a => !a.IsDeleted).Select(a => a.StaffId))
            .Distinct()
            .ToList();
        var staffNames = staffIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _unitOfWork.StaffAccounts.GetAllAsync().AsNoTracking()
                .Where(s => staffIds.Contains(s.AccountId) && !s.IsDeleted)
                .ToDictionaryAsync(s => s.AccountId, s => s.FullName, cancellationToken);

        return new CommonResponse<PaginationResponse<TicketDTO>>
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = page.Map(t => TicketQueryHelper.MapToTicketDTO(
                t, _slaCalculator, DateTime.UtcNow, unreadTicketIds.Contains(t.Id), staffNames))
        };
    }
}
