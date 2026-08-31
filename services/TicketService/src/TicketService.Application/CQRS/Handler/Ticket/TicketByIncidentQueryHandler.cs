using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using TicketService.Application.Common.Utils;
using TicketService.Application.CQRS.Query.Ticket;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Interfaces.Utils;

namespace TicketService.Application.CQRS.Handler.Ticket;

public class TicketByIncidentQueryHandler
    : IRequestHandler<TicketByIncidentQuery, CommonResponse<TicketDTO?>>
{
    private readonly ITicketUnitOfWork _unitOfWork;
    private readonly ISlaCalculator _slaCalculator;

    public TicketByIncidentQueryHandler(ITicketUnitOfWork unitOfWork, ISlaCalculator slaCalculator)
    {
        _unitOfWork = unitOfWork;
        _slaCalculator = slaCalculator;
    }

    public async Task<CommonResponse<TicketDTO?>> Handle(
        TicketByIncidentQuery request, CancellationToken ct)
    {
        // Consumer tạo ticket là idempotent theo IncidentId nên thực tế chỉ có một; lấy cái cũ
        // nhất để nếu dữ liệu cũ có trùng thì vẫn trả về ticket gốc chứ không phải cái ngẫu nhiên.
        var ticket = await _unitOfWork.Tickets.GetAllAsync()
            .AsNoTracking()
            .Include(t => t.SlaTimer)
            .Include(t => t.BatteryAssets)
            .Include(t => t.Assignments.Where(a => !a.IsDeleted))
            .Where(t => !t.IsDeleted
                        && t.EnvironmentalIncidentId == request.EnvironmentalIncidentId)
            .OrderBy(t => t.CreatedAt)
            .FirstOrDefaultAsync(ct);

        return new CommonResponse<TicketDTO?>
        {
            IsSuccess = true,
            StatusCode = 200,
            // Không có ticket KHÔNG phải lỗi: sự cố có thể vừa xảy ra và consumer chưa chạy xong.
            Data = ticket is null
                ? null
                : TicketQueryHelper.MapToTicketDTO(ticket, _slaCalculator, DateTime.UtcNow)
        };
    }
}
