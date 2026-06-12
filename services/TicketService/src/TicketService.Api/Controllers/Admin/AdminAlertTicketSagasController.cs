using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TicketService.Application.CQRS.Command.Sagas;
using TicketService.Application.CQRS.Query.Sagas;
using TicketService.Application.DTOs.Response.Saga;
using TicketService.Application.Interfaces.Services;

namespace TicketService.Api.Controllers.Admin;

/// <summary>
/// Admin endpoints cho Alert–Ticket Saga (Sprint 5B #239 — xem overall.md §53.11).
///
/// Authorization:
/// - GET endpoints: permission <c>ticket.saga.view</c> (Admin + Manager read-only).
/// - POST reprocess: permission <c>ticket.saga.reprocess</c> (Admin only).
/// </summary>
[ApiController]
[Route("api/v1/admin/sagas/alert-ticket")]
[Authorize(Roles = "Admin,Manager")]
[Produces("application/json")]
public class AdminAlertTicketSagasController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ITicketCurrentUserService _currentUser;

    public AdminAlertTicketSagasController(IMediator mediator, ITicketCurrentUserService currentUser)
    {
        _mediator = mediator;
        _currentUser = currentUser;
    }

    /// <summary>
    /// List trạng thái Alert–Ticket Saga (filter + phân trang).
    /// </summary>
    /// <remarks>
    /// Query parameters hỗ trợ filter theo:
    /// <list type="bullet">
    ///   <item><description><c>State</c>: <c>Pending</c> | <c>InProgress</c> | <c>Completed</c> | <c>Failed</c>.</description></item>
    ///   <item><description><c>From</c> / <c>To</c>: range theo <c>CreatedAt</c> UTC.</description></item>
    ///   <item><description><c>AlertId</c>: tìm Saga của 1 alert cụ thể.</description></item>
    ///   <item><description><c>PageNumber</c> / <c>PageSize</c>: phân trang (mặc định 20/page).</description></item>
    /// </list>
    ///
    /// Sort mặc định theo <c>CreatedAt DESC</c> — Saga mới nhất lên đầu.
    /// </remarks>
    /// <param name="query">Filter + phân trang.</param>
    /// <param name="ct">Token hủy request.</param>
    /// <returns><see cref="AlertTicketSagaListResponse"/>.</returns>
    /// <response code="200">Trả danh sách Saga.</response>
    /// <response code="401">Chưa đăng nhập.</response>
    /// <response code="403">Không có role Admin/Manager.</response>
    [HttpGet]
    [ProducesResponseType(typeof(AlertTicketSagaListResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetList([FromQuery] GetAlertTicketSagasQuery query, CancellationToken ct)
    {
        var result = await _mediator.Send(query, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Chi tiết Saga state theo <paramref name="alertId"/>.
    /// </summary>
    /// <remarks>
    /// Trả đầy đủ trạng thái Saga + lịch sử bước thực thi (step name, status, retry count, error message).
    /// Dùng để debug khi Saga rơi vào state <c>Failed</c>.
    /// </remarks>
    /// <param name="alertId">Id của alert gốc.</param>
    /// <param name="ct">Token hủy request.</param>
    /// <returns><see cref="AlertTicketSagaDetailResponse"/>.</returns>
    /// <response code="200">Trả chi tiết Saga.</response>
    /// <response code="401">Chưa đăng nhập.</response>
    /// <response code="403">Không có role Admin/Manager.</response>
    /// <response code="404">Không tìm thấy Saga cho <paramref name="alertId"/>.</response>
    [HttpGet("{alertId:guid}")]
    [ProducesResponseType(typeof(AlertTicketSagaDetailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid alertId, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetAlertTicketSagaByIdQuery { AlertId = alertId }, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Reprocess Saga đang ở state <c>Failed</c> — chỉ Admin.
    /// </summary>
    /// <remarks>
    /// Yêu cầu header <c>Idempotency-Key</c> bắt buộc để chống double-trigger trong race condition.
    ///
    /// Cách hoạt động:
    /// <list type="bullet">
    ///   <item><description>Validate Saga đang ở state <c>Failed</c> — state khác trả 400.</description></item>
    ///   <item><description>Check <c>Idempotency-Key</c> trong inbox — nếu đã xử lý trước đó → trả 409 (kèm response cũ).</description></item>
    ///   <item><description>Reset bước Failed và enqueue lại — Saga chạy bất đồng bộ trong background, response trả 202 ngay.</description></item>
    ///   <item><description>Ghi audit <c>PerformedBy</c> = user hiện tại.</description></item>
    /// </list>
    /// </remarks>
    /// <param name="alertId">Id của alert thuộc Saga cần reprocess.</param>
    /// <param name="idempotencyKey">Header <c>Idempotency-Key</c> — chuỗi unique cho mỗi lần gọi (vd GUID client gen).</param>
    /// <param name="ct">Token hủy request.</param>
    /// <returns><see cref="SagaReprocessResponse"/>.</returns>
    /// <response code="202">Đã accept và enqueue reprocess.</response>
    /// <response code="400">Thiếu <c>Idempotency-Key</c> hoặc Saga không ở state <c>Failed</c>.</response>
    /// <response code="401">Chưa đăng nhập.</response>
    /// <response code="403">Không có role Admin.</response>
    /// <response code="409">Idempotency-Key đã được xử lý trước đó.</response>
    [HttpPost("{alertId:guid}/reprocess")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(SagaReprocessResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Reprocess(
        Guid alertId,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken ct)
    {
        var cmd = new ReprocessSagaCommand
        {
            AlertId = alertId,
            IdempotencyKey = idempotencyKey ?? string.Empty,
            PerformedBy = Guid.TryParse(_currentUser.UserId, out var uid) ? uid : Guid.Empty
        };

        // ValidationBehavior pipeline tự bắt thiếu Idempotency-Key + AlertId qua command.ValidateAsync.
        var result = await _mediator.Send(cmd, ct);
        return StatusCode(result.StatusCode, result);
    }
}
