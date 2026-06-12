using BatteryService.Application.CQRS.Query.Site;
using BatteryService.Application.DTOs;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SharedContracts.Common.Responses;

namespace BatteryService.Api.Controllers;

/// <summary>
/// Nhóm endpoint quản lý <b>Site</b> - địa điểm vật lý (nhà máy, trang trại điện mặt trời, tòa nhà...) thuộc một Customer,
/// nơi lắp đặt các BatteryAsset.
/// </summary>
/// <remarks>
/// Mô hình dữ liệu:
/// <code>
/// Customer (1) ──── (*) Site ──── (*) BatteryAsset
/// </code>
///
/// Phân quyền:
/// <list type="bullet">
///   <item><description><b>Admin</b>: full CRUD + restore.</description></item>
///   <item><description><b>Manager</b>: list/detail/assets/dashboard.</description></item>
///   <item><description><b>Staff</b>: chỉ xem detail/assets/dashboard khi cần xử lý ticket trên site đó.</description></item>
///   <item><description><b>Customer</b>: xem site của chính mình qua <c>GET /me</c>, chỉ xem được detail/assets/dashboard của site thuộc về mình.</description></item>
/// </list>
///
/// Ràng buộc nghiệp vụ:
/// <list type="number">
///   <item><description>Tên Site unique (case-insensitive) trong phạm vi một Customer.</description></item>
///   <item><description>Không xóa được Site nếu còn BatteryAsset chưa xóa thuộc nó.</description></item>
///   <item><description>Không đổi <c>CustomerId</c> qua endpoint Update; muốn đổi chủ phải tạo Site mới + transfer asset.</description></item>
/// </list>
/// </remarks>
[ApiController]
[Route("api/sites")]
[Produces("application/json")]
public class SitesController : ControllerBase
{
    private readonly IMediator _mediator;

    public SitesController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Lấy danh sách Site có phân trang + filter.
    /// </summary>
    /// <remarks>
    /// Query parameters:
    /// - <c>PageNumber</c>, <c>PageSize</c>: phân trang (max 100).
    /// - <c>Keyword</c>: tìm trên <c>Name</c> hoặc <c>Address</c> (case-insensitive).
    /// - <c>CustomerId</c>: lọc theo chủ sở hữu.
    /// - <c>Status</c>: enum <c>SiteStatusEnum</c> (<c>Active</c>, <c>Inactive</c>, <c>Maintenance</c>).
    /// - <c>IncludeDeleted</c>: <c>true</c> để bao gồm site đã xóa.
    ///
    /// Cách hoạt động:
    /// - Include <c>BatteryAssets</c> để DTO có count thống kê.
    /// - Filter theo IsDeleted (trừ khi IncludeDeleted), keyword, customer, status.
    /// - Sort theo <c>CreatedAt</c> giảm dần.
    /// - DTO trả thêm <c>BatteryAssetCount</c>, <c>ActiveBatteryAssetCount</c> (loại trừ deleted).
    /// </remarks>
    /// <param name="query">Filter + phân trang.</param>
    /// <param name="cancellationToken">Token hủy request.</param>
    /// <returns><see cref="CommonResponse{T}"/> chứa <see cref="PaginationResponse{T}"/> các <see cref="SiteDto"/>.</returns>
    /// <response code="200">Trả danh sách.</response>
    /// <response code="401">Chưa đăng nhập.</response>
    /// <response code="403">Không có role Admin/Manager.</response>
    [HttpGet]
    [Authorize(Roles = "Admin,Manager")]
    [ProducesResponseType(typeof(CommonResponse<PaginationResponse<SiteDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetAll([FromQuery] GetSitesQuery query, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(query, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Customer xem danh sách Site của chính mình.
    /// </summary>
    /// <remarks>
    /// Cách hoạt động:
    /// - Parse <c>UserId</c> từ token; nếu không hợp lệ trả 401.
    /// - Filter <c>CustomerId == currentUserId &amp;&amp; !IsDeleted</c>.
    /// - Tương tự GetAll, trả các count thống kê asset trong DTO.
    /// </remarks>
    /// <param name="query">Phân trang.</param>
    /// <param name="cancellationToken">Token hủy request.</param>
    /// <returns><see cref="CommonResponse{T}"/> chứa <see cref="PaginationResponse{T}"/> các <see cref="SiteDto"/>.</returns>
    /// <response code="200">Trả danh sách (có thể rỗng).</response>
    /// <response code="401">Token không hợp lệ hoặc không có claim UserId.</response>
    /// <response code="403">Không có role Customer.</response>
    [HttpGet("me")]
    [Authorize(Roles = "Customer")]
    [ProducesResponseType(typeof(CommonResponse<PaginationResponse<SiteDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetMine([FromQuery] GetMySitesQuery query, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(query, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Lấy chi tiết một Site theo Id.
    /// </summary>
    /// <remarks>
    /// Trả về <see cref="SiteDto"/> với thông tin Site + count BatteryAsset, ActiveBatteryAsset.
    ///
    /// Projection trực tiếp trong SQL (LINQ <c>.Select(...)</c>) để tránh load full graph navigation.
    /// </remarks>
    /// <param name="id">Id Site.</param>
    /// <param name="cancellationToken">Token hủy request.</param>
    /// <returns><see cref="CommonResponse{T}"/> chứa <see cref="SiteDto"/>.</returns>
    /// <response code="200">Trả về Site.</response>
    /// <response code="401">Chưa đăng nhập.</response>
    /// <response code="403">Không có role phù hợp.</response>
    /// <response code="404">Không tìm thấy Site.</response>
    [HttpGet("{id:guid}")]
    [Authorize(Roles = "Admin,Manager,Staff,Customer")]
    [ProducesResponseType(typeof(CommonResponse<SiteDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(CommonResponse<SiteDto>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetSiteByIdQuery { Id = id }, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Lấy danh sách BatteryAsset thuộc một Site.
    /// </summary>
    /// <remarks>
    /// Query parameters (cùng với <c>{id}</c> trong URL):
    /// - <c>PageNumber</c>, <c>PageSize</c>: phân trang.
    /// - <c>Status</c>: tùy chọn, enum <c>BatteryStatusEnum</c>.
    ///
    /// Cách hoạt động:
    /// - Kiểm tra Site tồn tại + chưa xóa; nếu không trả 404.
    /// - Filter asset <c>SiteId == id &amp;&amp; !IsDeleted</c>.
    /// - Apply filter status nếu có.
    /// - Trả full <see cref="BatteryAssetDto"/> với BatteryType/Site name.
    /// </remarks>
    /// <param name="id">Id Site.</param>
    /// <param name="query">Filter bổ sung + phân trang.</param>
    /// <param name="cancellationToken">Token hủy request.</param>
    /// <returns><see cref="CommonResponse{T}"/> chứa <see cref="PaginationResponse{T}"/> các <see cref="BatteryAssetDto"/>.</returns>
    /// <response code="200">Trả danh sách asset.</response>
    /// <response code="401">Chưa đăng nhập.</response>
    /// <response code="403">Không có role phù hợp.</response>
    /// <response code="404">Site không tồn tại.</response>
    [HttpGet("{id:guid}/assets")]
    [Authorize(Roles = "Admin,Manager,Staff,Customer")]
    [ProducesResponseType(typeof(CommonResponse<PaginationResponse<BatteryAssetDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(CommonResponse<PaginationResponse<BatteryAssetDto>>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAssets(Guid id, [FromQuery] GetSiteAssetsQuery query, CancellationToken cancellationToken)
    {
        query.SiteId = id;
        var result = await _mediator.Send(query, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Lấy dashboard tóm tắt sức khỏe của một Site.
    /// </summary>
    /// <remarks>
    /// Trả <see cref="SiteDashboardDto"/> gồm:
    /// - <c>TotalAssets</c>: tổng asset chưa xóa thuộc site.
    /// - <c>ActiveAssets</c>: số asset có <c>Status = Active</c>.
    /// - <c>AssetsWithActiveAlerts</c>: số asset đang có ít nhất 1 alert ở trạng thái Open/Acknowledged.
    /// - <c>LastAlertAt</c>: thời điểm alert gần nhất trên các asset của site (nullable).
    /// - <c>HealthScore</c>: số nguyên 0-100, tính theo công thức:
    ///   <c>100 - (assets_inactive × 5) - (assets_with_active_alerts × 10)</c>, clamp về [0, 100].
    ///
    /// Use case:
    /// - Manager dashboard cấp Site: thấy nhanh site nào "khỏe", site nào nhiều alert.
    /// - Hỗ trợ ra quyết định phân bổ Staff đi xử lý.
    ///
    /// Lưu ý:
    /// - HealthScore chỉ là heuristic đơn giản, không phải metric SLA chính thức.
    /// - Nếu site không có asset nào, HealthScore = 100 (không có gì để mất điểm).
    /// </remarks>
    /// <param name="id">Id Site.</param>
    /// <param name="cancellationToken">Token hủy request.</param>
    /// <returns><see cref="CommonResponse{T}"/> chứa <see cref="SiteDashboardDto"/>.</returns>
    /// <response code="200">Trả dashboard.</response>
    /// <response code="401">Chưa đăng nhập.</response>
    /// <response code="403">Không có role phù hợp.</response>
    /// <response code="404">Site không tồn tại.</response>
    [HttpGet("{id:guid}/dashboard")]
    [Authorize(Roles = "Admin,Manager,Staff,Customer")]
    [ProducesResponseType(typeof(CommonResponse<SiteDashboardDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(CommonResponse<SiteDashboardDto>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetDashboard(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetSiteDashboardQuery { Id = id }, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

}
