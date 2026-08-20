using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SharedContracts.Common.Responses;
using TicketService.Application.CQRS.Command.Tickets;
using TicketService.Application.CQRS.Query.Ticket;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Application.Interfaces.Services;

namespace TicketService.Api.Controllers;

/// <summary>
/// Controller dành riêng cho Khách hàng (Customer) quản lý Ticket của mình.
/// </summary>
[ApiController]
[Route("api/customer/tickets")]
[Authorize(Roles = "Customer")]
[Produces("application/json")]
public class CustomerTicketsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ITicketCurrentUserService _currentUser;

    public CustomerTicketsController(IMediator mediator, ITicketCurrentUserService currentUser)
    {
        _mediator = mediator;
        _currentUser = currentUser;
    }

    /// <summary>
    /// Khách hàng lấy danh sách ticket của chính mình (filter theo status/priority) — auto-filter CustomerId từ JWT, sort theo CreatedAt DESC mặc định.
    /// </summary>
    /// <remarks>
    /// Các tham số lọc:
    /// - <c>Status</c>: Trạng thái ticket.
    /// - <c>PageIndex</c>, <c>PageSize</c>: Phân trang.
    ///
    /// Hệ thống tự động lọc theo ID khách hàng từ Token.
    /// </remarks>
    /// <param name="query">Tiêu chí lọc và phân trang.</param>
    /// <param name="ct">Token hủy request.</param>
    /// <response code="200">Lấy danh sách thành công.</response>
    /// <response code="401">Chưa đăng nhập.</response>
    [HttpGet("me")]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(CommonResponse<PaginationResponse<TicketDTO>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetMyTickets([FromQuery] MyTicketsAsCustomerQuery query, CancellationToken ct)
    {
        var result = await _mediator.Send(query, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Khách hàng tạo ticket mới để yêu cầu hỗ trợ hoặc sửa chữa.
    /// </summary>
    /// <remarks>
    /// - <c>Title</c>, <c>Description</c>: Bắt buộc.
    /// - <c>Category</c>: Charging, Overheat, NoPower, Performance, Other, Repair.
    /// - <c>BatteryAssetId</c>: ID thiết bị (tùy chọn).
    /// </remarks>
    /// <param name="command">Thông tin ticket mới.</param>
    /// <param name="ct">Token hủy request.</param>
    /// <response code="201">Tạo thành công.</response>
    /// <response code="400">Dữ liệu không hợp lệ.</response>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(TicketActionResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(TicketActionResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] TicketCreateCommand command, CancellationToken ct)
    {
        command.CustomerId = string.IsNullOrEmpty(_currentUser.UserId) ? Guid.Empty : Guid.Parse(_currentUser.UserId);
        var result = await _mediator.Send(command, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Khách hàng yêu cầu mở lại ticket khi chưa hài lòng với kết quả xử lý.
    /// </summary>
    /// <remarks>
    /// - Chỉ áp dụng cho ticket <c>Closed</c>, chưa đánh giá và không phải ticket đã merge.
    /// - Phải trong vòng 7 ngày kể từ khi ticket được phê duyệt.
    /// - Giữ nguyên ticket và lịch sử; trạng thái quay về <c>Open</c>.
    /// </remarks>
    /// <param name="id">ID của Ticket.</param>
    /// <param name="command">Lý do mở lại.</param>
    /// <param name="ct">Token hủy request.</param>
    /// <response code="200">Yêu cầu thành công.</response>
    /// <response code="403">Không đủ điều kiện (quá hạn, sai trạng thái).</response>
    [HttpPost("{id}/reopen")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(TicketActionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(TicketActionResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Reopen(Guid id, [FromBody] TicketReopenCommand command, CancellationToken ct)
    {
        command.TicketId = id;
        command.CustomerId = string.IsNullOrEmpty(_currentUser.UserId) ? Guid.Empty : Guid.Parse(_currentUser.UserId);
        command.CustomerName = _currentUser.FullName!;

        var result = await _mediator.Send(command, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Khách hàng đánh giá chất lượng xử lý của ticket đã đóng.
    /// </summary>
    /// <remarks>
    /// - Chỉ áp dụng cho ticket <c>Closed</c>, chưa đánh giá và không phải ticket đã merge.
    /// - Điểm đánh giá (Rating) từ 1-5 sao.
    /// </remarks>
    /// <param name="id">ID của Ticket.</param>
    /// <param name="command">Thông tin đánh giá.</param>
    /// <param name="ct">Token hủy request.</param>
    /// <response code="200">Đánh giá và đóng ticket thành công.</response>
    /// <response code="403">Sai trạng thái ticket.</response>
    [HttpPost("{id}/rate")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(TicketActionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(TicketActionResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Rate(Guid id, [FromBody] TicketRateCommand command, CancellationToken ct)
    {
        command.TicketId = id;
        command.CustomerId = string.IsNullOrEmpty(_currentUser.UserId) ? Guid.Empty : Guid.Parse(_currentUser.UserId);
        command.CustomerName = _currentUser.FullName!;

        var result = await _mediator.Send(command, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost("{id:guid}/periodic-maintenance/schedule")]
    [ProducesResponseType(typeof(TicketActionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(TicketActionResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(TicketActionResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(TicketActionResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SchedulePeriodicMaintenance(
        Guid id,
        [FromBody] CustomerSchedulePeriodicMaintenanceCommand command,
        CancellationToken ct)
    {
        command.TicketId = id;
        command.CustomerId = string.IsNullOrEmpty(_currentUser.UserId)
            ? Guid.Empty
            : Guid.Parse(_currentUser.UserId);
        var result = await _mediator.Send(command, ct);
        return StatusCode(result.StatusCode, result);
    }
}
