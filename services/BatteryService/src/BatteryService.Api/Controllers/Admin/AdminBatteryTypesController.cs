using BatteryService.Application.CQRS.Command.BatteryType;
using BatteryService.Application.DTOs;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SharedContracts.Common.Responses;

namespace BatteryService.Api.Controllers.Admin;

/// <summary>
/// Module admin quản lý <b>BatteryType</b>: tạo, cập nhật, xóa mềm, khôi phục danh mục loại pin.
/// Toàn bộ endpoint trong controller này chỉ dành cho Admin.
/// </summary>
[ApiController]
[Route("api/admin/battery-types")]
[Produces("application/json")]
[ApiExplorerSettings(GroupName = "admin")]
[Authorize]
public class AdminBatteryTypesController : ControllerBase
{
    private readonly IMediator _mediator;

    public AdminBatteryTypesController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Tạo mới 1 BatteryType (LiFePO4/NMC/NCA) — định nghĩa NominalCapacityAh/NominalVoltage/MaxCycleCount; thường tạo 1 lần khi seed, ít khi mới sau đó.
    /// </summary>
    /// <remarks>
    /// Body request:
    /// - <c>Name</c>: bắt buộc, tối đa 100 ký tự. Phải duy nhất (không phân biệt hoa thường) trong số các BatteryType chưa xóa.
    /// - <c>Manufacturer</c>: tùy chọn, tối đa 100 ký tự.
    /// - <c>NominalCapacityAh</c>: bắt buộc, dung lượng danh định tính bằng Ampere-hour, phải > 0.
    /// - <c>NominalVoltage</c>: bắt buộc, điện áp danh định tính bằng Volt, phải > 0.
    /// - <c>Chemistry</c>: enum hóa học pin, giá trị: <c>LiFePO4 = 1</c>, <c>Nmc = 2</c>, <c>Nca = 3</c>, <c>Lco = 4</c>, <c>Other = 99</c>. Mặc định LiFePO4.
    /// - <c>MaxCycleCount</c>: bắt buộc, số chu kỳ sạc/xả tối đa, phải > 0. Mặc định 2000.
    /// - <c>Description</c>: tùy chọn, tối đa 500 ký tự.
    ///
    /// Cách hoạt động:
    /// - Validate tất cả field qua <c>ValidateAsync</c>; mọi lỗi gom vào <c>ListErrors</c> rồi trả 400.
    /// - Kiểm tra trùng tên (case-insensitive, đã trim) trong các BatteryType chưa xóa; nếu trùng trả 409.
    /// - Sinh <c>Id = Guid.NewGuid()</c> và persist.
    /// - <c>CreatedAt</c>, <c>CreatedBy</c> được <c>AuditableEntityInterceptor</c> tự động set.
    ///
    /// Lưu ý:
    /// - Sau khi tạo BatteryType, Admin nên gọi tiếp <c>PUT /api/admin/thresholds/by-type/{id}</c> để cấu hình ngưỡng cảnh báo cho loại pin này.
    /// - Tên không phân biệt hoa thường khi kiểm tra trùng: "LiFePO4 12V" và "lifepo4 12v" bị coi là trùng.
    /// </remarks>
    /// <param name="command">Thông tin BatteryType cần tạo.</param>
    /// <param name="cancellationToken">Token hủy request khi client ngắt kết nối hoặc server dừng xử lý.</param>
    /// <returns><see cref="CommonResponse{T}"/> với <c>Data</c> là <see cref="BatteryTypeDto"/> vừa tạo.</returns>
    /// <response code="201">Tạo thành công.</response>
    /// <response code="400">Dữ liệu không hợp lệ (xem <c>ListErrors</c>).</response>
    /// <response code="401">Chưa đăng nhập.</response>
    /// <response code="403">Không có role Admin.</response>
    /// <response code="409">Tên BatteryType đã tồn tại.</response>
    [HttpPost]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(CommonResponse<BatteryTypeDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(CommonResponse<BatteryTypeDto>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(CommonResponse<BatteryTypeDto>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateBatteryTypeCommand command, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(command, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Cập nhật toàn bộ thông tin một BatteryType — thay đổi spec ảnh hưởng tới tất cả asset đang dùng type này, dùng cẩn thận (chỉ khi nhà sản xuất ra revision mới).
    /// </summary>
    /// <remarks>
    /// Body request: giống <see cref="Create"/>; <c>Id</c> trong URL được gán đè vào command body để đảm bảo nhất quán.
    ///
    /// Cách hoạt động:
    /// - Validate dữ liệu giống Create.
    /// - Tìm entity theo <c>Id</c> và <c>!IsDeleted</c>; không tìm thấy trả 404.
    /// - Kiểm tra trùng tên (loại trừ chính nó); trùng trả 409.
    /// - Update toàn bộ field rồi persist. <c>UpdatedAt</c> được interceptor tự set.
    ///
    /// Lưu ý:
    /// - Đây là full update (PUT semantics): mọi field optional không gửi sẽ bị reset về <c>null</c>.
    /// - Thay đổi <c>NominalCapacityAh</c> hoặc <c>NominalVoltage</c> KHÔNG tự động đổi ThresholdConfig đã cấu hình; Admin cần cập nhật threshold riêng nếu cần.
    /// - Endpoint này KHÔNG ảnh hưởng đến BatteryAsset đang tham chiếu đến BatteryType (cascade update không xảy ra).
    /// </remarks>
    /// <param name="id">Id BatteryType cần update.</param>
    /// <param name="command">Thông tin mới của BatteryType.</param>
    /// <param name="cancellationToken">Token hủy request khi client ngắt kết nối hoặc server dừng xử lý.</param>
    /// <returns><see cref="CommonResponse{T}"/> với <c>Data</c> là <see cref="BatteryTypeDto"/> sau khi update.</returns>
    /// <response code="200">Update thành công.</response>
    /// <response code="400">Dữ liệu không hợp lệ.</response>
    /// <response code="401">Chưa đăng nhập.</response>
    /// <response code="403">Không có role Admin.</response>
    /// <response code="404">Không tìm thấy BatteryType.</response>
    /// <response code="409">Tên đã tồn tại ở BatteryType khác.</response>
    [HttpPut("{id:guid}")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(CommonResponse<BatteryTypeDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(CommonResponse<BatteryTypeDto>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(CommonResponse<BatteryTypeDto>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(CommonResponse<BatteryTypeDto>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateBatteryTypeCommand command, CancellationToken cancellationToken)
    {
        command.Id = id;
        var result = await _mediator.Send(command, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Xoá mềm 1 BatteryType — chặn nếu còn BatteryAsset đang reference; phải migrate asset sang type khác trước hoặc soft-delete asset trước.
    /// </summary>
    /// <remarks>
    /// Cách hoạt động:
    /// - Tìm BatteryType có <c>Id</c> và <c>!IsDeleted</c>; không có trả 404.
    /// - Kiểm tra ràng buộc: nếu còn BatteryAsset nào tham chiếu đến BatteryType này và chưa bị xóa, trả 409.
    /// - Gọi <c>DeleteAsync</c>; <c>AuditableEntityInterceptor</c> sẽ tự convert thành <c>IsDeleted = true</c> + <c>DeletedAt = UtcNow</c>.
    /// - Bản ghi vẫn tồn tại trong DB, có thể restore qua <c>PATCH /{id}/restore</c>.
    ///
    /// Lưu ý:
    /// - Soft delete giữ lại audit trail và tránh phá vỡ FK của các BatteryAsset cũ.
    /// - Để hard delete (vĩnh viễn) cần thao tác DB trực tiếp - không được expose qua API.
    /// </remarks>
    /// <param name="id">Id BatteryType cần xóa.</param>
    /// <param name="cancellationToken">Token hủy request khi client ngắt kết nối hoặc server dừng xử lý.</param>
    /// <returns><see cref="CommonResponse{T}"/> với thông báo thành công.</returns>
    /// <response code="200">Xóa thành công.</response>
    /// <response code="401">Chưa đăng nhập.</response>
    /// <response code="403">Không có role Admin.</response>
    /// <response code="404">Không tìm thấy BatteryType.</response>
    /// <response code="409">Vẫn còn BatteryAsset đang gán BatteryType này.</response>
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(CommonResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(CommonResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(CommonResponse<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new DeleteBatteryTypeCommand { Id = id }, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Khôi phục 1 BatteryType đã soft delete — set IsDeleted=false. Asset cũ vẫn reference được nếu chưa bị migrate.
    /// </summary>
    /// <remarks>
    /// Cách hoạt động:
    /// - Tìm BatteryType có <c>Id</c> và <c>IsDeleted = true</c>; không có trả 404.
    /// - Kiểm tra trùng tên: nếu hiện tại đã có BatteryType khác cùng tên đang active, trả 409.
    /// - Set lại <c>IsDeleted = false</c>, <c>DeletedAt = null</c>, gọi <c>UpdateAsync</c>.
    ///
    /// Lưu ý:
    /// - Tình huống 409 thường xảy ra khi: BatteryType "X" bị xóa, Admin tạo BatteryType mới cùng tên "X" - giờ muốn restore cái cũ sẽ conflict.
    ///   Phải đổi tên cái active trước hoặc xóa nó.
    /// </remarks>
    /// <param name="id">Id BatteryType đã xóa cần khôi phục.</param>
    /// <param name="cancellationToken">Token hủy request khi client ngắt kết nối hoặc server dừng xử lý.</param>
    /// <returns><see cref="CommonResponse{T}"/> thông báo khôi phục thành công.</returns>
    /// <response code="200">Khôi phục thành công.</response>
    /// <response code="401">Chưa đăng nhập.</response>
    /// <response code="403">Không có role Admin.</response>
    /// <response code="404">Không tìm thấy BatteryType đã xóa với Id này.</response>
    /// <response code="409">Tên BatteryType đã được dùng bởi một bản ghi active khác.</response>
    [HttpPatch("{id:guid}/restore")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(CommonResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(CommonResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(CommonResponse<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Restore(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new RestoreBatteryTypeCommand { Id = id }, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }
}
