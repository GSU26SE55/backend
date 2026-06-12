using BatteryService.Application.CQRS.Command.BatteryAsset;
using BatteryService.Application.DTOs;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SharedContracts.Common.Responses;

namespace BatteryService.Api.Controllers.Admin;

/// <summary>
/// Module admin quản lý <b>BatteryAsset</b>: tạo, cập nhật, xóa mềm, khôi phục và chuyển quyền sở hữu.
/// Toàn bộ endpoint trong controller này chỉ dành cho Admin.
/// </summary>
[ApiController]
[Route("api/admin/battery-assets")]
[Produces("application/json")]
[ApiExplorerSettings(GroupName = "admin")]
[Authorize]
public class AdminBatteryAssetsController : ControllerBase
{
    private readonly IMediator _mediator;

    public AdminBatteryAssetsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Tạo mới một BatteryAsset.
    /// </summary>
    /// <remarks>
    /// Body request:
    /// - <c>SerialNumber</c>: bắt buộc, 5-64 ký tự, chỉ chứa <c>A-Z 0-9 -</c> (regex <c>^[A-Z0-9-]+$</c>). Hệ thống tự trim + upper trước khi check trùng.
    /// - <c>BatteryTypeId</c>: bắt buộc, phải tồn tại trong BatteryType chưa xóa.
    /// - <c>CustomerId</c>: bắt buộc, phải tồn tại trong <c>CustomerAccount</c> (read-model sync từ AuthService) và <c>IsActive = true</c>.
    /// - <c>SiteId</c>: tùy chọn. Nếu có, Site phải tồn tại, chưa xóa và thuộc cùng <c>CustomerId</c>.
    /// - <c>InstallDate</c>: bắt buộc, không ở tương lai và không cũ hơn 5 năm.
    /// - <c>WarrantyEndDate</c>: tùy chọn, phải sau <c>InstallDate</c>. Nếu đã qua hiện tại thì <c>WarrantyStatus</c> tự set <c>Expired</c>, ngược lại <c>Active</c>.
    /// - <c>Location</c>: tùy chọn, ≤ 255 ký tự (mô tả vị trí lắp).
    /// - <c>Latitude</c>: tùy chọn, trong khoảng [-90, 90].
    /// - <c>Longitude</c>: tùy chọn, trong khoảng [-180, 180].
    /// - <c>Notes</c>: tùy chọn, ≤ 1000 ký tự.
    ///
    /// Cách hoạt động:
    /// - Validate đầu vào (gom toàn bộ lỗi).
    /// - Check customer active → check trùng serial → check BatteryType → check Site → validate relation.
    /// - Tạo asset với <c>Status = Active</c>.
    /// - Lưu xuống DB.
    ///
    /// Lưu ý:
    /// - SerialNumber được normalize <c>Trim().ToUpperInvariant()</c> trước khi check; "bat-001" và " BAT-001 " bị coi là cùng serial.
    /// - <c>Status</c> ban đầu luôn là <c>Active</c>; muốn đổi sang Inactive/Decommissioned phải dùng <c>PUT</c>.
    /// </remarks>
    /// <param name="command">Thông tin BatteryAsset cần tạo.</param>
    /// <param name="cancellationToken">Token hủy request.</param>
    /// <returns><see cref="CommonResponse{T}"/> chứa <see cref="BatteryAssetDto"/> vừa tạo.</returns>
    /// <response code="201">Tạo thành công.</response>
    /// <response code="400">Dữ liệu không hợp lệ (xem <c>ListErrors</c>).</response>
    /// <response code="401">Chưa đăng nhập.</response>
    /// <response code="403">Không có role Admin.</response>
    /// <response code="404">Không tìm thấy Customer / BatteryType / Site được tham chiếu.</response>
    /// <response code="409">Serial đã tồn tại, hoặc vi phạm ràng buộc Site/BatteryType (ví dụ Site khác Customer).</response>
    [HttpPost]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(CommonResponse<BatteryAssetDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(CommonResponse<BatteryAssetDto>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(CommonResponse<BatteryAssetDto>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(CommonResponse<BatteryAssetDto>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateBatteryAssetCommand command, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(command, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Cập nhật một BatteryAsset.
    /// </summary>
    /// <remarks>
    /// Body request: kế thừa toàn bộ field của <see cref="CreateBatteryAssetCommand"/>, thêm:
    /// - <c>WarrantyStatus</c>: enum <c>Active = 1</c>, <c>Expired = 2</c>, <c>Void = 3</c>. Cho phép manual override.
    /// - <c>Status</c>: enum <c>Active</c>/<c>Inactive</c>/<c>Decommissioned</c>.
    ///
    /// Cách hoạt động:
    /// - Tìm asset (include BatteryType/Site); 404 nếu không có.
    /// - Check trùng serial (loại trừ chính nó), check tham chiếu, check ràng buộc Site/Type giống Create.
    /// - Update toàn bộ field.
    ///
    /// Lưu ý:
    /// - Không cho phép đổi <c>CustomerId</c> qua endpoint này (xem <see cref="TransferOwner"/>).
    /// </remarks>
    /// <param name="id">Id BatteryAsset.</param>
    /// <param name="command">Thông tin update.</param>
    /// <param name="cancellationToken">Token hủy request.</param>
    /// <returns><see cref="CommonResponse{T}"/> chứa <see cref="BatteryAssetDto"/> sau khi update.</returns>
    /// <response code="200">Update thành công.</response>
    /// <response code="400">Dữ liệu không hợp lệ.</response>
    /// <response code="401">Chưa đăng nhập.</response>
    /// <response code="403">Không có role Admin.</response>
    /// <response code="404">Không tìm thấy asset hoặc reference (BatteryType/Site).</response>
    /// <response code="409">Serial trùng hoặc vi phạm ràng buộc Site.</response>
    [HttpPut("{id:guid}")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(CommonResponse<BatteryAssetDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(CommonResponse<BatteryAssetDto>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(CommonResponse<BatteryAssetDto>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(CommonResponse<BatteryAssetDto>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateBatteryAssetCommand command, CancellationToken cancellationToken)
    {
        command.Id = id;
        var result = await _mediator.Send(command, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Xóa mềm một BatteryAsset.
    /// </summary>
    /// <remarks>
    /// Cách hoạt động:
    /// - Tìm asset; 404 nếu không có.
    /// - Soft delete asset (interceptor set <c>IsDeleted = true</c>, <c>DeletedAt = UtcNow</c>).
    ///
    /// Lưu ý:
    /// - SensorReading lịch sử KHÔNG bị xóa cùng (TimescaleDB hypertable không có IsDeleted), có thể vẫn truy vấn được qua history endpoint.
    /// - Alert đang mở của asset không bị tự động resolve - cần xử lý riêng nếu cần.
    /// </remarks>
    /// <param name="id">Id BatteryAsset cần xóa.</param>
    /// <param name="cancellationToken">Token hủy request.</param>
    /// <returns><see cref="CommonResponse{T}"/> thông báo xóa thành công.</returns>
    /// <response code="200">Xóa thành công.</response>
    /// <response code="401">Chưa đăng nhập.</response>
    /// <response code="403">Không có role Admin.</response>
    /// <response code="404">Không tìm thấy asset.</response>
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(CommonResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(CommonResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new DeleteBatteryAssetCommand { Id = id }, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Khôi phục một BatteryAsset đã soft delete.
    /// </summary>
    /// <remarks>
    /// Cách hoạt động:
    /// - Tìm asset <c>IsDeleted = true</c>; 404 nếu không có.
    /// - Check trùng serial trong các asset active; trùng trả 409.
    /// - Nếu asset có <c>SiteId</c>, kiểm tra Site đó vẫn còn active; nếu đã xóa trả 409.
    /// - Set <c>IsDeleted = false</c>, <c>DeletedAt = null</c>.
    ///
    /// Lưu ý:
    /// - Nếu Site của asset đã bị xóa thì phải restore Site trước (hoặc cập nhật asset reference) rồi mới restore asset được.
    /// </remarks>
    /// <param name="id">Id BatteryAsset đã xóa.</param>
    /// <param name="cancellationToken">Token hủy request.</param>
    /// <returns><see cref="CommonResponse{T}"/> thông báo khôi phục thành công.</returns>
    /// <response code="200">Khôi phục thành công.</response>
    /// <response code="401">Chưa đăng nhập.</response>
    /// <response code="403">Không có role Admin.</response>
    /// <response code="404">Không tìm thấy asset đã xóa.</response>
    /// <response code="409">Serial trùng, hoặc Site của asset đã bị xóa.</response>
    [HttpPatch("{id:guid}/restore")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(CommonResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(CommonResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(CommonResponse<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Restore(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new RestoreBatteryAssetCommand { Id = id }, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Chuyển quyền sở hữu một BatteryAsset sang Customer khác.
    /// </summary>
    /// <remarks>
    /// Endpoint dành riêng cho việc đổi chủ sở hữu (transfer of ownership) - không dùng PUT vì đây là hành động nghiệp vụ
    /// đặc biệt, có hệ quả về reset Site.
    ///
    /// Body request:
    /// - <c>NewCustomerId</c>: bắt buộc, Customer mới phải tồn tại trong read-model và đang active.
    /// - <c>Reason</c>: tùy chọn, ≤ 500 ký tự, ghi nhận lý do chuyển (audit log).
    ///
    /// Cách hoạt động:
    /// - Tìm asset; 404 nếu không có.
    /// - Nếu <c>NewCustomerId</c> trùng customer hiện tại, trả 409 (no-op không được phép).
    /// - Validate Customer mới active; không có trả 404.
    /// - Reset <c>SiteId = null</c> (Site cũ thuộc Customer cũ, không hợp lệ với Customer mới).
    /// - Set <c>CustomerId = NewCustomerId</c>.
    ///
    /// Lưu ý:
    /// - After after transfer, Admin nên gán lại Site cho asset qua <c>PUT /api/admin/battery-assets/{id}</c> nếu Customer mới có Site phù hợp.
    /// - SensorReading lịch sử KHÔNG bị xóa - giữ nguyên cho audit/analytics, nhưng được tính cho owner mới từ thời điểm transfer.
    /// - Alert lịch sử của asset cũng giữ nguyên.
    /// </remarks>
    /// <param name="id">Id BatteryAsset.</param>
    /// <param name="command">Customer mới + lý do.</param>
    /// <param name="cancellationToken">Token hủy request.</param>
    /// <returns><see cref="CommonResponse{T}"/> thông báo chuyển owner thành công.</returns>
    /// <response code="200">Chuyển thành công.</response>
    /// <response code="400">Dữ liệu không hợp lệ.</response>
    /// <response code="401">Chưa đăng nhập.</response>
    /// <response code="403">Không có role Admin.</response>
    /// <response code="404">Asset không tồn tại hoặc Customer mới không tồn tại/không active.</response>
    /// <response code="409">Customer mới trùng customer hiện tại.</response>
    [HttpPut("{id:guid}/transfer-owner")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(CommonResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(CommonResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(CommonResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(CommonResponse<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> TransferOwner(Guid id, [FromBody] TransferBatteryAssetOwnerCommand command, CancellationToken cancellationToken)
    {
        command.Id = id;
        var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                          ?? User.FindFirst("UserId")?.Value;
        if (Guid.TryParse(userIdClaim, out var userId))
            command.PerformedByUserId = userId;
        var result = await _mediator.Send(command, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }
}
