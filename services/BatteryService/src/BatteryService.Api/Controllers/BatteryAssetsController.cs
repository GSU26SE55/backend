using BatteryService.Application.CQRS.Command.BatteryAsset;
using BatteryService.Application.CQRS.Query.BatteryAsset;
using BatteryService.Application.CQRS.Query.Maintenance;
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
    /// Customer xem danh sách BatteryAsset của chính mình (auto filter theo current userId từ JWT) — Mobile app render danh sách pin trong account.
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
    /// Lấy chi tiết 1 BatteryAsset theo Id — full metadata + warranty + location + last reading snapshot. Customer chỉ xem được asset thuộc về mình.
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

    /// <summary>Periodic maintenance history for one battery, newest cycle first.</summary>
    /// <remarks>
    /// Lịch sử ở tầng TÀI SẢN — mỗi dòng là một kỳ bảo trì định kỳ (đến hạn khi nào, làm
    /// xong lúc nào, đúng hạn hay trễ, SoH tại thời điểm đó). Khác với maintenance log bên
    /// TicketService: log là báo cáo công việc Staff ghi trong lúc xử lý một ticket.
    /// </remarks>
    [HttpGet("{id:guid}/maintenance-cycles")]
    [Authorize(Roles = "Admin,Manager,Staff,Customer")]
    [ProducesResponseType(typeof(CommonResponse<List<MaintenanceCycleDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetMaintenanceCycles(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new GetMaintenanceCyclesQuery { BatteryAssetId = id }, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>Send a charge/discharge MOSFET command to the JK BMS through the site's gateway.</summary>
    [HttpPost("{id:guid}/bms-switch")]
    [Authorize(Roles = "Admin,Manager,Staff,Customer")]
    [ProducesResponseType(typeof(CommonResponse<BmsSwitchCommandAcceptedDto>), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(CommonResponse<BmsSwitchCommandAcceptedDto>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(CommonResponse<BmsSwitchCommandAcceptedDto>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(CommonResponse<BmsSwitchCommandAcceptedDto>), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(CommonResponse<BmsSwitchCommandAcceptedDto>), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> SetBmsSwitch(
        Guid id,
        [FromBody] SetBmsSwitchRequestDto body,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new SetBmsSwitchCommand
        {
            BatteryAssetId = id,
            Target = body?.Target ?? string.Empty,
            Enable = body?.Enable ?? false
        }, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>Read the last verified BMS switch state and any pending command.</summary>
    [HttpGet("{id:guid}/bms-switch")]
    [Authorize(Roles = "Admin,Manager,Staff,Customer")]
    [ProducesResponseType(typeof(CommonResponse<BmsSwitchStateDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(CommonResponse<BmsSwitchStateDto>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(CommonResponse<BmsSwitchStateDto>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> GetBmsSwitch(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetBmsSwitchStateQuery { BatteryAssetId = id }, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

}
