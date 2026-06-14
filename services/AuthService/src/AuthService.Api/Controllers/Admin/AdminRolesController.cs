using AuthService.Application.CQRS.Command.Role;
using AuthService.Application.CQRS.Query.Role;
using AuthService.Application.DTOs.Response.Role;
using AuthService.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AuthService.Api.Controllers.Admin;

/// <summary>
/// Module admin quản lý role (vai trò) trong hệ thống.
/// Role hệ thống (`IsSystemRole = true`) như Admin, Manager, Technician, Customer
/// không thể chỉnh sửa, đổi trạng thái, hoặc xóa.
/// </summary>
[ApiController]
[Route("api/admin/roles")]
[Produces("application/json")]
[ApiExplorerSettings(GroupName = "admin")]
public class AdminRolesController : ControllerBase
{
    private readonly IMediator _mediator;

    public AdminRolesController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Liệt kê tất cả role trong hệ thống (system roles + custom) — phân trang + filter theo keyword. Mỗi role kèm permission count + assigned user count.
    /// </summary>
    /// <remarks>
    /// **Quyền truy cập:** Admin, Manager.
    ///
    /// **Tham số lọc (đều optional):**
    /// - `keyword`: tìm kiếm trong `Name` hoặc `Description` (case-insensitive).
    /// - `status`: 1 = Active, 2 = Inactive, 3 = Deprecated.
    /// - `isSystemRole`: lọc role hệ thống / role tự tạo.
    ///
    /// **Phân trang:** mặc định `pageNumber = 1`, `pageSize = 10`, max `pageSize = 100`.
    /// Sắp xếp theo `CreatedAt` giảm dần (mới nhất lên đầu).
    /// </remarks>
    /// <response code="200">Danh sách role kèm metadata phân trang (TotalItems, TotalPages, HasNextPage...).</response>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [Authorize(Roles = "Admin,Manager")]
    [ProducesResponseType(typeof(RoleListResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] string? keyword = null,
        [FromQuery] RoleStatusEnum? status = null,
        [FromQuery] bool? isSystemRole = null,
        CancellationToken cancellationToken = default)
    {
        var query = new GetRolesQuery
        {
            PageNumber = pageNumber,
            PageSize = pageSize,
            Keyword = keyword,
            Status = status,
            IsSystemRole = isSystemRole
        };
        var result = await _mediator.Send(query, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Lấy chi tiết 1 role theo Id — trả tên/mô tả + list permission đã gán + flag IsSystemRole (Admin/Manager/Staff/Customer KHÔNG cho phép edit cấu trúc).
    /// </summary>
    /// <remarks>
    /// **Quyền truy cập:** Admin, Manager.
    ///
    /// Trả về đầy đủ thông tin role: tên, mô tả, trạng thái, có phải role hệ thống không, thời điểm tạo/cập nhật.
    /// </remarks>
    /// <param name="id">Id (Guid) của role.</param>
    /// <response code="200">Trả về `RoleDto` trong `Data`.</response>
    /// <response code="404">Không tìm thấy role với Id đã cho.</response>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [Authorize(Roles = "Admin,Manager")]
    [ProducesResponseType(typeof(RoleResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RoleResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetRoleByIdQuery { Id = id }, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Tạo mới 1 custom role (ngoài system roles) — cấp role cho user qua AdminAccountsController.ChangeRole; gán permission qua AdminPermissionsController.SetRolePermissions.
    /// </summary>
    /// <remarks>
    /// **Quyền truy cập:** Admin.
    ///
    /// Role mới được tạo sẽ luôn có `IsSystemRole = false` và `Status = Active`.
    /// Tên role sẽ được normalize sang chữ HOA (ví dụ "Quản lý kho" → "QUẢN LÝ KHO") để check trùng.
    ///
    /// **Validate:**
    /// - `Name`: bắt buộc, tối đa 100 ký tự.
    /// - `Description`: optional, tối đa 500 ký tự.
    /// </remarks>
    /// <response code="201">Tạo thành công, trả về `Id` của role mới trong `Data`.</response>
    /// <response code="400">Dữ liệu không hợp lệ.</response>
    /// <response code="409">Tên role đã tồn tại (trùng `NormalizedName`).</response>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(RoleActionResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(RoleActionResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(RoleActionResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateRoleCommand command, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(command, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Cập nhật tên và mô tả của role — KHÔNG được rename system roles (Admin/Manager/Staff/Customer). Để đổi permissions, dùng PUT roles/{id}/permissions.
    /// </summary>
    /// <remarks>
    /// **Quyền truy cập:** Admin.
    ///
    /// **Lưu ý:** Không thể cập nhật role hệ thống (`IsSystemRole = true`) — sẽ trả **403**.
    /// Tên mới phải không trùng với role khác (sau khi normalize sang chữ HOA).
    /// Endpoint này KHÔNG đổi được `Status` — dùng PATCH `/status` cho việc đó.
    /// </remarks>
    /// <param name="id">Id role cần cập nhật.</param>
    /// <param name="command">Dữ liệu mới (Name, Description).</param>
    /// <response code="200">Cập nhật thành công.</response>
    /// <response code="400">Dữ liệu không hợp lệ.</response>
    /// <response code="403">Đang cố sửa role hệ thống.</response>
    /// <response code="404">Không tìm thấy role.</response>
    /// <response code="409">Tên role mới đã trùng với role khác.</response>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(RoleActionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RoleActionResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(RoleActionResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(RoleActionResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateRoleCommand command, CancellationToken cancellationToken)
    {
        command.Id = id;
        var result = await _mediator.Send(command, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Đổi trạng thái role (Active / Inactive / Deprecated).
    /// </summary>
    /// <remarks>
    /// **Quyền truy cập:** Admin.
    ///
    /// Khi role chuyển sang `Inactive` hoặc `Deprecated`, các tài khoản đang được gán role này
    /// VẪN giữ assignment trong DB — nhưng khi login, các role không Active sẽ KHÔNG được đưa
    /// vào claim của access token. Tức là user sẽ mất quyền của role đó ở lần login tiếp theo.
    ///
    /// **Không áp dụng cho role hệ thống** (sẽ trả **403**).
    /// </remarks>
    /// <param name="id">Id role.</param>
    /// <param name="command">Trạng thái mới: 1 = Active, 2 = Inactive, 3 = Deprecated.</param>
    /// <response code="200">Đổi trạng thái thành công (hoặc trạng thái không thay đổi).</response>
    /// <response code="403">Role hệ thống không cho đổi trạng thái.</response>
    /// <response code="404">Không tìm thấy role.</response>
    [HttpPatch("{id:guid}/status")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(RoleActionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RoleActionResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ChangeStatus(Guid id, [FromBody] ChangeRoleStatusCommand command, CancellationToken cancellationToken)
    {
        command.Id = id;
        var result = await _mediator.Send(command, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Xoá mềm 1 custom role — KHÔNG cho xoá system roles. User đang giữ role bị xóa sẽ được fallback về Customer (default) ở lần issue JWT tiếp theo.
    /// </summary>
    /// <remarks>
    /// **Quyền truy cập:** Admin.
    ///
    /// Đây là **soft delete** — set `IsDeleted = true`, không xóa vật lý. Bị chặn trong 2 trường hợp:
    /// - Role hệ thống (`IsSystemRole = true`) → **403**.
    /// - Role đang được gán cho ít nhất 1 tài khoản với `IsActive = true` → **409**.
    ///   Cần thu hồi role khỏi tất cả tài khoản trước (xem `DELETE /api/admin/accounts/{id}/roles/{roleId}`),
    ///   hoặc chuyển trạng thái role sang `Deprecated`.
    /// </remarks>
    /// <param name="id">Id role.</param>
    /// <response code="200">Xóa thành công.</response>
    /// <response code="403">Role hệ thống không thể xóa.</response>
    /// <response code="404">Không tìm thấy role.</response>
    /// <response code="409">Role đang được sử dụng bởi tài khoản nào đó.</response>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(RoleActionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RoleActionResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(RoleActionResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(RoleActionResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new DeleteRoleCommand { Id = id }, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }
}
