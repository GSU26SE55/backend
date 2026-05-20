using BatteryService.Application.CQRS.Command.BatteryAsset;
using BatteryService.Application.CQRS.Query.BatteryAsset;
using BatteryService.Application.DTOs;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SharedContracts.Common.Responses;

namespace BatteryService.Api.Controllers;

/// <summary>
/// Nhóm endpoint quản lý <b>BatteryAsset</b> - một viên pin vật lý đang vận hành tại hệ thống của khách hàng.
/// BatteryAsset là entity trung tâm: gắn với 1 Customer, có thể nằm trong 1 Site, tham chiếu 1 BatteryType,
/// và là chủ thể ghi nhận <see cref="SensorReadingsController">sensor readings</see> + <see cref="AlertsController">alert</see>.
/// </summary>
/// <remarks>
/// Phân quyền:
/// <list type="bullet">
///   <item><description><b>Admin</b>: full CRUD + transfer owner.</description></item>
///   <item><description><b>Manager</b>: chỉ đọc (list, detail, realtime).</description></item>
///   <item><description><b>Staff</b>: chỉ đọc 1 asset cụ thể (detail, realtime) khi cần xử lý ticket.</description></item>
///   <item><description><b>Customer</b>: chỉ thấy asset của chính mình qua <c>GET /me</c>, có thể xem detail/realtime nếu đúng chủ sở hữu.</description></item>
/// </list>
/// Mọi ràng buộc nghiệp vụ:
/// <list type="number">
///   <item><description>Serial number unique (case-insensitive, upper) trong các asset chưa xóa.</description></item>
///   <item><description>CustomerId phải tồn tại trong read-model <c>CustomerAccount</c> (sync từ AuthService) và đang active.</description></item>
///   <item><description>Nếu chọn Site thì Site phải thuộc cùng Customer.</description></item>
/// </list>
/// </remarks>
[ApiController]
[Route("api/battery-assets")]
[Produces("application/json")]
public class BatteryAssetsController : ControllerBase
{
    private readonly IMediator _mediator;

    public BatteryAssetsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Lấy danh sách BatteryAsset có phân trang với nhiều filter chéo.
    /// </summary>
    /// <remarks>
    /// Query parameters:
    /// - <c>PageNumber</c>, <c>PageSize</c>: phân trang chuẩn (mặc định 1/10, max page size 100).
    /// - <c>Keyword</c>: tìm trên <c>SerialNumber</c> hoặc <c>Location</c> (case-insensitive).
    /// - <c>CustomerId</c>: lọc theo chủ sở hữu.
    /// - <c>BatteryTypeId</c>: lọc theo loại pin.
    /// - <c>SiteId</c>: lọc theo site lắp đặt.
    /// - <c>Status</c>: enum trạng thái asset (<c>Active = 1</c>, <c>Inactive = 2</c>, <c>Decommissioned = 3</c>).
    /// - <c>IncludeDeleted</c>: <c>true</c> để xem cả asset đã xóa.
    ///
    /// Cách hoạt động:
    /// - Include BatteryType, Site để DTO có tên hiển thị thay vì chỉ Id.
    /// - Filter điều kiện theo từng query param có giá trị.
    /// - Sort theo <c>CreatedAt</c> giảm dần.
    /// - Projection sang <see cref="BatteryAssetDto"/> ở tầng query để giảm dữ liệu trả về.
    ///
    /// Use case:
    /// - Admin xem toàn bộ asset trong hệ thống.
    /// - Manager lọc theo Customer / Site để theo dõi fleet.
    /// </remarks>
    /// <param name="query">Bộ filter + phân trang.</param>
    /// <param name="cancellationToken">Token hủy request.</param>
    /// <returns><see cref="CommonResponse{T}"/> chứa <see cref="PaginationResponse{T}"/> các <see cref="BatteryAssetDto"/>.</returns>
    /// <response code="200">Danh sách asset.</response>
    /// <response code="401">Chưa đăng nhập.</response>
    /// <response code="403">Không có role Admin hoặc Manager.</response>
    [HttpGet]
    [Authorize(Roles = "Admin,Manager")]
    [ProducesResponseType(typeof(CommonResponse<PaginationResponse<BatteryAssetDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetAll([FromQuery] GetBatteryAssetsQuery query, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(query, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Customer lấy danh sách BatteryAsset của chính mình.
    /// </summary>
    /// <remarks>
    /// Cách hoạt động:
    /// - Đọc <c>UserId</c> từ access token (claim NameIdentifier) và parse thành Guid.
    /// - Nếu không parse được, trả 401.
    /// - Filter <c>CustomerId == currentUserId &amp;&amp; !IsDeleted</c>.
    /// - Include BatteryType/Site để hiển thị tên.
    ///
    /// Lưu ý:
    /// - Endpoint này chỉ dành cho role <b>Customer</b>. Admin/Manager dùng <c>GET /api/battery-assets?customerId=...</c>.
    /// - Customer KHÔNG cần truyền customerId; được suy từ token.
    /// - PageSize tối đa 100; nếu Customer có ít asset thì kết quả nhỏ là bình thường.
    /// </remarks>
    /// <param name="query">Tham số phân trang.</param>
    /// <param name="cancellationToken">Token hủy request.</param>
    /// <returns><see cref="CommonResponse{T}"/> chứa <see cref="PaginationResponse{T}"/> các <see cref="BatteryAssetDto"/> thuộc Customer hiện tại.</returns>
    /// <response code="200">Trả danh sách (có thể rỗng nếu Customer chưa được gán asset).</response>
    /// <response code="401">Chưa đăng nhập hoặc token không có claim UserId hợp lệ.</response>
    /// <response code="403">Không có role Customer.</response>
    [HttpGet("me")]
    [Authorize(Roles = "Customer")]
    [ProducesResponseType(typeof(CommonResponse<PaginationResponse<BatteryAssetDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetMine([FromQuery] GetMyBatteryAssetsQuery query, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(query, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Lấy chi tiết một BatteryAsset theo Id.
    /// </summary>
    /// <remarks>
    /// Trả đầy đủ thông tin asset + tên BatteryType, Site.
    ///
    /// Cách hoạt động:
    /// - Tìm asset theo <c>Id</c> + <c>!IsDeleted</c>, include navigation properties.
    /// - 404 nếu không tồn tại hoặc đã bị soft delete.
    ///
    /// Lưu ý phân quyền:
    /// - Endpoint cho phép cả Customer gọi (để xem chi tiết asset của họ).
    /// - <b>Hiện tại</b> chưa enforce server-side rằng Customer chỉ xem được asset của chính mình tại tầng controller này; logic dữ liệu dựa vào fact rằng Customer chỉ biết Id asset của họ.
    ///   FE/Mobile nên dùng <c>GET /me</c> để lấy danh sách rồi mới xem detail.
    /// </remarks>
    /// <param name="id">Id BatteryAsset.</param>
    /// <param name="cancellationToken">Token hủy request.</param>
    /// <returns><see cref="CommonResponse{T}"/> chứa <see cref="BatteryAssetDto"/>.</returns>
    /// <response code="200">Trả về asset.</response>
    /// <response code="401">Chưa đăng nhập.</response>
    /// <response code="403">Không có role Admin/Manager/Staff/Customer.</response>
    /// <response code="404">Không tìm thấy asset.</response>
    [HttpGet("{id:guid}")]
    [Authorize(Roles = "Admin,Manager,Staff,Customer")]
    [ProducesResponseType(typeof(CommonResponse<BatteryAssetDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(CommonResponse<BatteryAssetDto>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetBatteryAssetByIdQuery { Id = id }, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Lấy snapshot realtime của một BatteryAsset: số đo mới nhất + số alert đang mở.
    /// </summary>
    /// <remarks>
    /// Endpoint trả về <see cref="BatteryAssetRealtimeDto"/> gồm:
    /// - <c>AssetId</c>, <c>SerialNumber</c>, <c>Status</c>.
    /// - Thông số reading mới nhất: <c>Time</c>, <c>Voltage</c>, <c>Current</c>, <c>Temperature</c>, <c>SocPercent</c>, <c>CycleCount</c>. Tất cả nullable - <c>null</c> nếu asset chưa từng có reading.
    /// - <c>ActiveAlerts</c>: số lượng alert đang ở trạng thái <c>Open</c> hoặc <c>Acknowledged</c> (không tính <c>Resolved</c>, <c>Merged</c>) trên asset này.
    ///
    /// Cách hoạt động:
    /// - 1 query cho asset (404 nếu không có).
    /// - 1 query <c>SensorReadings.OrderByDescending(Time).FirstOrDefault()</c> - TimescaleDB tối ưu thao tác này.
    /// - 1 query <c>CountAsync</c> trên Alerts với filter status.
    ///
    /// Use case:
    /// - Mobile dashboard hiển thị "voltage hiện tại" + badge cảnh báo.
    /// - Manager web view real-time tổng quan 1 asset.
    /// </remarks>
    /// <param name="id">Id BatteryAsset.</param>
    /// <param name="cancellationToken">Token hủy request.</param>
    /// <returns><see cref="CommonResponse{T}"/> chứa <see cref="BatteryAssetRealtimeDto"/>.</returns>
    /// <response code="200">Trả snapshot.</response>
    /// <response code="401">Chưa đăng nhập.</response>
    /// <response code="403">Không có role phù hợp.</response>
    /// <response code="404">Không tìm thấy asset.</response>
    [HttpGet("{id:guid}/realtime")]
    [Authorize(Roles = "Admin,Manager,Staff,Customer")]
    [ProducesResponseType(typeof(CommonResponse<BatteryAssetRealtimeDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(CommonResponse<BatteryAssetRealtimeDto>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetRealtime(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetBatteryAssetRealtimeQuery { Id = id }, cancellationToken);
        return StatusCode(result.StatusCode, result);
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
    /// - Sau khi transfer, Admin nên gán lại Site cho asset qua <c>PUT /api/battery-assets/{id}</c> nếu Customer mới có Site phù hợp.
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
        var result = await _mediator.Send(command, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }
}
