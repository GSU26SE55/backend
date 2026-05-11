using AuthService.Application.CQRS.Query.Audit;
using AuthService.Application.DTOs.Response.Audit;
using AuthService.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AuthService.Api.Controllers.Admin;

/// <summary>
/// Module admin xem audit log: lịch sử mọi hành động nhạy cảm trên hệ thống Auth.
/// Audit log là append-only, không có endpoint update/delete.
/// </summary>
[ApiController]
[Route("api/admin/audit-logs")]
[Produces("application/json")]
[ApiExplorerSettings(GroupName = "admin")]
[Authorize(Roles = "Admin")]
public class AdminAuditLogsController : ControllerBase
{
    private readonly IMediator _mediator;

    public AdminAuditLogsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Danh sách audit log có phân trang và filter nâng cao.
    /// </summary>
    /// <remarks>
    /// Endpoint chỉ dành cho Admin. Trả về lịch sử các hành động nhạy cảm: login (success/fail),
    /// đổi mật khẩu, gán/thu hồi role, đổi trạng thái account, force logout, ...
    ///
    /// Use case tiêu biểu:
    /// - Xem mọi hoạt động đụng đến 1 account cụ thể: <c>?targetAccountId={guid}</c>.
    /// - Audit hoạt động của 1 admin: <c>?actorAccountId={guid}</c>.
    /// - Điều tra incident bảo mật: <c>?action=TokenReuseDetected</c> hoặc <c>?isSuccess=false</c>.
    /// - Báo cáo trong khoảng thời gian: <c>?fromUtc=2026-05-01T00:00:00Z&amp;toUtc=2026-05-08T00:00:00Z</c>.
    ///
    /// Trả về sort theo CreatedAt giảm dần (mới nhất trước).
    /// </remarks>
    /// <response code="200">Lấy danh sách audit log thành công.</response>
    /// <response code="400">Filter không hợp lệ (FromUtc >= ToUtc).</response>
    /// <response code="401">Chưa đăng nhập.</response>
    /// <response code="403">Không có role Admin.</response>
    [HttpGet]
    [ProducesResponseType(typeof(AuditLogListResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(AuditLogListResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetAll(
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] AuditActionEnum? action = null,
        [FromQuery] Guid? targetAccountId = null,
        [FromQuery] Guid? actorAccountId = null,
        [FromQuery] bool? isSuccess = null,
        [FromQuery] DateTime? fromUtc = null,
        [FromQuery] DateTime? toUtc = null,
        CancellationToken cancellationToken = default)
    {
        var query = new GetAuditLogsQuery
        {
            PageNumber = pageNumber,
            PageSize = pageSize,
            Action = action,
            TargetAccountId = targetAccountId,
            ActorAccountId = actorAccountId,
            IsSuccess = isSuccess,
            FromUtc = fromUtc,
            ToUtc = toUtc
        };

        var result = await _mediator.Send(query, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Lịch sử audit log của 1 account cụ thể (shortcut của endpoint chính với targetAccountId cố định).
    /// </summary>
    [HttpGet("by-account/{accountId:guid}")]
    [ProducesResponseType(typeof(AuditLogListResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetByAccount(
        Guid accountId,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] AuditActionEnum? action = null,
        [FromQuery] bool? isSuccess = null,
        CancellationToken cancellationToken = default)
    {
        var query = new GetAuditLogsQuery
        {
            PageNumber = pageNumber,
            PageSize = pageSize,
            TargetAccountId = accountId,
            Action = action,
            IsSuccess = isSuccess
        };

        var result = await _mediator.Send(query, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }
}
