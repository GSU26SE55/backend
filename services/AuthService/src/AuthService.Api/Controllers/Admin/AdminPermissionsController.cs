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
[Route("api/admin/permissions")]
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
    /// <response code="200">Lấy danh sách permission thành công.</response>
    /// <response code="403">Không có role Admin.</response>
    [HttpGet]
    [ProducesResponseType(typeof(PermissionListResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAllPermissions(
        [FromQuery] string? module = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(new GetAllPermissionsQuery { Module = module }, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Lấy danh sách permission hiện gán cho 1 role.
    /// </summary>
    /// <response code="200">Lấy danh sách thành công.</response>
    /// <response code="404">Role không tồn tại.</response>
    [HttpGet("{roleId:guid}/roles")]
    [ProducesResponseType(typeof(RolePermissionsResponse), StatusCodes.Status200OK)]
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
    [HttpPut("{roleId:guid}/roles")]
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
