using AuthService.Application.CQRS.Query.Permission;
using AuthService.Application.DTOs.Response.Permission;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AuthService.Api.Controllers;

/// <summary>
/// Nhóm API permission dùng chung — chỉ yêu cầu access token hợp lệ, KHÔNG phân quyền theo role.
/// </summary>
/// <remarks>
/// Khác biệt với <c>AdminPermissionsController</c> (<c>/api/admin/permissions</c>):
/// <list type="bullet">
///   <item><description>Controller này chỉ gắn <c>[Authorize]</c> — mọi role đã đăng nhập (Admin/Manager/Staff/Customer) đều gọi được, miễn token hợp lệ.</description></item>
///   <item><description>Module admin yêu cầu thêm role Admin (<c>[Authorize(Roles = "Admin")]</c>) và kèm các thao tác gán role↔permission.</description></item>
/// </list>
///
/// Use case: FE/Mobile cần load danh mục permission đầy đủ (để build picker, đối chiếu <c>P.*</c> constant,
/// hiển thị mô tả) mà không cần quyền admin.
/// </remarks>
[ApiController]
[Route("api/permissions")]
[Produces("application/json")]
[Authorize]
public class PermissionsController : ControllerBase
{
    private readonly IMediator _mediator;

    public PermissionsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Danh sách tất cả permission trong hệ thống. Filter optional theo module.
    /// </summary>
    /// <remarks>
    /// Trả flat list <c>PermissionDto</c> sort theo <c>Module</c> rồi <c>Code</c>. Mỗi entry có:
    /// <list type="bullet">
    ///   <item><description><c>Id</c>: Guid identifier.</description></item>
    ///   <item><description><c>Code</c>: dot-separated key (vd <c>"ticket.view"</c>, <c>"alert.acknowledge"</c>) — match với <c>P.*</c> constant ở FE.</description></item>
    ///   <item><description><c>Module</c>: namespace logic (Ticket / Alert / Battery / Account / Admin) — filter qua <c>?module=Ticket</c>.</description></item>
    ///   <item><description><c>Description</c>: mô tả tiếng Việt cho UI.</description></item>
    ///   <item><description><c>IsSystemPermission</c>: cờ phân biệt permission hệ thống vs custom.</description></item>
    /// </list>
    ///
    /// Quyền truy cập: chỉ cần access token hợp lệ — KHÔNG yêu cầu role Admin.
    /// </remarks>
    /// <param name="module">Filter theo module name (Ticket/Alert/Battery/...). Để trống = trả tất cả.</param>
    /// <param name="cancellationToken">Token hủy request.</param>
    /// <response code="200">Lấy danh sách permission thành công.</response>
    /// <response code="401">Chưa đăng nhập / token hết hạn.</response>
    [HttpGet]
    [ProducesResponseType(typeof(PermissionListResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetAllPermissions(
        [FromQuery] string? module = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(new GetAllPermissionsQuery { Module = module }, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }
}
