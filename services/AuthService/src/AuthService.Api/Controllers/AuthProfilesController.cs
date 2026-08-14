using System.Security.Claims;
using AuthService.Application.CQRS.Command.Account;
using AuthService.Application.CQRS.Query.Account;
using AuthService.Application.CQRS.Query.Permission;
using AuthService.Application.DTOs.Response.Account;
using AuthService.Application.DTOs.Response.Permission;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AuthService.Api.Controllers;

/// <summary>
/// Nhóm API profile mở rộng cho chính tài khoản đang đăng nhập.
/// </summary>
/// <remarks>
/// Controller này chỉ xử lý các thao tác self-service của user hiện tại:
/// - Xem profile tổng hợp của chính mình.
/// - Cập nhật thông tin cá nhân nằm trong <c>Account</c> và <c>AccountProfile</c>.
/// - Gắn avatar upload từ FileStorageService thông qua <c>avatarFileId</c>.
///
/// Các endpoint trong controller này không dùng để quản trị staff, role hoặc tài khoản khác.
/// AccountId luôn được lấy từ claim <c>NameIdentifier</c> trong access token để tránh client truyền id của người khác.
/// </remarks>
[ApiController]
[Route("api/auth")]
[Produces("application/json")]
[Authorize]
public class AuthProfilesController : ControllerBase
{
    private readonly IMediator _mediator;

    public AuthProfilesController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Lấy thông tin profile tổng hợp của tài khoản đang đăng nhập.
    /// </summary>
    /// <remarks>
    /// Endpoint này dùng cho màn hình "My Profile" hoặc bước đồng bộ lại user context sau khi login.
    /// Client không truyền account id; hệ thống đọc AccountId từ JWT để đảm bảo user chỉ xem dữ liệu của chính mình.
    ///
    /// Dữ liệu trả về gồm:
    /// - Thông tin account cơ bản như id, email, số điện thoại, họ tên, trạng thái và roles.
    /// - <c>profile</c>: phần mở rộng cá nhân như avatar file, avatar Google, địa chỉ, ngày sinh và timezone.
    /// - <c>staffProfile</c>: thông tin staff nếu account này có hồ sơ staff; user thường không phải staff có thể nhận null.
    /// - <c>displayAvatarUrl</c>: URL avatar đã resolve sẵn cho FE.
    ///
    /// Quy tắc resolve <c>displayAvatarUrl</c>:
    /// - Ưu tiên avatar upload qua FileStorageService: <c>/api/files/{avatarFileId}/download</c>.
    /// - Nếu chưa có avatar upload thì dùng Google picture lưu trong <c>ExternalAvatarUrl</c>.
    /// - Nếu không có cả hai thì trả null để FE hiển thị placeholder.
    ///
    /// Quyền truy cập:
    /// - Yêu cầu access token hợp lệ.
    /// - Mọi role đăng nhập đều có thể gọi endpoint này cho chính mình.
    /// </remarks>
    /// <param name="cancellationToken">Token hủy request khi client ngắt kết nối hoặc server dừng xử lý.</param>
    /// <returns><see cref="AccountResponse"/> chứa profile tổng hợp của user hiện tại.</returns>
    /// <response code="200">Lấy profile thành công.</response>
    /// <response code="401">Chưa đăng nhập hoặc access token không hợp lệ/hết hạn.</response>
    [HttpGet("me")]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(AccountResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(AccountResponse), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetMe(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetMyProfileQuery(), cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Lấy toàn bộ permission của role mà tài khoản đang đăng nhập được gán.
    /// </summary>
    /// <remarks>
    /// Endpoint dùng để FE build feature-gate / permission-check sau khi login mà không cần decode JWT.
    /// Mọi role đã đăng nhập đều có thể gọi — chỉ cần JWT hợp lệ. Không phân quyền riêng cho Admin.
    ///
    /// Cách hoạt động:
    /// - Controller lấy AccountId từ claim <c>NameIdentifier</c> trong JWT (KHÔNG nhận từ client).
    /// - Handler resolve qua DB: Account.RoleId → Role (Active, chưa xóa) → RolePermission → Permission.
    /// - Resolve theo DB thay vì đọc <c>perm[]</c> trong JWT để luôn trả về snapshot mới nhất —
    ///   phản ánh ngay thay đổi role/permission mà không cần đợi refresh token.
    ///
    /// Response shape <see cref="MyPermissionsDto"/>:
    /// <list type="bullet">
    ///   <item><description><c>RoleId</c>: Guid role hiện tại.</description></item>
    ///   <item><description><c>RoleName</c>: tên role ("Admin" / "Manager" / "Staff" / "Customer").</description></item>
    ///   <item><description><c>Permissions</c>: flat list <see cref="PermissionDto"/> sort theo <c>Module</c> rồi <c>Code</c>.</description></item>
    /// </list>
    ///
    /// Trường hợp đặc biệt:
    /// - Account đã xóa nhưng JWT chưa hết hạn → 401.
    /// - Account chưa được gán role hoặc role đã xóa → 403.
    /// - Role tồn tại nhưng <c>Status != Active</c> (Inactive/Deprecated) → 200, <c>Permissions = []</c> + message giải thích.
    /// </remarks>
    /// <param name="cancellationToken">Token hủy request khi client ngắt kết nối hoặc server dừng xử lý.</param>
    /// <returns><see cref="MyPermissionsResponse"/> chứa role + danh sách permission.</returns>
    /// <response code="200">Lấy danh sách permission thành công (kể cả khi list rỗng do role không active).</response>
    /// <response code="401">Chưa đăng nhập, access token không hợp lệ/hết hạn, hoặc account đã bị xóa.</response>
    /// <response code="403">Account chưa được gán role.</response>
    [HttpGet("me/permissions")]
    [ProducesResponseType(typeof(MyPermissionsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MyPermissionsResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(MyPermissionsResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetMyPermissions(CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
            return Unauthorized(UnauthMyPermissionsResponse());

        var result = await _mediator.Send(new GetMyPermissionsQuery { AccountId = userId.Value }, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Cập nhật thông tin profile cá nhân của tài khoản đang đăng nhập.
    /// </summary>
    /// <remarks>
    /// Endpoint này cho phép user tự cập nhật thông tin cá nhân nhưng không cho phép đổi role,
    /// email đăng nhập, mật khẩu, trạng thái account hoặc dữ liệu staff-specific.
    ///
    /// Body request:
    /// - <c>fullName</c>: bắt buộc, tối đa 150 ký tự.
    /// - <c>phoneNumber</c>: tùy chọn, tối đa 20 ký tự.
    /// - <c>address</c>: tùy chọn, tối đa 500 ký tự.
    /// - <c>birthDate</c>: tùy chọn, không được là ngày trong tương lai và năm sinh phải từ 1900 trở lên.
    /// - <c>timeZone</c>: tùy chọn, tối đa 100 ký tự, ví dụ <c>Asia/Ho_Chi_Minh</c>.
    ///
    /// Cách hoạt động:
    /// - Controller lấy AccountId từ JWT và gán vào command, bỏ qua mọi AccountId từ client.
    /// - Handler cập nhật <c>Account.FullName</c>, <c>Account.PhoneNumber</c> và tạo/cập nhật <c>AccountProfile</c> nếu cần.
    /// - Response trả lại cùng shape với <c>GET /api/auth/me</c> để FE có thể refresh state ngay sau khi lưu.
    ///
    /// Lỗi thường gặp:
    /// - HTTP 400 nếu dữ liệu không đạt validation.
    /// - HTTP 401 nếu thiếu hoặc sai access token.
    /// - HTTP 404 nếu account trong token không còn tồn tại.
    /// - HTTP 409 nếu số điện thoại hoặc dữ liệu có ràng buộc bị trùng theo rule nghiệp vụ.
    /// </remarks>
    /// <param name="command">Thông tin profile cần cập nhật.</param>
    /// <param name="cancellationToken">Token hủy request khi client ngắt kết nối hoặc server dừng xử lý.</param>
    /// <returns><see cref="AccountResponse"/> chứa profile mới sau khi cập nhật thành công.</returns>
    /// <response code="200">Cập nhật profile thành công.</response>
    /// <response code="400">Dữ liệu đầu vào không hợp lệ.</response>
    /// <response code="401">Chưa đăng nhập hoặc access token không hợp lệ/hết hạn.</response>
    /// <response code="404">Không tìm thấy tài khoản hiện tại.</response>
    /// <response code="409">Dữ liệu cập nhật bị xung đột với account khác hoặc rule nghiệp vụ.</response>
    [HttpPut("me/profile")]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(AccountResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(AccountResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(AccountResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(AccountResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(AccountResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateMyProfile([FromBody] UpdateMyProfileCommand command, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
            return Unauthorized(UnauthAccountResponse());

        command.AccountId = userId.Value;
        var result = await _mediator.Send(command, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Gắn avatar upload (đã upload qua FileStorageService) cho tài khoản đang đăng nhập — endpoint nhận avatar URL/fileId từ FileStorage response, save vào account.AvatarUrl.
    /// </summary>
    /// <remarks>
    /// Endpoint này là bước thứ hai của flow upload avatar:
    /// - FE upload ảnh lên FileStorageService bằng <c>POST /api/files/upload</c> với purpose phù hợp, ví dụ <c>Avatar</c>.
    /// - FileStorageService trả về <c>fileId</c>.
    /// - FE gọi endpoint này với <c>avatarFileId</c> để AuthService lưu avatar vào <c>AccountProfile</c>.
    ///
    /// Body request:
    /// - <c>avatarFileId</c>: id metadata file đã được FileStorageService trả về sau khi upload thành công.
    ///
    /// Cách hoạt động:
    /// - Controller lấy AccountId từ JWT và gán vào command.
    /// - Handler tạo <c>AccountProfile</c> nếu user chưa có profile mở rộng.
    /// - <c>AvatarFileId</c> được lưu vào profile và <c>AvatarSource</c> được đặt thành Uploaded.
    /// - Google picture cũ trong <c>ExternalAvatarUrl</c> vẫn được giữ lại làm fallback, nhưng không còn được ưu tiên hiển thị.
    ///
    /// Lưu ý:
    /// - Endpoint này chỉ lưu tham chiếu fileId, không upload binary.
    /// - AuthService không lưu Google URL vào <c>AvatarFileId</c>.
    /// - Khi user đã upload avatar, các lần Google login sau không được ghi đè avatar upload này.
    /// </remarks>
    /// <param name="command">Thông tin avatar file cần gắn vào profile.</param>
    /// <param name="cancellationToken">Token hủy request khi client ngắt kết nối hoặc server dừng xử lý.</param>
    /// <returns><see cref="AccountResponse"/> chứa profile mới và <c>displayAvatarUrl</c> đã resolve.</returns>
    /// <response code="200">Gắn avatar thành công.</response>
    /// <response code="400">Thiếu hoặc truyền <c>avatarFileId</c> không hợp lệ.</response>
    /// <response code="401">Chưa đăng nhập hoặc access token không hợp lệ/hết hạn.</response>
    /// <response code="404">Không tìm thấy tài khoản hiện tại.</response>
    [HttpPost("me/avatar")]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(AccountResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(AccountResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(AccountResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(AccountResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetMyAvatar([FromBody] SetMyAvatarCommand command, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
            return Unauthorized(UnauthAccountResponse());

        command.AccountId = userId.Value;
        var result = await _mediator.Send(command, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    private Guid? GetCurrentUserId()
    {
        var raw = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(raw, out var id) ? id : null;
    }

    private static AccountResponse UnauthAccountResponse() => new()
    {
        IsSuccess = false,
        StatusCode = 401,
        Message = "Not logged in."
    };

    private static MyPermissionsResponse UnauthMyPermissionsResponse() => new()
    {
        IsSuccess = false,
        StatusCode = 401,
        Message = "Not logged in."
    };
}
