using BatteryService.Application.CQRS.Command.BatteryGroup;
using BatteryService.Application.CQRS.Query.BatteryGroup;
using BatteryService.Application.DTOs;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SharedContracts.Common.Responses;

namespace BatteryService.Api.Controllers;

/// <summary>
/// Nhóm endpoint quản lý <b>BatteryGroup</b> - cụm/dãy pin trong một Site, gom các BatteryAsset cùng BatteryType
/// để quản lý và monitoring chung.
/// </summary>
/// <remarks>
/// Ví dụ một Site có 2 string pin: Group "Block A" và Group "Block B", mỗi group chứa 10 pin LiFePO4.
///
/// Ràng buộc nghiệp vụ:
/// <list type="number">
///   <item><description>Group thuộc đúng 1 Site (qua <c>SiteId</c>).</description></item>
///   <item><description>Group có 1 BatteryType cố định - mọi asset trong group phải cùng type này.</description></item>
///   <item><description>Tên Group unique (case-insensitive) trong phạm vi 1 Site.</description></item>
///   <item><description><c>BatteryCount</c> được hệ thống auto-tăng/giảm khi asset được tạo/xóa/transfer/move group.</description></item>
///   <item><description>Không cho đổi <c>SiteId</c> hoặc <c>BatteryTypeId</c> nếu group đang chứa asset.</description></item>
///   <item><description>Không xóa được Group nếu còn asset chưa xóa.</description></item>
/// </list>
///
/// Phân quyền:
/// <list type="bullet">
///   <item><description><b>Admin</b>: full CRUD + restore.</description></item>
///   <item><description><b>Manager/Staff</b>: list/detail.</description></item>
///   <item><description><b>Customer</b>: chỉ detail (để xem group của asset họ sở hữu).</description></item>
/// </list>
/// </remarks>
[ApiController]
[Route("api/battery-groups")]
[Produces("application/json")]
public class BatteryGroupsController : ControllerBase
{
    private readonly IMediator _mediator;

    public BatteryGroupsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Lấy danh sách BatteryGroup có phân trang + filter.
    /// </summary>
    /// <remarks>
    /// Query parameters:
    /// - <c>PageNumber</c>, <c>PageSize</c>: phân trang.
    /// - <c>Keyword</c>: tìm trên <c>Name</c> của group, <c>Name</c> của Site cha hoặc <c>Name</c> của BatteryType.
    /// - <c>SiteId</c>: lọc theo site.
    /// - <c>BatteryTypeId</c>: lọc theo loại pin.
    /// - <c>IncludeDeleted</c>: <c>true</c> để bao gồm group đã xóa.
    ///
    /// Include Site + BatteryType để DTO có tên hiển thị.
    /// </remarks>
    /// <param name="query">Filter + phân trang.</param>
    /// <param name="cancellationToken">Token hủy request.</param>
    /// <returns><see cref="CommonResponse{T}"/> chứa <see cref="PaginationResponse{T}"/> các <see cref="BatteryGroupDto"/>.</returns>
    /// <response code="200">Trả danh sách.</response>
    /// <response code="401">Chưa đăng nhập.</response>
    /// <response code="403">Không có role Admin/Manager/Staff.</response>
    [HttpGet]
    [Authorize(Roles = "Admin,Manager,Staff")]
    [ProducesResponseType(typeof(CommonResponse<PaginationResponse<BatteryGroupDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetAll([FromQuery] GetBatteryGroupsQuery query, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(query, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Lấy chi tiết một BatteryGroup theo Id.
    /// </summary>
    /// <remarks>
    /// Trả DTO với tên Site, tên BatteryType và <c>BatteryCount</c> hiện tại.
    /// 404 nếu group không tồn tại hoặc đã xóa.
    /// </remarks>
    /// <param name="id">Id BatteryGroup.</param>
    /// <param name="cancellationToken">Token hủy request.</param>
    /// <returns><see cref="CommonResponse{T}"/> chứa <see cref="BatteryGroupDto"/>.</returns>
    /// <response code="200">Trả group.</response>
    /// <response code="401">Chưa đăng nhập.</response>
    /// <response code="403">Không có role phù hợp.</response>
    /// <response code="404">Group không tồn tại.</response>
    [HttpGet("{id:guid}")]
    [Authorize(Roles = "Admin,Manager,Staff,Customer")]
    [ProducesResponseType(typeof(CommonResponse<BatteryGroupDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(CommonResponse<BatteryGroupDto>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetBatteryGroupByIdQuery { Id = id }, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Tạo mới một BatteryGroup.
    /// </summary>
    /// <remarks>
    /// Body request:
    /// - <c>SiteId</c>: bắt buộc, Site phải tồn tại và chưa xóa.
    /// - <c>Name</c>: bắt buộc, ≤ 100 ký tự, unique trong phạm vi Site.
    /// - <c>BatteryTypeId</c>: bắt buộc, BatteryType phải tồn tại và chưa xóa.
    ///
    /// Cách hoạt động:
    /// - Validate input.
    /// - Check Site (404), check BatteryType (404), check trùng tên trong site (409).
    /// - Tạo group với <c>BatteryCount = 0</c>.
    ///
    /// Lưu ý:
    /// - Sau khi tạo group, gán asset vào group qua <c>POST/PUT /api/battery-assets</c> với <c>BatteryGroupId</c>;
    ///   hệ thống sẽ auto tăng <c>BatteryCount</c>.
    /// </remarks>
    /// <param name="command">Thông tin group.</param>
    /// <param name="cancellationToken">Token hủy request.</param>
    /// <returns><see cref="CommonResponse{T}"/> chứa <see cref="BatteryGroupDto"/> vừa tạo.</returns>
    /// <response code="201">Tạo thành công.</response>
    /// <response code="400">Dữ liệu không hợp lệ.</response>
    /// <response code="401">Chưa đăng nhập.</response>
    /// <response code="403">Không có role Admin.</response>
    /// <response code="404">Site hoặc BatteryType không tồn tại.</response>
    /// <response code="409">Tên group đã tồn tại trong site.</response>
    [HttpPost]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(CommonResponse<BatteryGroupDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(CommonResponse<BatteryGroupDto>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(CommonResponse<BatteryGroupDto>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(CommonResponse<BatteryGroupDto>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateBatteryGroupCommand command, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(command, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Cập nhật một BatteryGroup.
    /// </summary>
    /// <remarks>
    /// Body request: <c>SiteId</c>, <c>Name</c>, <c>BatteryTypeId</c> (giống Create).
    ///
    /// Cách hoạt động:
    /// - Tìm group (include Site, BatteryType); 404 nếu không có.
    /// - Đếm asset đang thuộc group; nếu &gt; 0 và body cố ý đổi <c>SiteId</c> hoặc <c>BatteryTypeId</c>, trả 409.
    /// - Check Site mới (404), BatteryType mới (404), trùng tên trong site mới (409).
    /// - Update.
    /// </remarks>
    /// <param name="id">Id Group.</param>
    /// <param name="command">Thông tin update.</param>
    /// <param name="cancellationToken">Token hủy request.</param>
    /// <returns><see cref="CommonResponse{T}"/> chứa <see cref="BatteryGroupDto"/> sau update.</returns>
    /// <response code="200">Update thành công.</response>
    /// <response code="400">Dữ liệu không hợp lệ.</response>
    /// <response code="401">Chưa đăng nhập.</response>
    /// <response code="403">Không có role Admin.</response>
    /// <response code="404">Group / Site / BatteryType không tồn tại.</response>
    /// <response code="409">Đổi Site/BatteryType khi group còn asset, hoặc trùng tên.</response>
    [HttpPut("{id:guid}")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(CommonResponse<BatteryGroupDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(CommonResponse<BatteryGroupDto>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(CommonResponse<BatteryGroupDto>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(CommonResponse<BatteryGroupDto>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateBatteryGroupCommand command, CancellationToken cancellationToken)
    {
        command.Id = id;
        var result = await _mediator.Send(command, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Xóa mềm một BatteryGroup.
    /// </summary>
    /// <remarks>
    /// Cách hoạt động:
    /// - 404 nếu group không tồn tại / đã xóa.
    /// - 409 nếu còn asset chưa xóa thuộc group (phải clear/move asset trước).
    /// - Soft delete.
    /// </remarks>
    /// <param name="id">Id Group.</param>
    /// <param name="cancellationToken">Token hủy request.</param>
    /// <returns><see cref="CommonResponse{T}"/> thông báo xóa.</returns>
    /// <response code="200">Xóa thành công.</response>
    /// <response code="401">Chưa đăng nhập.</response>
    /// <response code="403">Không có role Admin.</response>
    /// <response code="404">Group không tồn tại.</response>
    /// <response code="409">Group còn asset.</response>
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(CommonResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(CommonResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(CommonResponse<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new DeleteBatteryGroupCommand { Id = id }, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Khôi phục một BatteryGroup đã soft delete.
    /// </summary>
    /// <remarks>
    /// Cách hoạt động:
    /// - Tìm group <c>IsDeleted = true</c>; 404 nếu không có.
    /// - Check Site cha vẫn active; nếu Site đã xóa, trả 409 (restore Site trước).
    /// - Check trùng tên trong các group active của cùng Site; trùng trả 409.
    /// - Set <c>IsDeleted = false</c>, <c>DeletedAt = null</c>.
    ///
    /// Lưu ý:
    /// - <c>BatteryCount</c> KHÔNG được tự reset khi restore - giữ nguyên giá trị lúc soft delete.
    ///   Nếu cần đồng bộ lại với các asset đã/đang gán, cần thao tác thủ công hoặc cron sync.
    /// </remarks>
    /// <param name="id">Id Group đã xóa.</param>
    /// <param name="cancellationToken">Token hủy request.</param>
    /// <returns><see cref="CommonResponse{T}"/> thông báo khôi phục.</returns>
    /// <response code="200">Khôi phục thành công.</response>
    /// <response code="401">Chưa đăng nhập.</response>
    /// <response code="403">Không có role Admin.</response>
    /// <response code="404">Group không tồn tại.</response>
    /// <response code="409">Site cha đã xóa, hoặc tên group trùng với group active trong site.</response>
    [HttpPatch("{id:guid}/restore")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(CommonResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(CommonResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(CommonResponse<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Restore(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new RestoreBatteryGroupCommand { Id = id }, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }
}
