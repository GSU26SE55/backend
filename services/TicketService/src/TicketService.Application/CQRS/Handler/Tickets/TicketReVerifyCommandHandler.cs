using MediatR;
using SharedContracts.Common.Responses;
using TicketService.Application.CQRS.Command.Tickets;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Interfaces.Services;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Handler.Tickets;

/// <summary>
/// Manager kích hoạt AI kiểm tra lại: reset Pending rồi gọi TicketVerifyRunner NGAY (đồng bộ) —
/// không qua event (ticket đã tồn tại, không race). Chỉ ticket manual đang Skipped/Pending.
/// </summary>
public class TicketReVerifyCommandHandler : IRequestHandler<TicketReVerifyCommand, TicketActionResponse>
{
    private readonly ITicketUnitOfWork _uow;
    private readonly ITicketVerifyRunner _runner;

    public TicketReVerifyCommandHandler(ITicketUnitOfWork uow, ITicketVerifyRunner runner)
    {
        _uow = uow;
        _runner = runner;
    }

    public async Task<TicketActionResponse> Handle(TicketReVerifyCommand request, CancellationToken ct)
    {
        var ticket = await _uow.Tickets.GetByIdAsync(request.TicketId);
        if (ticket is null || ticket.IsDeleted)
            return Fail(404, "Không tìm thấy ticket.");

        if (ticket.Origin != TicketOriginEnum.ManualByCustomer)
            return Fail(400, "Chỉ ticket do khách hàng tạo mới có AI kiểm tra.");

        if (ticket.AiVerifyStatus != TicketVerifyStatusEnum.Skipped
            && ticket.AiVerifyStatus != TicketVerifyStatusEnum.Pending)
            return Fail(400, "Ticket đã được AI đánh giá — không cần kiểm tra lại.");

        // Reset về Pending + xóa kết quả cũ (runner chỉ chạy khi Pending), rồi verify NGAY.
        ticket.AiVerifyStatus = TicketVerifyStatusEnum.Pending;
        ticket.AiVerifyScore = null;
        ticket.AiVerifyReason = null;
        ticket.SuspectedDuplicateOfTicketId = null;
        ticket.DuplicateReason = null;
        _uow.Tickets.UpdateAsync(ticket);
        await _uow.SaveChangesAsync(ct);

        // Verify đồng bộ ngay (ticket đã commit ở trên, cùng DbContext scope).
        await _runner.RunAsync(ticket.Id, ct);

        return new TicketActionResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Đã kiểm tra lại bằng AI.",
            Data = new TicketActionDTO
            {
                Id = ticket.Id.ToString(),
                TicketId = ticket.Id.ToString(),
                Code = ticket.Code,
                Status = ticket.Status
            }
        };
    }

    private static TicketActionResponse Fail(int statusCode, string message) => new()
    {
        IsSuccess = false,
        StatusCode = statusCode,
        Message = message
    };
}
