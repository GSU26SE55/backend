using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using TicketService.Application.Common.Utils;
using TicketService.Application.CQRS.Query.Ticket;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Interfaces.Utils;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Handler.Ticket;

public class TicketRelatedQueryHandler
    : IRequestHandler<TicketRelatedQuery, CommonResponse<List<TicketDTO>>>
{
    private readonly ITicketUnitOfWork _unitOfWork;
    private readonly ISlaCalculator _slaCalculator;

    /// Trần cứng: panel này là "cái nhìn tổng quan", không phải danh sách phân trang. Site nào
    /// vượt mức này thì vấn đề nằm ở chỗ khác, và người trực cần trang ticket chứ không phải
    /// một danh sách dài trong ticket.
    private const int MaxRelated = 20;

    /// Ticket đã kết thúc thì không còn là việc phải làm — panel chỉ liệt kê việc đang mở.
    private static readonly TicketStatusEnum[] TerminalStatuses =
    [
        TicketStatusEnum.Completed,
        TicketStatusEnum.Closed,
        TicketStatusEnum.ClosedRejected
    ];

    public TicketRelatedQueryHandler(ITicketUnitOfWork unitOfWork, ISlaCalculator slaCalculator)
    {
        _unitOfWork = unitOfWork;
        _slaCalculator = slaCalculator;
    }

    public async Task<CommonResponse<List<TicketDTO>>> Handle(
        TicketRelatedQuery request, CancellationToken ct)
    {
        var ticket = await _unitOfWork.Tickets.GetAllAsync()
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == request.Id && !t.IsDeleted, ct);

        if (ticket is null)
            return new CommonResponse<List<TicketDTO>>
            {
                IsSuccess = false,
                StatusCode = 404,
                Message = "Ticket not found."
            };

        var query = _unitOfWork.Tickets.GetAllAsync()
            .AsNoTracking()
            .Include(t => t.SlaTimers)
            .Include(t => t.BatteryAssets)
            // Thiếu Include này thì Assignments rỗng và mọi dòng hiện "chưa phân công" —
            // cùng cái bẫy đã sửa ở ManagerQueueQueryHandler.
            .Include(t => t.Assignments.Where(a => !a.IsDeleted))
            .Where(t => !t.IsDeleted
                        && t.Id != ticket.Id
                        && t.MergedIntoTicketId == null
                        && !TerminalStatuses.Contains(t.Status));

        // Hai nguồn "liên quan", gộp bằng OR chứ không phải hai truy vấn:
        //   - link cha–con đã được Manager xác nhận (đi cả hai chiều);
        //   - cùng site và còn mở — ứng viên để Manager quyết có link hay không.
        // Cùng site chỉ áp khi ticket gốc BIẾT site của nó; SiteId null (ticket cũ, hoặc
        // auto-from-alert chưa điền được) thì chỉ còn nhánh link.
        var siteId = ticket.SiteId;
        query = siteId.HasValue
            ? query.Where(t => t.SiteId == siteId.Value
                               || t.ParentTicketId == ticket.Id
                               || (ticket.ParentTicketId != null && t.Id == ticket.ParentTicketId))
            : query.Where(t => t.ParentTicketId == ticket.Id
                               || (ticket.ParentTicketId != null && t.Id == ticket.ParentTicketId));

        var items = await query
            // Ticket đã link lên đầu — đó là quan hệ đã được xác nhận, phần còn lại mới là
            // ứng viên. Sau đó theo mức nghiêm trọng rồi tới cũ nhất, khớp thứ tự hàng chờ.
            .OrderBy(t => t.ParentTicketId == ticket.Id || t.Id == ticket.ParentTicketId ? 0 : 1)
            .ThenBy(TicketQueryHelper.PriorityRank)
            .ThenBy(t => t.CreatedAt)
            .ThenBy(t => t.Id)
            .Take(MaxRelated)
            .ToListAsync(ct);

        var now = DateTime.UtcNow;
        return new CommonResponse<List<TicketDTO>>
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = items
                .Select(t => TicketQueryHelper.MapToTicketDTO(t, _slaCalculator, now))
                .ToList()
        };
    }
}
