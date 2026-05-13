using AuthService.Application.CQRS.Command.Account;
using AuthService.Application.DTOs.Response.Account;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AuthService.Api.Controllers;

/// <summary>
/// Nhóm API quản trị hồ sơ staff và kỹ năng staff.
/// </summary>
/// <remarks>
/// Controller này dành riêng cho Admin để cấu hình dữ liệu nền phục vụ assignment ở các sprint sau.
/// Nhóm endpoint này tách khỏi <c>/api/auth</c> để auth namespace không bị trộn với thao tác quản trị staff.
///
/// Phạm vi trách nhiệm:
/// - Tạo hoặc cập nhật <c>StaffProfile</c> cho account hiện có.
/// - Thêm, cập nhật hoặc khôi phục <c>StaffSkill</c>.
/// - Xóa mềm skill khỏi hồ sơ staff.
///
/// Ngoài phạm vi:
/// - Không tạo account mới.
/// - Không đổi role hoặc permission.
/// - Không tính workload ticket thực tế vì TicketService chưa thuộc Sprint 1.
/// </remarks>
[ApiController]
[Route("api/admin/staff")]
[Produces("application/json")]
[Authorize(Roles = "Admin")]
public class AdminStaffProfilesController : ControllerBase
{
    private readonly IMediator _mediator;

    public AdminStaffProfilesController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Tạo hoặc cập nhật hồ sơ staff cho một account.
    /// </summary>
    /// <remarks>
    /// Endpoint này cấu hình phần staff-specific tách khỏi bảng <c>Account</c>.
    /// Cách tách này giúp AuthService giữ <c>Account</c> gọn cho identity/auth, còn dữ liệu vận hành staff nằm trong <c>StaffProfile</c>.
    ///
    /// Path parameter:
    /// - <c>id</c>: AccountId cần cấu hình staff profile.
    ///
    /// Body request:
    /// - <c>employeeCode</c>: tùy chọn, tối đa 50 ký tự, phải duy nhất nếu có truyền.
    /// - <c>department</c>: tùy chọn, tối đa 100 ký tự.
    /// - <c>maxConcurrentTickets</c>: số ticket tối đa có thể xử lý đồng thời, hợp lệ từ 1 đến 50.
    /// - <c>isAvailable</c>: staff có đang sẵn sàng nhận assignment hay không.
    /// - <c>notes</c>: ghi chú vận hành nội bộ, tối đa 1000 ký tự.
    ///
    /// Cách hoạt động:
    /// - Nếu account không tồn tại, trả 404.
    /// - Nếu account tồn tại nhưng chưa có <c>StaffProfile</c>, hệ thống tạo mới profile.
    /// - Nếu đã có <c>StaffProfile</c>, hệ thống cập nhật các field được gửi lên.
    /// - Nếu <c>employeeCode</c> trùng với staff khác, trả 409.
    ///
    /// Use case:
    /// - Admin onboard một staff sau khi account đã được tạo.
    /// - Admin cập nhật khả năng nhận ticket, phòng ban hoặc trạng thái available.
    /// - Chuẩn bị dữ liệu để TicketService/SLA engine dùng trong các sprint sau.
    /// </remarks>
    /// <param name="id">AccountId cần tạo hoặc cập nhật staff profile.</param>
    /// <param name="command">Thông tin staff profile cần lưu.</param>
    /// <param name="cancellationToken">Token hủy request khi client ngắt kết nối hoặc server dừng xử lý.</param>
    /// <returns><see cref="AccountActionResponse"/> chứa kết quả thao tác và AccountId được cập nhật.</returns>
    /// <response code="200">Cập nhật staff profile thành công.</response>
    /// <response code="400">Dữ liệu đầu vào không hợp lệ.</response>
    /// <response code="401">Chưa đăng nhập hoặc access token không hợp lệ/hết hạn.</response>
    /// <response code="403">Account đăng nhập không có role Admin.</response>
    /// <response code="404">Không tìm thấy account cần cấu hình.</response>
    /// <response code="409">EmployeeCode đã được sử dụng bởi staff khác.</response>
    [HttpPut("{id:guid}/profile")]
    [ProducesResponseType(typeof(AccountActionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(AccountActionResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(AccountActionResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(AccountActionResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(AccountActionResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(AccountActionResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateStaffProfile(Guid id, [FromBody] UpdateStaffProfileCommand command, CancellationToken cancellationToken)
    {
        command.AccountId = id;
        var result = await _mediator.Send(command, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Thêm hoặc cập nhật kỹ năng của một staff.
    /// </summary>
    /// <remarks>
    /// Endpoint này quản lý bảng <c>StaffSkill</c> theo cặp <c>StaffAccountId</c> và <c>SkillCode</c>.
    /// Nếu skill chưa tồn tại thì tạo mới; nếu skill đã tồn tại thì cập nhật level/certification; nếu skill đã bị xóa mềm thì khôi phục.
    ///
    /// Path parameter:
    /// - <c>id</c>: AccountId của staff cần thêm/cập nhật skill.
    ///
    /// Body request:
    /// - <c>skillCode</c>: bắt buộc, tối đa 64 ký tự, ví dụ <c>LiFePO4</c>, <c>Inverter</c>, <c>BMS</c>.
    /// - <c>skillLevel</c>: mức độ kỹ năng, hợp lệ từ 1 đến 5.
    /// - <c>certifiedUntil</c>: ngày hết hạn chứng chỉ nếu có; có thể null nếu skill không cần chứng chỉ.
    ///
    /// Cách hoạt động:
    /// - Nếu account không tồn tại, trả 404.
    /// - Nếu staff chưa có <c>StaffProfile</c>, hệ thống tự tạo profile rỗng để gắn skill.
    /// - Skill code được trim trước khi lưu để tránh sai lệch do khoảng trắng.
    /// - Response trả AccountId của staff để FE có thể reload assignment profile.
    ///
    /// Lưu ý nghiệp vụ:
    /// - Endpoint này chưa validate skill code với catalog riêng vì catalog skill chưa nằm trong Sprint 1.
    /// - Manager chỉ đọc skill qua <c>/api/staff</c>; chỉ Admin được thay đổi skill.
    /// </remarks>
    /// <param name="id">AccountId của staff cần thêm hoặc cập nhật skill.</param>
    /// <param name="command">Thông tin skill cần lưu.</param>
    /// <param name="cancellationToken">Token hủy request khi client ngắt kết nối hoặc server dừng xử lý.</param>
    /// <returns><see cref="AccountActionResponse"/> chứa kết quả thao tác và AccountId của staff.</returns>
    /// <response code="200">Thêm hoặc cập nhật staff skill thành công.</response>
    /// <response code="400">Dữ liệu đầu vào không hợp lệ.</response>
    /// <response code="401">Chưa đăng nhập hoặc access token không hợp lệ/hết hạn.</response>
    /// <response code="403">Account đăng nhập không có role Admin.</response>
    /// <response code="404">Không tìm thấy account staff.</response>
    [HttpPost("{id:guid}/skills")]
    [ProducesResponseType(typeof(AccountActionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(AccountActionResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(AccountActionResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(AccountActionResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(AccountActionResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AddStaffSkill(Guid id, [FromBody] AddStaffSkillCommand command, CancellationToken cancellationToken)
    {
        command.StaffAccountId = id;
        var result = await _mediator.Send(command, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Xóa một kỹ năng khỏi hồ sơ staff.
    /// </summary>
    /// <remarks>
    /// Endpoint này xóa mềm một <c>StaffSkill</c> dựa theo AccountId của staff và skill code.
    /// Dữ liệu bị xóa mềm sẽ không còn xuất hiện trong danh sách assignment profile thông thường.
    ///
    /// Path parameters:
    /// - <c>id</c>: AccountId của staff cần xóa skill.
    /// - <c>skillCode</c>: mã skill cần xóa, ví dụ <c>LiFePO4</c>.
    ///
    /// Cách hoạt động:
    /// - Hệ thống validate AccountId và skillCode.
    /// - Tìm skill đang active theo đúng AccountId và skillCode.
    /// - Gọi repository delete để đánh dấu xóa theo cơ chế soft delete hiện tại.
    ///
    /// Lưu ý:
    /// - Nếu sau này Admin thêm lại cùng skill bằng <c>POST /api/admin/staff/{id}/skills</c>, handler sẽ khôi phục record đã xóa mềm.
    /// - Endpoint này không xóa staff profile và không ảnh hưởng account/role của staff.
    ///
    /// Lỗi thường gặp:
    /// - HTTP 400 nếu id hoặc skillCode không hợp lệ.
    /// - HTTP 404 nếu staff không có skill active tương ứng.
    /// - HTTP 401/403 nếu thiếu quyền Admin.
    /// </remarks>
    /// <param name="id">AccountId của staff cần xóa skill.</param>
    /// <param name="skillCode">Mã skill cần xóa khỏi staff profile.</param>
    /// <param name="cancellationToken">Token hủy request khi client ngắt kết nối hoặc server dừng xử lý.</param>
    /// <returns><see cref="AccountActionResponse"/> chứa kết quả thao tác và AccountId của staff.</returns>
    /// <response code="200">Xóa staff skill thành công.</response>
    /// <response code="400">Dữ liệu đầu vào không hợp lệ.</response>
    /// <response code="401">Chưa đăng nhập hoặc access token không hợp lệ/hết hạn.</response>
    /// <response code="403">Account đăng nhập không có role Admin.</response>
    /// <response code="404">Không tìm thấy staff skill cần xóa.</response>
    [HttpDelete("{id:guid}/skills/{skillCode}")]
    [ProducesResponseType(typeof(AccountActionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(AccountActionResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(AccountActionResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(AccountActionResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(AccountActionResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteStaffSkill(Guid id, string skillCode, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new DeleteStaffSkillCommand
        {
            StaffAccountId = id,
            SkillCode = skillCode
        }, cancellationToken);

        return StatusCode(result.StatusCode, result);
    }
}
