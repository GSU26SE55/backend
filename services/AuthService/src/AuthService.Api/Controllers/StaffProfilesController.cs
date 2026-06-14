using AuthService.Application.CQRS.Query.Account;
using AuthService.Application.DTOs.Response.Account;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AuthService.Api.Controllers;

/// <summary>
/// Nhóm API đọc thông tin staff phục vụ phân công ticket và điều phối bảo trì.
/// </summary>
/// <remarks>
/// Controller này tách riêng khỏi <c>/api/auth</c> để giữ namespace auth chỉ dành cho đăng nhập,
/// token và self-profile. Các endpoint staff hiện chỉ đọc dữ liệu staff profile, không thay đổi role,
/// không tạo account mới và không cập nhật thông tin quản trị.
///
/// Quyền truy cập:
/// - Admin và Manager được xem danh sách staff và assignment profile.
/// - Staff/Customer không được gọi nhóm endpoint này trừ khi được mở quyền sau này.
/// </remarks>
[ApiController]
[Route("api/staff")]
[Produces("application/json")]
[Authorize(Roles = "Admin,Manager")]
public class StaffProfilesController : ControllerBase
{
    private readonly IMediator _mediator;

    public StaffProfilesController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Lấy danh sách staff có thể dùng cho màn hình phân công ticket.
    /// </summary>
    /// <remarks>
    /// Endpoint này trả danh sách staff profile đã được chuẩn hóa cho FE hoặc service nghiệp vụ cần chọn người xử lý ticket.
    /// Dữ liệu được lấy từ <c>StaffProfile</c>, join với <c>Account</c>, <c>AccountProfile</c> và danh sách <c>StaffSkill</c>.
    ///
    /// Query string:
    /// - <c>skill</c>: tùy chọn. Khi truyền, hệ thống chỉ trả staff có skill code đúng bằng giá trị này,
    ///   ví dụ <c>LiFePO4</c>. Nếu bỏ trống, trả toàn bộ staff profile hiện có.
    ///
    /// Dữ liệu trả về cho mỗi staff gồm:
    /// - <c>accountId</c>, email, họ tên và số điện thoại liên hệ.
    /// - <c>department</c>, <c>maxConcurrentTickets</c> và <c>isAvailable</c> để hỗ trợ assignment.
    /// - <c>displayAvatarUrl</c> đã resolve theo cùng rule với profile cá nhân.
    /// - Danh sách skill gồm <c>skillCode</c>, <c>skillLevel</c> và <c>certifiedUntil</c>.
    ///
    /// Cách sắp xếp:
    /// - Staff đang available được ưu tiên lên trước.
    /// - Sau đó sắp xếp theo họ tên để danh sách ổn định cho FE.
    ///
    /// Lưu ý nghiệp vụ:
    /// - Endpoint này chỉ phục vụ đọc dữ liệu assignment foundation ở Sprint 1.
    /// - Chưa tính workload thực tế từ TicketService vì TicketService chưa nằm trong scope Sprint 1.
    /// - <c>maxConcurrentTickets</c> là cấu hình năng lực tối đa, chưa phải số ticket đang nhận.
    /// </remarks>
    /// <param name="skill">Skill code cần lọc, ví dụ <c>LiFePO4</c>. Bỏ trống để lấy toàn bộ staff.</param>
    /// <param name="cancellationToken">Token hủy request khi client ngắt kết nối hoặc server dừng xử lý.</param>
    /// <returns><see cref="StaffAssignmentProfileListResponse"/> chứa danh sách staff phù hợp.</returns>
    /// <response code="200">Lấy danh sách staff thành công.</response>
    /// <response code="401">Chưa đăng nhập hoặc access token không hợp lệ/hết hạn.</response>
    /// <response code="403">Account đăng nhập không có role Admin hoặc Manager.</response>
    [HttpGet]
    [ProducesResponseType(typeof(StaffAssignmentProfileListResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(StaffAssignmentProfileListResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(StaffAssignmentProfileListResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetStaff([FromQuery] string? skill = null, CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(new GetStaffQuery { Skill = skill }, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Lấy hồ sơ phân công chi tiết của 1 staff (tier + skills + current workload + max concurrent) — Manager dùng để tính fitness score khi assign ticket.
    /// </summary>
    /// <remarks>
    /// Endpoint này dùng khi Manager/Admin cần xem chi tiết một staff trước khi gán ticket hoặc kiểm tra năng lực xử lý.
    ///
    /// Path parameter:
    /// - <c>id</c>: AccountId của staff cần xem assignment profile.
    ///
    /// Dữ liệu trả về gồm:
    /// - Thông tin account cơ bản: id, email, họ tên, số điện thoại.
    /// - Thông tin staff profile: phòng ban, giới hạn ticket đồng thời, trạng thái available và ghi chú vận hành.
    /// - Danh sách skill/certification đang có.
    /// - <c>displayAvatarUrl</c> đã resolve để FE hiển thị avatar trong màn hình assignment.
    ///
    /// Lưu ý:
    /// - Endpoint này yêu cầu account đã có <c>StaffProfile</c>. Nếu account tồn tại nhưng chưa được cấu hình staff profile,
    ///   hệ thống trả 404 vì không có dữ liệu assignment để sử dụng.
    /// - Endpoint này không sửa dữ liệu staff. Mọi thay đổi profile/skill đi qua nhóm <c>/api/admin/staff</c>.
    ///
    /// Lỗi thường gặp:
    /// - HTTP 400 nếu id không phải GUID hợp lệ hoặc request validation fail.
    /// - HTTP 404 nếu không tìm thấy staff profile tương ứng.
    /// - HTTP 401/403 nếu thiếu quyền truy cập.
    /// </remarks>
    /// <param name="id">AccountId của staff cần xem hồ sơ phân công.</param>
    /// <param name="cancellationToken">Token hủy request khi client ngắt kết nối hoặc server dừng xử lý.</param>
    /// <returns><see cref="StaffAssignmentProfileResponse"/> chứa hồ sơ phân công của staff.</returns>
    /// <response code="200">Lấy staff assignment profile thành công.</response>
    /// <response code="400">Staff account id không hợp lệ.</response>
    /// <response code="401">Chưa đăng nhập hoặc access token không hợp lệ/hết hạn.</response>
    /// <response code="403">Account đăng nhập không có role Admin hoặc Manager.</response>
    /// <response code="404">Không tìm thấy staff profile.</response>
    [HttpGet("{id:guid}/assignment-profile")]
    [ProducesResponseType(typeof(StaffAssignmentProfileResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(StaffAssignmentProfileResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(StaffAssignmentProfileResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(StaffAssignmentProfileResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(StaffAssignmentProfileResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetStaffAssignmentProfile(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetStaffAssignmentProfileQuery { StaffAccountId = id }, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }
}
