using AuthService.Application.CQRS.Command.Permission;
using AuthService.Application.CQRS.Query.Permission;
using AuthService.Application.DTOs.Response.Permission;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AuthService.Api.Controllers.Admin;

/// <summary>
/// Module admin quản lý Permission system + assignment role↔permission.
/// </summary>
[ApiController]
[Route("api/admin")]
[Produces("application/json")]
[ApiExplorerSettings(GroupName = "admin")]
[Authorize(Roles = "Admin")]
public class AdminPermissionsController : ControllerBase
{
    private readonly IMediator _mediator;

    public AdminPermissionsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Danh sách tất cả permission trong hệ thống. Filter optional theo module.
    /// </summary>
    /// <remarks>
    /// Trả flat list <c>PermissionDto</c> sort theo <c>Code</c>. Mỗi entry có:
    /// <list type="bullet">
    ///   <item><description><c>Id</c>: Guid identifier.</description></item>
    ///   <item><description><c>Code</c>: dot-separated key (vd <c>"ticket.view"</c>, <c>"alert.acknowledge"</c>) — match với <c>P.*</c> constant ở FE.</description></item>
    ///   <item><description><c>Module</c>: namespace logic (Ticket / Alert / Battery / Account / Admin) — filter qua <c>?module=Ticket</c>.</description></item>
    ///   <item><description><c>Description</c>: mô tả tiếng Việt cho UI gán permission.</description></item>
    /// </list>
    ///
    /// Use case:
    /// <list type="bullet">
    ///   <item><description>Admin dashboard render dropdown "chọn permission" khi tạo/edit role.</description></item>
    ///   <item><description>FE migration check — đảm bảo <c>P.*</c> constants khớp với DB.</description></item>
    /// </list>
    /// </remarks>
    /// <param name="module">Filter theo module name (Ticket/Alert/Battery/...). Để trống = trả tất cả.</param>
    /// <param name="cancellationToken">Token hủy request.</param>
    /// <response code="200">Lấy danh sách permission thành công.</response>
    /// <response code="401">Chưa đăng nhập / token hết hạn.</response>
    /// <response code="403">Không có role Admin.</response>
    [HttpGet("permissions")]
    [ProducesResponseType(typeof(PermissionListResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetAllPermissions(
        [FromQuery] string? module = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(new GetAllPermissionsQuery { Module = module }, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Lấy danh sách permission hiện được gán cho 1 role cụ thể.
    /// </summary>
    /// <remarks>
    /// Trả về <c>RolePermissionsResponse</c> gồm:
    /// <list type="bullet">
    ///   <item><description><c>RoleId</c> + <c>RoleName</c></description></item>
    ///   <item><description><c>Permissions</c>: list <c>PermissionDto</c> đã gán (sort theo Code).</description></item>
    /// </list>
    ///
    /// Use case: Admin edit role — load list current permissions để pre-check checkbox UI.
    /// Để thay đổi gán, gọi <c>PUT /roles/{roleId}/permissions</c> với toàn bộ list mới (replace semantics).
    /// </remarks>
    /// <param name="roleId">Guid role.</param>
    /// <param name="cancellationToken">Token hủy request.</param>
    /// <response code="200">Lấy danh sách thành công.</response>
    /// <response code="401">Chưa đăng nhập / token hết hạn.</response>
    /// <response code="403">Không có role Admin.</response>
    /// <response code="404">Role không tồn tại.</response>
    [HttpGet("roles/{roleId:guid}/permissions")]
    [ProducesResponseType(typeof(RolePermissionsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(RolePermissionsResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetRolePermissions(
        Guid roleId,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(new GetRolePermissionsQuery { RoleId = roleId }, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Set TOÀN BỘ permission cho 1 role (replace semantics).
    /// </summary>
    /// <remarks>
    /// - PermissionIds nào KHÔNG có trong list hiện tại → ADD.
    /// - PermissionIds nào HIỆN CÓ nhưng KHÔNG truyền → REMOVE (soft delete).
    /// - System role (Admin/Manager/Staff/Customer) bị chặn mặc định, set <c>allowSystemRole=true</c> trong body để override.
    ///
    /// User sẽ nhận permission update qua claim trong lần issue JWT tiếp theo (login/refresh).
    /// </remarks>
    /// <response code="200">Set permission thành công.</response>
    /// <response code="400">Permission không tồn tại / Cố modify system role mà không allowSystemRole.</response>
    /// <response code="404">Role không tồn tại.</response>
    [HttpPut("roles/{roleId:guid}/permissions")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(PermissionActionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(PermissionActionResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(PermissionActionResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetRolePermissions(
        Guid roleId,
        [FromBody] SetRolePermissionsCommand command,
        CancellationToken cancellationToken)
    {
        command.RoleId = roleId;
        var result = await _mediator.Send(command, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }
}
