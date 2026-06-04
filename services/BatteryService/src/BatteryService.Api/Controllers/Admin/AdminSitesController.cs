using BatteryService.Application.CQRS.Command.Site;
using BatteryService.Application.DTOs;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SharedContracts.Common.Responses;

namespace BatteryService.Api.Controllers.Admin;

/// <summary>
/// Module admin quản lý <b>Site</b>: tạo, cập nhật, xóa mềm, khôi phục.
/// Toàn bộ endpoint trong controller này chỉ dành cho Admin.
/// </summary>
[ApiController]
[Route("api/admin/sites")]
[Produces("application/json")]
[ApiExplorerSettings(GroupName = "admin")]
[Authorize]
public class AdminSitesController : ControllerBase
{
    private readonly IMediator _mediator;

    public AdminSitesController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Tạo mới một Site.
    /// </summary>
    /// <remarks>
    /// Body request:
    /// - <c>Name</c>: bắt buộc, ≤ 200 ký tự. Unique trong phạm vi 1 Customer (case-insensitive).
    /// - <c>CustomerId</c>: bắt buộc, Customer phải tồn tại và active trong read-model.
    /// - <c>Address</c>: tùy chọn, ≤ 500 ký tự.
    /// - <c>Latitude</c>: tùy chọn, [-90, 90].
    /// - <c>Longitude</c>: tùy chọn, [-180, 180].
    /// - <c>CapacityKw</c>: tùy chọn, ≥ 0 (kW).
    /// - <c>InstallDate</c>: bắt buộc, không ở tương lai.
    /// - <c>Status</c>: enum <c>SiteStatusEnum</c>, mặc định <c>Active</c>.
    /// - <c>ContactPersonName</c>: tùy chọn, ≤ 150 ký tự.
    /// - <c>ContactPersonPhone</c>: tùy chọn, ≤ 30 ký tự.
    ///
    /// Cách hoạt động:
    /// - Validate đầy đủ.
    /// - Check Customer tồn tại + active (404 nếu không).
    /// - Check trùng tên trong phạm vi customer (409 nếu trùng).
    /// - Lưu site với <c>Id = Guid.NewGuid()</c>.
    /// </remarks>
    /// <param name="command">Thông tin Site.</param>
    /// <param name="cancellationToken">Token hủy request.</param>
    /// <returns><see cref="CommonResponse{T}"/> chứa <see cref="SiteDto"/> vừa tạo.</returns>
    /// <response code="201">Tạo thành công.</response>
    /// <response code="400">Dữ liệu không hợp lệ.</response>
    /// <response code="401">Chưa đăng nhập.</response>
    /// <response code="403">Không có role Admin.</response>
    /// <response code="404">Customer không tồn tại.</response>
    /// <response code="409">Tên Site đã tồn tại trong Customer.</response>
    [HttpPost]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(CommonResponse<SiteDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(CommonResponse<SiteDto>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(CommonResponse<SiteDto>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(CommonResponse<SiteDto>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateSiteCommand command, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(command, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Cập nhật một Site.
    /// </summary>
    /// <remarks>
    /// Body request: giống Create, thêm các field trong <see cref="UpdateSiteCommand"/>.
    ///
    /// Cách hoạt động:
    /// - Tìm site (include Assets); 404 nếu không có.
    /// - Nếu <c>CustomerId</c> trong body khác customer hiện tại (và khác <c>Guid.Empty</c>), trả 409 - không cho phép đổi chủ sở hữu qua Update.
    /// - Check trùng tên (loại trừ chính nó); trùng trả 409.
    /// - Update toàn bộ field còn lại; KHÔNG đụng đến <c>CustomerId</c>.
    /// </remarks>
    /// <param name="id">Id Site.</param>
    /// <param name="command">Thông tin cập nhật.</param>
    /// <param name="cancellationToken">Token hủy request.</param>
    /// <returns><see cref="CommonResponse{T}"/> chứa <see cref="SiteDto"/> sau update.</returns>
    /// <response code="200">Update thành công.</response>
    /// <response code="400">Dữ liệu không hợp lệ.</response>
    /// <response code="401">Chưa đăng nhập.</response>
    /// <response code="403">Không có role Admin.</response>
    /// <response code="404">Site không tồn tại.</response>
    /// <response code="409">Tên trùng hoặc cố ý đổi CustomerId.</response>
    [HttpPut("{id:guid}")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(CommonResponse<SiteDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(CommonResponse<SiteDto>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(CommonResponse<SiteDto>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(CommonResponse<SiteDto>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateSiteCommand command, CancellationToken cancellationToken)
    {
        command.Id = id;
        var result = await _mediator.Send(command, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Xóa mềm một Site.
    /// </summary>
    /// <remarks>
    /// Cách hoạt động:
    /// - 404 nếu site không tồn tại / đã xóa.
    /// - 409 nếu còn BatteryAsset chưa xóa thuộc site.
    /// - Nếu pass check, soft delete site.
    ///
    /// Lưu ý:
    /// - Để xóa site có asset, cần xóa hoặc transfer các thực thể con trước.
    /// </remarks>
    /// <param name="id">Id Site.</param>
    /// <param name="cancellationToken">Token hủy request.</param>
    /// <returns><see cref="CommonResponse{T}"/> thông báo xóa.</returns>
    /// <response code="200">Xóa thành công.</response>
    /// <response code="401">Chưa đăng nhập.</response>
    /// <response code="403">Không có role Admin.</response>
    /// <response code="404">Site không tồn tại.</response>
    /// <response code="409">Còn asset thuộc site.</response>
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(CommonResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(CommonResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(CommonResponse<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new DeleteSiteCommand { Id = id }, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Khôi phục một Site đã soft delete.
    /// </summary>
    /// <remarks>
    /// Cách hoạt động:
    /// - Tìm site <c>IsDeleted = true</c>; 404 nếu không có.
    /// - Check trùng tên trong các site active của cùng customer; trùng trả 409.
    /// - Set <c>IsDeleted = false</c>, <c>DeletedAt = null</c>.
    /// </remarks>
    /// <param name="id">Id Site đã xóa.</param>
    /// <param name="cancellationToken">Token hủy request.</param>
    /// <returns><see cref="CommonResponse{T}"/> thông báo khôi phục.</returns>
    /// <response code="200">Khôi phục thành công.</response>
    /// <response code="401">Chưa đăng nhập.</response>
    /// <response code="403">Không có role Admin.</response>
    /// <response code="404">Không tìm thấy site đã xóa.</response>
    /// <response code="409">Tên trùng với site active khác.</response>
    [HttpPatch("{id:guid}/restore")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(CommonResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(CommonResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(CommonResponse<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Restore(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new RestoreSiteCommand { Id = id }, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }
}
