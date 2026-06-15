using AuthService.Application.CQRS.Command.Account;
using AuthService.Application.CQRS.Query.Account;
using AuthService.Application.CQRS.Query.Login;
using AuthService.Application.DTOs.Response.Account;
using AuthService.Application.DTOs.Response.Login;
using AuthService.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SharedContracts.Common.Responses;

namespace AuthService.Api.Controllers.Admin;

/// <summary>
/// Module admin quản lý tài khoản: CRUD, gán/thu hồi role, đổi trạng thái.
/// Toàn bộ endpoint trong controller này chỉ dành cho Admin/Manager theo từng action.
/// </summary>
[ApiController]
[Route("api/admin/accounts")]
[Produces("application/json")]
[ApiExplorerSettings(GroupName = "admin")]
[Authorize]
public class AdminAccountsController : ControllerBase
{
    private readonly IMediator _mediator;

    public AdminAccountsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Admin liệt kê tất cả account với phân trang + filter nâng cao (role/status/email/createdAt range) — sort theo CreatedAt DESC. Dùng cho admin dashboard quản lý người dùng.
    /// </summary>
    /// <remarks>
    /// Endpoint này dành cho màn hình quản trị danh sách user/account.
    ///
    /// Quyền truy cập:
    /// - Role <c>Admin</c> hoặc <c>Manager</c>.
    ///
    /// Query parameters:
    /// - <c>pageNumber</c>: số trang, mặc định 1.
    /// - <c>pageSize</c>: số item mỗi trang, mặc định 10.
    /// - <c>keyword</c>: từ khóa tìm kiếm, thường dùng cho email, họ tên hoặc thông tin nhận diện khác theo logic query.
    /// - <c>status</c>: lọc theo trạng thái account.
    /// - <c>roleId</c>: lọc account đang có role cụ thể.
    /// - <c>emailConfirmed</c>: lọc theo trạng thái xác thực email.
    ///
    /// Cách hoạt động:
    /// - Controller gom query string vào <see cref="GetAccountsQuery"/>.
    /// - Application layer thực hiện filter, phân trang và trả metadata kèm danh sách account.
    ///
    /// Use case:
    /// - Trang admin quản lý người dùng.
    /// - Tìm tài khoản theo email/tên.
    /// - Lọc tài khoản chưa xác thực email hoặc đang bị khóa/vô hiệu hóa.
    /// </remarks>
    /// <param name="pageNumber">Số trang cần lấy, mặc định 1.</param>
    /// <param name="pageSize">Số bản ghi mỗi trang, mặc định 10.</param>
    /// <param name="keyword">Từ khóa tìm kiếm tùy chọn.</param>
    /// <param name="status">Trạng thái account cần lọc.</param>
    /// <param name="roleId">Role id cần lọc.</param>
    /// <param name="emailConfirmed">Trạng thái xác thực email cần lọc.</param>
    /// <param name="cancellationToken">Token hủy request khi client ngắt kết nối hoặc server dừng xử lý.</param>
    /// <returns>Danh sách account có phân trang.</returns>
    /// <response code="200">Lấy danh sách tài khoản thành công.</response>
    /// <response code="401">Chưa đăng nhập.</response>
    /// <response code="403">Không có role Admin hoặc Manager.</response>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [Authorize(Roles = "Admin,Manager")]
    [ProducesResponseType(typeof(AccountListResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] string? keyword = null,
        [FromQuery] AccountStatusEnum? status = null,
        [FromQuery] Guid? roleId = null,
        [FromQuery] bool? emailConfirmed = null,
        CancellationToken cancellationToken = default)
    {
        var query = new GetAccountsQuery
        {
            PageNumber = pageNumber,
            PageSize = pageSize,
            Keyword = keyword,
            Status = status,
            RoleId = roleId,
            EmailConfirmed = emailConfirmed
        };

        var result = await _mediator.Send(query, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Admin lấy chi tiết 1 account theo Id — trả full profile bao gồm role assignments, 2FA status, ProviderLinks, audit trail counts.
    /// </summary>
    /// <remarks>
    /// Endpoint này trả thông tin chi tiết của một account bất kỳ cho admin/manager.
    ///
    /// Quyền truy cập:
    /// - Role <c>Admin</c> hoặc <c>Manager</c>.
    ///
    /// Path parameter:
    /// - <c>id</c>: id account cần xem.
    ///
    /// Response thường dùng để:
    /// - Xem profile đầy đủ của user.
    /// - Kiểm tra trạng thái email/phone/account.
    /// - Kiểm tra role hiện tại trước khi gán hoặc thu hồi quyền.
    ///
    /// Nếu không tìm thấy account hoặc account đã bị loại khỏi phạm vi query theo rule repository, API trả HTTP 404.
    /// </remarks>
    /// <param name="id">Id account cần xem chi tiết.</param>
    /// <param name="cancellationToken">Token hủy request khi client ngắt kết nối hoặc server dừng xử lý.</param>
    /// <returns>Thông tin chi tiết account.</returns>
    /// <response code="200">Lấy chi tiết tài khoản thành công.</response>
    /// <response code="401">Chưa đăng nhập.</response>
    /// <response code="403">Không có role Admin hoặc Manager.</response>
    /// <response code="404">Không tìm thấy tài khoản.</response>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [Authorize(Roles = "Admin,Manager")]
    [ProducesResponseType(typeof(AccountResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(AccountResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetAccountByIdQuery { Id = id }, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Admin tạo mới 1 account thủ công (bỏ qua flow self-register) — hệ thống gửi email kèm reset-password token để user set password lần đầu.
    /// </summary>
    /// <remarks>
    /// Endpoint này cho phép admin tạo account trực tiếp, không đi qua luồng đăng ký tự phục vụ.
    ///
    /// Quyền truy cập:
    /// - Chỉ role <c>Admin</c>.
    ///
    /// Body request:
    /// - <c>Email</c>: bắt buộc, tối đa 256 ký tự, đúng định dạng email.
    /// - <c>Password</c>: bắt buộc, 8-100 ký tự và phải có chữ hoa, chữ thường, số, ký tự đặc biệt.
    /// - <c>FullName</c>: bắt buộc, tối đa 150 ký tự.
    /// - <c>PhoneNumber</c>: tùy chọn, tối đa 20 ký tự.
    /// - <c>DateOfBirth</c>: tùy chọn, không được ở tương lai.
    /// - <c>Address</c>: tùy chọn, tối đa 500 ký tự.
    /// - <c>RoleId</c>: role gán cho account mới, bắt buộc và phải là Guid hợp lệ + role đang Active.
    ///
    /// Cách hoạt động:
    /// - Validate dữ liệu và kiểm tra trùng email/phone theo rule hiện có.
    /// - Tạo account bởi admin, thường dùng cho nhân sự nội bộ như Manager/Technician.
    /// - Nếu truyền role, hệ thống gán role ngay sau khi tạo account.
    ///
    /// Lưu ý:
    /// - Endpoint này không phải luồng đăng ký public và không nên mở cho user thường.
    /// - Admin cần truyền mật khẩu ban đầu an toàn và buộc user đổi mật khẩu nếu nghiệp vụ yêu cầu.
    /// </remarks>
    /// <param name="command">Thông tin account cần tạo.</param>
    /// <param name="cancellationToken">Token hủy request khi client ngắt kết nối hoặc server dừng xử lý.</param>
    /// <returns>Kết quả tạo tài khoản, thường chứa id account mới.</returns>
    /// <response code="201">Tạo tài khoản thành công.</response>
    /// <response code="400">Dữ liệu đầu vào không hợp lệ.</response>
    /// <response code="401">Chưa đăng nhập.</response>
    /// <response code="403">Không có role Admin.</response>
    /// <response code="409">Email, số điện thoại hoặc dữ liệu unique đã tồn tại.</response>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(AccountActionResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(AccountActionResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(AccountActionResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateAccountCommand command, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(command, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Admin mời 1 user mới qua email (chế độ invite, không set password sẵn).
    /// </summary>
    /// <remarks>
    /// Khác với <c>POST /api/admin/accounts</c> (direct create + set password), endpoint này:
    /// - Không yêu cầu admin nhập password cho user.
    /// - Tạo account ở Status=PendingVerification.
    /// - Sinh invitation token (24h TTL), gửi email mời qua NotificationService.
    /// - User click link trong email → gọi <c>POST /api/auth/accept-invite</c> để đặt mật khẩu lần đầu và kích hoạt.
    ///
    /// Quyền truy cập: chỉ role Admin.
    ///
    /// Body request:
    /// - <c>Email</c>: bắt buộc, đúng định dạng.
    /// - <c>FullName</c>: bắt buộc.
    /// - <c>PhoneNumber</c>: tùy chọn.
    /// - <c>RoleId</c>: bắt buộc, role gán cho user khi accept invite.
    /// </remarks>
    /// <response code="201">Đã tạo account + gửi email invite.</response>
    /// <response code="400">Dữ liệu không hợp lệ hoặc role không tồn tại.</response>
    /// <response code="403">Không có role Admin.</response>
    /// <response code="409">Email hoặc số điện thoại đã được sử dụng.</response>
    [HttpPost("invite")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(AccountActionResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(AccountActionResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(AccountActionResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Invite([FromBody] InviteAccountCommand command, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(command, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Admin cập nhật profile bất kỳ account nào (override quyền owner) — chỉ update field cho phép (FullName/Phone/Avatar); KHÔNG đổi Email/Password qua endpoint này.
    /// </summary>
    /// <remarks>
    /// Endpoint này cho phép admin cập nhật thông tin profile của account bất kỳ.
    ///
    /// Quyền truy cập:
    /// - Chỉ role <c>Admin</c>.
    ///
    /// Path parameter:
    /// - <c>id</c>: id account cần cập nhật.
    ///
    /// Body request:
    /// - <c>FullName</c>: bắt buộc, tối đa 150 ký tự.
    /// - <c>PhoneNumber</c>: tùy chọn, tối đa 20 ký tự.
    /// - <c>AvatarUrl</c>: tùy chọn, tối đa 500 ký tự.
    /// - <c>DateOfBirth</c>: tùy chọn, không được ở tương lai.
    /// - <c>Address</c>: tùy chọn, tối đa 500 ký tự.
    ///
    /// Cách hoạt động:
    /// - Controller gán <c>command.Id</c> từ route để tránh client sửa id trong body.
    /// - Handler validate và cập nhật profile.
    ///
    /// Lưu ý:
    /// - Endpoint này không đổi role, email/password hoặc trạng thái account.
    /// - Gán/thu hồi role dùng các endpoint riêng trong cùng controller.
    /// </remarks>
    /// <param name="id">Id account cần cập nhật.</param>
    /// <param name="command">Thông tin profile mới.</param>
    /// <param name="cancellationToken">Token hủy request khi client ngắt kết nối hoặc server dừng xử lý.</param>
    /// <returns>Kết quả cập nhật tài khoản.</returns>
    /// <response code="200">Cập nhật tài khoản thành công.</response>
    /// <response code="400">Dữ liệu đầu vào không hợp lệ.</response>
    /// <response code="401">Chưa đăng nhập.</response>
    /// <response code="403">Không có role Admin.</response>
    /// <response code="404">Không tìm thấy tài khoản.</response>
    /// <response code="409">Dữ liệu cập nhật bị trùng theo rule nghiệp vụ.</response>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(AccountActionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(AccountActionResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(AccountActionResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(AccountActionResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateAccountCommand command, CancellationToken cancellationToken)
    {
        command.Id = id;
        var result = await _mediator.Send(command, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Admin đổi trạng thái account (Active/Deactivated/Suspended/Locked) — Suspended/Locked force logout ngay; Deactivated cho user tự reactivate bằng email link.
    /// </summary>
    /// <remarks>
    /// Endpoint này dùng để khóa, mở, vô hiệu hóa hoặc thay đổi trạng thái vận hành của account.
    ///
    /// Quyền truy cập:
    /// - Chỉ role <c>Admin</c>.
    ///
    /// Path parameter:
    /// - <c>id</c>: id account cần đổi trạng thái.
    ///
    /// Body request:
    /// - <c>Status</c>: trạng thái mới theo <see cref="AccountStatusEnum"/>.
    /// - <c>Reason</c>: lý do thay đổi trạng thái, tùy chọn, tối đa 500 ký tự.
    ///
    /// Cách hoạt động:
    /// - Controller gán id từ route vào command.
    /// - Handler kiểm tra account tồn tại và cập nhật trạng thái.
    ///
    /// Lưu ý nghiệp vụ:
    /// - Nếu chuyển account sang trạng thái không được đăng nhập, các lần login/refresh sau có thể bị chặn theo logic auth.
    /// - Nếu muốn buộc user đăng xuất ngay khỏi mọi thiết bị, dùng thêm endpoint revoke session.
    /// </remarks>
    /// <param name="id">Id account cần đổi trạng thái.</param>
    /// <param name="command">Trạng thái mới và lý do tùy chọn.</param>
    /// <param name="cancellationToken">Token hủy request khi client ngắt kết nối hoặc server dừng xử lý.</param>
    /// <returns>Kết quả đổi trạng thái account.</returns>
    /// <response code="200">Đổi trạng thái thành công.</response>
    /// <response code="400">Trạng thái hoặc lý do không hợp lệ.</response>
    /// <response code="401">Chưa đăng nhập.</response>
    /// <response code="403">Không có role Admin.</response>
    /// <response code="404">Không tìm thấy tài khoản.</response>
    [HttpPatch("{id:guid}/status")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(AccountActionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(AccountActionResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ChangeStatus(Guid id, [FromBody] ChangeAccountStatusCommand command, CancellationToken cancellationToken)
    {
        command.Id = id;
        var result = await _mediator.Send(command, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Admin xóa mềm account của user khác — set IsDeleted=true + revoke session. Khác DeactivateMe ở chỗ user KHÔNG tự reactivate được, phải Admin restore.
    /// </summary>
    /// <remarks>
    /// Endpoint này đánh dấu account là đã xóa thay vì xóa vật lý khỏi database.
    ///
    /// Quyền truy cập:
    /// - Chỉ role <c>Admin</c>.
    ///
    /// Path parameter:
    /// - <c>id</c>: id account cần xóa mềm.
    ///
    /// Cách hoạt động:
    /// - Handler tìm account và thực hiện soft delete.
    /// - Dữ liệu vẫn còn trong database để phục vụ audit hoặc tránh mất liên kết nghiệp vụ.
    ///
    /// Lưu ý:
    /// - Nếu cần đăng xuất account khỏi mọi thiết bị, kiểm tra handler có revoke session không; nếu không, gọi thêm revoke-all sessions.
    /// - Không dùng endpoint này cho user tự xóa account; user dùng <c>DELETE /api/accounts/me</c>.
    /// </remarks>
    /// <param name="id">Id account cần xóa mềm.</param>
    /// <param name="cancellationToken">Token hủy request khi client ngắt kết nối hoặc server dừng xử lý.</param>
    /// <returns>Kết quả xóa mềm tài khoản.</returns>
    /// <response code="200">Xóa mềm tài khoản thành công.</response>
    /// <response code="401">Chưa đăng nhập.</response>
    /// <response code="403">Không có role Admin.</response>
    /// <response code="404">Không tìm thấy tài khoản.</response>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(AccountActionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(AccountActionResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new DeleteAccountCommand { Id = id }, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Admin reset 2FA cho user khác (cho case user mất hoàn toàn device + hết backup codes).
    /// Clear <c>TwoFactorSecret</c> + flag + xóa hết backup codes. User phải enroll lại nếu muốn dùng 2FA.
    /// </summary>
    /// <remarks>
    /// Quyền: chỉ <c>Admin</c>.
    /// Idempotent — gọi trên account chưa bật 2FA cũng trả 200.
    /// Audit log <c>Admin2FAReset</c>: actor=admin (resolve từ JWT), target=user.
    /// </remarks>
    [HttpDelete("{id:guid}/2fa")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(CommonResponse<string>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(CommonResponse<string>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(CommonResponse<string>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(CommonResponse<string>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Reset2FA(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new AuthService.Application.CQRS.Command.Admin.AdminReset2FACommand { TargetAccountId = id }, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Admin đổi role assignment cho account (Customer → Staff → Manager → Admin) — refresh permissions cache; user nhận role mới ở lần issue JWT tiếp theo.
    /// </summary>
    /// <remarks>
    /// Quan hệ Role ↔ Account là 1-N — mỗi account chỉ có duy nhất 1 role tại bất kỳ thời điểm nào.
    /// Endpoint này thay thế role hiện tại của account bằng role mới được chỉ định.
    ///
    /// Quyền truy cập:
    /// - Chỉ role <c>Admin</c>.
    ///
    /// Path parameter:
    /// - <c>id</c>: id account cần đổi role.
    ///
    /// Body request:
    /// - <c>RoleId</c>: role mới sẽ thay thế role hiện tại; bắt buộc và phải tồn tại + đang Active.
    ///
    /// Cách hoạt động:
    /// - Controller gán AccountId từ route.
    /// - Handler kiểm tra account và role tồn tại + role đang Active.
    /// - Nếu role mới trùng role hiện tại → 200 OK nhưng không phát sinh thay đổi.
    /// - Ghi audit log với metadata <c>previousRoleId</c> + <c>newRoleId</c>.
    ///
    /// Lưu ý:
    /// - Role được đưa vào JWT ở lần issue token tiếp theo.
    /// - Nếu user đang đăng nhập, access token cũ có thể vẫn giữ claim role cũ cho đến khi token hết hạn hoặc được refresh/login lại.
    /// - Vì account bắt buộc phải có 1 role, KHÔNG có endpoint revoke role; muốn "thu hồi" thì đổi sang role thấp hơn (vd Customer).
    /// </remarks>
    /// <param name="id">Id account cần đổi role.</param>
    /// <param name="command">RoleId mới.</param>
    /// <param name="cancellationToken">Token hủy request khi client ngắt kết nối hoặc server dừng xử lý.</param>
    /// <returns>Kết quả đổi role.</returns>
    /// <response code="200">Đổi role thành công (hoặc role mới trùng role cũ — không có thay đổi).</response>
    /// <response code="400">RoleId không hợp lệ hoặc role không tồn tại/đã bị vô hiệu hóa.</response>
    /// <response code="401">Chưa đăng nhập.</response>
    /// <response code="403">Không có role Admin.</response>
    /// <response code="404">Không tìm thấy account.</response>
    [HttpPut("{id:guid}/role")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(AccountActionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(AccountActionResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(AccountActionResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ChangeRole(Guid id, [FromBody] ChangeAccountRoleCommand command, CancellationToken cancellationToken)
    {
        command.AccountId = id;
        var result = await _mediator.Send(command, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Unlock 1 tài khoản đang bị khóa (reset FailedLoginAttempts, LockoutEndAt; chuyển Status sang Active nếu đang Locked).
    /// </summary>
    /// <remarks>
    /// Endpoint này dùng khi account bị khóa do đăng nhập/OTP sai nhiều lần hoặc bị lock theo trạng thái hệ thống.
    ///
    /// Quyền truy cập:
    /// - Chỉ role <c>Admin</c>.
    ///
    /// Path parameter:
    /// - <c>id</c>: id account cần mở khóa.
    ///
    /// Cách hoạt động:
    /// - Reset số lần đăng nhập sai.
    /// - Xóa thời điểm lockout nếu có.
    /// - Nếu account đang ở trạng thái Locked, chuyển về Active theo logic handler.
    ///
    /// Lưu ý:
    /// - Unlock không tự đổi mật khẩu cho user.
    /// - Nếu nghi ngờ account bị chiếm đoạt, admin nên kết hợp revoke sessions hoặc yêu cầu reset password.
    /// </remarks>
    /// <param name="id">Id account cần mở khóa.</param>
    /// <param name="cancellationToken">Token hủy request khi client ngắt kết nối hoặc server dừng xử lý.</param>
    /// <returns>Kết quả mở khóa tài khoản.</returns>
    /// <response code="200">Mở khóa thành công.</response>
    /// <response code="400">Id account không hợp lệ.</response>
    /// <response code="401">Chưa đăng nhập.</response>
    /// <response code="403">Không có role Admin.</response>
    /// <response code="404">Không tìm thấy tài khoản.</response>
    [HttpPost("{id:guid}/unlock")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(AccountActionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(AccountActionResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Unlock(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new UnlockAccountCommand { Id = id }, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Admin hoặc Manager xem danh sách session của một tài khoản bất kỳ.
    /// </summary>
    /// <remarks>
    /// Endpoint này phục vụ kiểm tra thiết bị/phiên đăng nhập của user dưới góc nhìn quản trị.
    ///
    /// Quyền truy cập:
    /// - Role <c>Admin</c> hoặc <c>Manager</c>.
    ///
    /// Path parameter:
    /// - <c>id</c>: id account cần xem session.
    ///
    /// Query parameter:
    /// - <c>activeOnly</c>: mặc định true, chỉ lấy session đang active; false để lấy cả lịch sử session đã used/revoked/expired.
    ///
    /// Response gồm metadata session như device id, IP, user agent, thời điểm cấp/hết hạn,
    /// trạng thái refresh token và lý do revoke nếu có.
    /// </remarks>
    /// <param name="id">Id account cần xem session.</param>
    /// <param name="activeOnly">Chỉ lấy session đang hoạt động hay lấy toàn bộ lịch sử.</param>
    /// <param name="cancellationToken">Token hủy request khi client ngắt kết nối hoặc server dừng xử lý.</param>
    /// <returns>Danh sách session của account.</returns>
    /// <response code="200">Lấy danh sách session thành công.</response>
    /// <response code="401">Chưa đăng nhập.</response>
    /// <response code="403">Không có role Admin hoặc Manager.</response>
    [HttpGet("{id:guid}/sessions")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [Authorize(Roles = "Admin,Manager")]
    [ProducesResponseType(typeof(global::AuthService.Application.DTOs.Response.RefreshToken.SessionListResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSessions(Guid id, [FromQuery] bool activeOnly = true, CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(new global::AuthService.Application.CQRS.Query.Session.GetAccountSessionsQuery
        {
            AccountId = id,
            ActiveOnly = activeOnly
        }, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Admin buộc một tài khoản đăng xuất khỏi tất cả phiên active.
    /// </summary>
    /// <remarks>
    /// Endpoint này revoke toàn bộ refresh token active của account được chỉ định.
    ///
    /// Quyền truy cập:
    /// - Chỉ role <c>Admin</c>.
    ///
    /// Path parameter:
    /// - <c>id</c>: id account cần force logout.
    ///
    /// Body request:
    /// - <c>Reason</c>: lý do revoke, tùy chọn; dùng cho audit và hiển thị nội bộ.
    ///
    /// Cách hoạt động:
    /// - Controller gán AccountId từ route.
    /// - Handler tìm các refresh token/session active của account.
    /// - Đánh dấu các session đó là revoked.
    ///
    /// Lưu ý:
    /// - Access token đã cấp có thể vẫn còn hiệu lực đến khi hết TTL nếu hệ thống không có token blacklist.
    /// - Dùng endpoint này khi nghi ngờ tài khoản bị lộ, khi admin đổi trạng thái account hoặc sau thao tác bảo mật nhạy cảm.
    /// </remarks>
    /// <param name="id">Id account cần buộc đăng xuất.</param>
    /// <param name="command">Thông tin revoke, gồm lý do tùy chọn.</param>
    /// <param name="cancellationToken">Token hủy request khi client ngắt kết nối hoặc server dừng xử lý.</param>
    /// <returns>Kết quả revoke session, thường gồm số session đã thu hồi.</returns>
    /// <response code="200">Revoke tất cả session active thành công.</response>
    /// <response code="400">AccountId không hợp lệ.</response>
    /// <response code="401">Chưa đăng nhập.</response>
    /// <response code="403">Không có role Admin.</response>
    /// <response code="404">Không tìm thấy tài khoản.</response>
    [HttpPost("{id:guid}/sessions/revoke-all")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(global::AuthService.Application.DTOs.Response.RefreshToken.SessionActionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(global::AuthService.Application.DTOs.Response.RefreshToken.SessionActionResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RevokeAllSessions(Guid id, [FromBody] global::AuthService.Application.CQRS.Command.Session.AdminRevokeAccountSessionsCommand command, CancellationToken cancellationToken)
    {
        command.AccountId = id;
        var result = await _mediator.Send(command, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Admin xem login history của 1 account bất kỳ — dùng cho forensic khi điều tra account bị compromise, hiển thị IP + User-Agent + device + lý do fail.
    /// </summary>
    /// <remarks>
    /// Dùng để điều tra incident: ai đã login khi nào, từ IP nào, device nào, fail bao nhiêu lần.
    /// </remarks>
    /// <response code="200">Lấy login history thành công.</response>
    /// <response code="400">Filter không hợp lệ.</response>
    /// <response code="401">Chưa đăng nhập.</response>
    /// <response code="403">Không có role Admin hoặc Manager.</response>
    [HttpGet("{id:guid}/login-history")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [Authorize(Roles = "Admin,Manager")]
    [ProducesResponseType(typeof(LoginAttemptListResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetLoginHistory(
        Guid id,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] LoginAttemptResult? result = null,
        [FromQuery] bool? onlyFailed = null,
        [FromQuery] DateTime? fromUtc = null,
        [FromQuery] DateTime? toUtc = null,
        CancellationToken cancellationToken = default)
    {
        var query = new GetLoginHistoryQuery
        {
            AccountId = id,
            PageNumber = pageNumber,
            PageSize = pageSize,
            Result = result,
            OnlyFailed = onlyFailed,
            FromUtc = fromUtc,
            ToUtc = toUtc
        };

        var response = await _mediator.Send(query, cancellationToken);
        return StatusCode(response.StatusCode, response);
    }
}
