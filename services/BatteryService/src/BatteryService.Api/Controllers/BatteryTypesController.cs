using BatteryService.Application.CQRS.Query.BatteryType;
using BatteryService.Application.DTOs;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SharedContracts.Common.Responses;

namespace BatteryService.Api.Controllers;

/// <summary>
/// Nhóm endpoint quản lý <b>loại pin</b> (BatteryType) - danh mục tham chiếu mô tả thông số kỹ thuật chuẩn của một dòng pin.
/// BatteryType là master data, được Admin tạo trước khi gán cho từng <see cref="BatteryAssetsController"/> hoặc <see cref="ThresholdConfigsController"/>.
/// </summary>
/// <remarks>
/// Mỗi BatteryType định nghĩa: tên model, nhà sản xuất, dung lượng danh định (Ah), điện áp danh định (V), hóa học pin
/// (LiFePO4, NMC, NCA, LCO...), số chu kỳ tối đa. Toàn bộ endpoint yêu cầu đăng nhập; phân quyền chi tiết:
/// <list type="bullet">
///   <item><description><b>GET</b>: Admin, Manager.</description></item>
///   <item><description><b>POST/PUT/DELETE/PATCH restore</b>: chỉ Admin.</description></item>
/// </list>
/// Soft delete: <c>DELETE</c> chỉ đánh dấu <c>IsDeleted = true</c>, có thể khôi phục bằng <c>PATCH /restore</c>.
/// </remarks>
[ApiController]
[Route("api/battery-types")]
[Produces("application/json")]
public class BatteryTypesController : ControllerBase
{
    private readonly IMediator _mediator;

    public BatteryTypesController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Lấy danh sách BatteryType có phân trang, hỗ trợ tìm kiếm theo từ khóa và filter trạng thái xóa.
    /// </summary>
    /// <remarks>
    /// Query parameters:
    /// - <c>PageNumber</c>: số trang, mặc định 1. Giá trị ≤ 0 sẽ tự reset về 1.
    /// - <c>PageSize</c>: kích thước trang, mặc định 10, tối đa 100.
    /// - <c>Keyword</c>: tùy chọn, tìm kiếm không phân biệt hoa thường trên <c>Name</c> hoặc <c>Manufacturer</c>.
    /// - <c>IncludeDeleted</c>: <c>true</c> để bao gồm cả record đã soft delete, mặc định <c>false</c>.
    ///
    /// Cách hoạt động:
    /// - Query DB qua repository với <c>AsNoTracking</c> để tối ưu read-only.
    /// - Filter <c>IsDeleted</c> trừ khi <c>IncludeDeleted = true</c>.
    /// - Filter keyword áp dụng đồng thời trên <c>Name</c> và <c>Manufacturer</c> bằng <c>ToLower().Contains(keyword)</c>.
    /// - Sắp xếp theo <c>CreatedAt</c> giảm dần (mới nhất lên đầu).
    /// - Trả về <see cref="PaginationResponse{T}"/> với <c>Items</c>, <c>TotalItems</c>, <c>PageNumber</c>, <c>PageSize</c>.
    ///
    /// Use case điển hình:
    /// - Admin/Manager/Staff xem danh sách BatteryType để chọn khi tạo BatteryAsset/Site/Group.
    /// - Admin filter <c>IncludeDeleted=true</c> để tìm BatteryType cũ cần khôi phục.
    /// </remarks>
    /// <param name="query">Query phân trang + filter.</param>
    /// <param name="cancellationToken">Token hủy request khi client ngắt kết nối hoặc server dừng xử lý.</param>
    /// <returns><see cref="CommonResponse{T}"/> với <c>Data</c> là <see cref="PaginationResponse{T}"/> các <see cref="BatteryTypeDto"/>.</returns>
    /// <response code="200">Trả về danh sách BatteryType (có thể rỗng).</response>
    /// <response code="401">Chưa đăng nhập hoặc access token không hợp lệ.</response>
    /// <response code="403">Đăng nhập nhưng không có role Admin, Manager hoặc Staff.</response>
    [HttpGet]
    [Authorize(Roles = "Admin,Manager,Staff")]
    [ProducesResponseType(typeof(CommonResponse<PaginationResponse<BatteryTypeDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetAll([FromQuery] GetBatteryTypesQuery query, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(query, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Lấy chi tiết một BatteryType theo Id.
    /// </summary>
    /// <remarks>
    /// Cách hoạt động:
    /// - Tìm BatteryType có <c>Id = id</c> và <c>IsDeleted = false</c>.
    /// - Nếu không tìm thấy hoặc đã bị soft delete, trả 404.
    /// - Nếu tồn tại, trả về DTO đầy đủ thông tin (Name, Manufacturer, NominalCapacityAh, NominalVoltage,
    ///   Chemistry, MaxCycleCount, Description, CreatedAt).
    ///
    /// Lưu ý:
    /// - Endpoint này KHÔNG trả về danh sách Asset/ThresholdConfig liên kết với BatteryType. Để xem các BatteryAsset
    ///   của một BatteryType, dùng <c>GET /api/battery-assets?batteryTypeId={id}</c>.
    /// </remarks>
    /// <param name="id">Id (GUID) của BatteryType cần xem chi tiết.</param>
    /// <param name="cancellationToken">Token hủy request khi client ngắt kết nối hoặc server dừng xử lý.</param>
    /// <returns><see cref="CommonResponse{T}"/> với <c>Data</c> là <see cref="BatteryTypeDto"/>.</returns>
    /// <response code="200">Trả về BatteryType.</response>
    /// <response code="401">Chưa đăng nhập.</response>
    /// <response code="403">Không có role Admin, Manager hoặc Staff.</response>
    /// <response code="404">Không tìm thấy BatteryType hoặc đã bị xóa.</response>
    [HttpGet("{id:guid}")]
    [Authorize(Roles = "Admin,Manager,Staff")]
    [ProducesResponseType(typeof(CommonResponse<BatteryTypeDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(CommonResponse<BatteryTypeDto>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetBatteryTypeByIdQuery { Id = id }, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

}
