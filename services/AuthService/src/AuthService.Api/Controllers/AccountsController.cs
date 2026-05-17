using AuthService.Api.Extensions;
using AuthService.Application.CQRS.Command.Account;
using AuthService.Application.CQRS.Command.Auth;
using AuthService.Application.CQRS.Query.Account;
using AuthService.Application.CQRS.Query.Login;
using AuthService.Application.DTOs.Response.Account;
using AuthService.Application.DTOs.Response.Login;
using AuthService.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SharedContracts.Common.Responses;

namespace AuthService.Api.Controllers;

/// <summary>
/// Quản lý tài khoản của chính user đang đăng nhập (profile, password, phone verify, 2FA, link Google,
/// deactivate / delete chính mình).
/// Toàn bộ endpoint trong controller này yêu cầu access token hợp lệ.
/// </summary>
[ApiController]
[Route("api/accounts")]
[Produces("application/json")]
[Authorize]
public class AccountsController : ControllerBase
{
    private readonly IMediator _mediator;

    public AccountsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Lấy thông tin profile của tài khoản đang đăng nhập.
    /// </summary>
    /// <remarks>
    /// Endpoint này đọc AccountId từ claim trong access token, vì vậy client không cần truyền id tài khoản.
    ///
    /// Quyền truy cập:
    /// - Yêu cầu access token hợp lệ.
    /// - Chỉ trả về profile của chính user đang gọi API.
    ///
    /// Response thành công thường bao gồm thông tin cơ bản như id, email, họ tên, số điện thoại,
    /// avatar, ngày sinh, địa chỉ, trạng thái xác thực email/phone, trạng thái account và danh sách role.
    ///
    /// Use case:
    /// - Hiển thị trang "Tài khoản của tôi".
    /// - Đồng bộ lại thông tin user sau khi login hoặc sau khi cập nhật profile.
    /// </remarks>
    /// <param name="cancellationToken">Token hủy request khi client ngắt kết nối hoặc server dừng xử lý.</param>
    /// <returns><see cref="AccountResponse"/> chứa profile của user hiện tại.</returns>
    /// <response code="200">Lấy profile thành công.</response>
    /// <response code="401">Chưa đăng nhập hoặc access token không hợp lệ/hết hạn.</response>
    [HttpGet("me")]
    [ProducesResponseType(typeof(AccountResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(AccountResponse), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetMyProfile(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetMyProfileQuery(), cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Cập nhật profile của chính user theo id trên route.
    /// </summary>
    /// <remarks>
    /// Endpoint này cho phép user tự cập nhật thông tin cá nhân nhưng chỉ khi <c>id</c> trên route trùng với AccountId trong JWT.
    ///
    /// Path parameter:
    /// - <c>id</c>: id của account cần cập nhật. Phải là id của chính user đang đăng nhập.
    ///
    /// Body request:
    /// - <c>FullName</c>: bắt buộc, tối đa 150 ký tự.
    /// - <c>PhoneNumber</c>: tùy chọn, tối đa 20 ký tự.
    /// - <c>AvatarUrl</c>: tùy chọn, tối đa 500 ký tự.
    /// - <c>DateOfBirth</c>: tùy chọn, không được là ngày trong tương lai.
    /// - <c>Address</c>: tùy chọn, tối đa 500 ký tự.
    ///
    /// Cách hoạt động:
    /// - Controller kiểm tra route id có phải account hiện tại không.
    /// - Nếu khác user, trả HTTP 403 để chặn cập nhật tài khoản người khác.
    /// - Nếu hợp lệ, command được gán id từ route rồi gửi xuống application layer.
    ///
    /// Lưu ý:
    /// - Endpoint này không dùng để đổi email, mật khẩu, role hoặc trạng thái account.
    /// - Nếu đổi số điện thoại, trạng thái xác thực số điện thoại có thể cần xác minh lại tùy logic handler.
    /// </remarks>
    /// <param name="id">Id account cần cập nhật, phải trùng với user hiện tại.</param>
    /// <param name="command">Thông tin profile mới.</param>
    /// <param name="cancellationToken">Token hủy request khi client ngắt kết nối hoặc server dừng xử lý.</param>
    /// <returns>Kết quả cập nhật profile.</returns>
    /// <response code="200">Cập nhật profile thành công.</response>
    /// <response code="400">Dữ liệu đầu vào không hợp lệ.</response>
    /// <response code="403">Route id không phải tài khoản của user hiện tại.</response>
    /// <response code="404">Không tìm thấy tài khoản.</response>
    /// <response code="409">Dữ liệu cập nhật bị trùng với account khác, ví dụ phone theo rule nghiệp vụ.</response>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(AccountActionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(AccountActionResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(AccountActionResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(AccountActionResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(AccountActionResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateAccountCommand command, CancellationToken cancellationToken)
    {
        if (!IsSelf(id))
            return StatusCode(StatusCodes.Status403Forbidden, new AccountActionResponse
            {
                IsSuccess = false,
                StatusCode = 403,
                Message = "Không có quyền cập nhật tài khoản này."
            });

        command.Id = id;
        var result = await _mediator.Send(command, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Cập nhật profile của chính user hiện tại, không cần truyền id trên route.
    /// </summary>
    /// <remarks>
    /// Đây là phiên bản thuận tiện hơn của <c>PUT /api/accounts/{id}</c>.
    /// Controller lấy AccountId trực tiếp từ JWT claim rồi gán vào command.
    ///
    /// Body request giống endpoint cập nhật theo id:
    /// - <c>FullName</c>: bắt buộc, tối đa 150 ký tự.
    /// - <c>PhoneNumber</c>: tùy chọn, tối đa 20 ký tự.
    /// - <c>AvatarUrl</c>: tùy chọn, tối đa 500 ký tự.
    /// - <c>DateOfBirth</c>: tùy chọn, không được là ngày trong tương lai.
    /// - <c>Address</c>: tùy chọn, tối đa 500 ký tự.
    ///
    /// Use case:
    /// - Frontend cập nhật trang profile mà không cần lưu account id ở route.
    /// - Giảm rủi ro FE truyền nhầm id của user khác.
    /// </remarks>
    /// <param name="command">Thông tin profile mới.</param>
    /// <param name="cancellationToken">Token hủy request khi client ngắt kết nối hoặc server dừng xử lý.</param>
    /// <returns>Kết quả cập nhật profile.</returns>
    /// <response code="200">Cập nhật profile thành công.</response>
    /// <response code="400">Dữ liệu đầu vào không hợp lệ.</response>
    /// <response code="401">Chưa đăng nhập hoặc JWT không có AccountId hợp lệ.</response>
    /// <response code="404">Không tìm thấy tài khoản.</response>
    /// <response code="409">Dữ liệu cập nhật bị trùng với account khác.</response>
    [HttpPut("me")]
    [ProducesResponseType(typeof(AccountActionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(AccountActionResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(AccountActionResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(AccountActionResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(AccountActionResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateMe([FromBody] UpdateAccountCommand command, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId == null)
            return Unauthorized(new AccountActionResponse { IsSuccess = false, StatusCode = 401, Message = "Chưa đăng nhập." });

        command.Id = userId.Value;
        var result = await _mediator.Send(command, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Đổi mật khẩu của tài khoản đang đăng nhập.
    /// </summary>
    /// <remarks>
    /// Endpoint này dùng khi user biết mật khẩu hiện tại và muốn đổi sang mật khẩu mới.
    ///
    /// Body request:
    /// - <c>CurrentPassword</c>: mật khẩu hiện tại, bắt buộc.
    /// - <c>NewPassword</c>: mật khẩu mới, bắt buộc, 8-100 ký tự và phải có chữ hoa,
    ///   chữ thường, số, ký tự đặc biệt.
    /// - <c>ConfirmPassword</c>: phải khớp với <c>NewPassword</c>.
    ///
    /// Cách hoạt động:
    /// - AccountId được lấy từ JWT, client không được tự truyền account id.
    /// - Handler kiểm tra mật khẩu hiện tại.
    /// - Mật khẩu mới phải khác mật khẩu hiện tại.
    /// - Nếu đổi thành công, toàn bộ session/refresh token hiện có sẽ bị revoke để buộc đăng nhập lại.
    ///
    /// Lưu ý:
    /// - Đây không phải luồng quên mật khẩu. Nếu user quên mật khẩu, dùng nhóm endpoint <c>/api/auth/forgot-password</c>.
    /// </remarks>
    /// <param name="command">Mật khẩu hiện tại, mật khẩu mới và xác nhận mật khẩu.</param>
    /// <param name="cancellationToken">Token hủy request khi client ngắt kết nối hoặc server dừng xử lý.</param>
    /// <returns>Kết quả đổi mật khẩu.</returns>
    /// <response code="200">Đổi mật khẩu thành công.</response>
    /// <response code="400">Dữ liệu không hợp lệ hoặc mật khẩu hiện tại không đúng theo logic handler.</response>
    /// <response code="401">Chưa đăng nhập hoặc JWT không có AccountId hợp lệ.</response>
    [HttpPatch("me/password")]
    [ProducesResponseType(typeof(AccountActionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(AccountActionResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(AccountActionResponse), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ChangeMyPassword([FromBody] ChangePasswordCommand command, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId == null)
            return Unauthorized(Unauth());

        command.AccountId = userId.Value;
        var result = await _mediator.Send(command, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Gửi OTP xác thực số điện thoại hiện tại của tài khoản.
    /// </summary>
    /// <remarks>
    /// Endpoint này bắt đầu luồng xác minh số điện thoại cho user đang đăng nhập.
    ///
    /// Cách hoạt động:
    /// - AccountId được lấy từ JWT.
    /// - Handler kiểm tra account tồn tại và có số điện thoại để xác minh.
    /// - Sinh OTP 6 chữ số cho mục đích xác thực số điện thoại.
    /// - Gửi yêu cầu sang SmsService hoặc provider SMS thông qua hạ tầng hiện tại.
    ///
    /// Rate limit:
    /// - Có giới hạn tần suất gửi OTP để tránh spam SMS.
    /// - Nếu gọi quá nhanh, API trả HTTP 429.
    ///
    /// Sau khi nhận OTP, client gọi <c>POST /api/accounts/me/verify-phone-otp</c>.
    /// </remarks>
    /// <param name="cancellationToken">Token hủy request khi client ngắt kết nối hoặc server dừng xử lý.</param>
    /// <returns>Thông báo kết quả gửi OTP điện thoại.</returns>
    /// <response code="200">Gửi OTP thành công.</response>
    /// <response code="400">Account không có số điện thoại hoặc dữ liệu trạng thái không hợp lệ.</response>
    /// <response code="401">Chưa đăng nhập.</response>
    /// <response code="404">Không tìm thấy tài khoản.</response>
    /// <response code="429">Gửi OTP quá nhanh, cần chờ trước khi gửi lại.</response>
    [HttpPost("me/send-phone-otp")]
    [EnableRateLimiting(RateLimitingExtensions.PolicyAuthOtp)]
    [ProducesResponseType(typeof(CommonResponse<string>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(CommonResponse<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(CommonResponse<string>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(CommonResponse<string>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(CommonResponse<string>), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> SendPhoneOtp(CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId == null)
            return Unauthorized(UnauthString());

        var result = await _mediator.Send(new SendPhoneOtpCommand { AccountId = userId.Value }, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Xác thực OTP số điện thoại và đánh dấu số điện thoại đã xác minh.
    /// </summary>
    /// <remarks>
    /// Endpoint này hoàn tất luồng xác minh số điện thoại sau khi user nhận OTP qua SMS.
    ///
    /// Body request:
    /// - <c>Otp</c>: mã OTP gồm đúng 6 chữ số.
    ///
    /// Cách hoạt động:
    /// - AccountId được lấy từ JWT.
    /// - Handler kiểm tra OTP đúng mục đích xác minh phone, còn hạn và chưa dùng.
    /// - Nếu hợp lệ, cập nhật <c>PhoneConfirmed = true</c> cho account.
    ///
    /// Lưu ý:
    /// - OTP điện thoại không dùng cho đăng ký email hoặc reset password.
    /// - Nếu OTP sai/hết hạn, client cần yêu cầu gửi lại OTP.
    /// </remarks>
    /// <param name="command">OTP xác minh số điện thoại.</param>
    /// <param name="cancellationToken">Token hủy request khi client ngắt kết nối hoặc server dừng xử lý.</param>
    /// <returns>Thông báo kết quả xác minh số điện thoại.</returns>
    /// <response code="200">Xác minh số điện thoại thành công.</response>
    /// <response code="400">OTP không đúng định dạng, sai, hết hạn hoặc không hợp lệ.</response>
    /// <response code="401">Chưa đăng nhập.</response>
    [HttpPost("me/verify-phone-otp")]
    [ProducesResponseType(typeof(CommonResponse<string>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(CommonResponse<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(CommonResponse<string>), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> VerifyPhoneOtp([FromBody] VerifyPhoneOtpCommand command, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId == null)
            return Unauthorized(UnauthString());

        command.AccountId = userId.Value;
        var result = await _mediator.Send(command, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Bật 2FA cho tài khoản hiện tại và nhận secret để cấu hình ứng dụng authenticator.
    /// </summary>
    /// <remarks>
    /// Endpoint này tạo secret TOTP cho user hiện tại.
    ///
    /// Cách hoạt động:
    /// - AccountId được lấy từ JWT.
    /// - Handler sinh <c>TwoFactorSecret</c> dạng base32.
    /// - Response trả về <c>Secret</c> và <c>OtpAuthUri</c>.
    /// - Frontend có thể dùng <c>OtpAuthUri</c> để render QR code cho Google Authenticator, Microsoft Authenticator hoặc app tương thích TOTP.
    ///
    /// Lưu ý:
    /// - Secret cần được bảo vệ như thông tin nhạy cảm.
    /// - 2FA được bật ngay sau khi endpoint này thành công. Hiện chưa có bước confirm TOTP riêng.
    ///
    /// Idempotency:
    /// - Client NÊN gửi header <c>Idempotency-Key</c> (UUID v4) để chống bật 2FA trùng (sinh secret mới). Cache 24h.
    /// </remarks>
    /// <param name="cancellationToken">Token hủy request khi client ngắt kết nối hoặc server dừng xử lý.</param>
    /// <returns>Secret và otpauth URI để cấu hình 2FA.</returns>
    /// <response code="200">Tạo/bật 2FA thành công.</response>
    /// <response code="400">Không thể bật 2FA do trạng thái tài khoản không hợp lệ.</response>
    /// <response code="401">Chưa đăng nhập.</response>
    [HttpPost("me/2fa/enable")]
    [EnableRateLimiting(RateLimitingExtensions.PolicyAuthOtp)]
    [ProducesResponseType(typeof(CommonResponse<TwoFactorSecretDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(CommonResponse<TwoFactorSecretDto>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(CommonResponse<TwoFactorSecretDto>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Enable2FA(CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId == null)
            return Unauthorized(new CommonResponse<TwoFactorSecretDto> { IsSuccess = false, StatusCode = 401, Message = "Chưa đăng nhập." });

        var result = await _mediator.Send(new Enable2FACommand { AccountId = userId.Value }, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Tắt 2FA cho tài khoản hiện tại.
    /// </summary>
    /// <remarks>
    /// Endpoint này xóa cấu hình 2FA của user đang đăng nhập.
    ///
    /// Cách hoạt động:
    /// - AccountId được lấy từ JWT.
    /// - Handler xóa <c>TwoFactorSecret</c>.
    /// - Cập nhật <c>TwoFactorEnabled = false</c>.
    ///
    /// Lưu ý bảo mật:
    /// - Sau khi tắt 2FA, tài khoản chỉ còn phụ thuộc vào các cơ chế đăng nhập còn lại như mật khẩu hoặc Google.
    /// - Nếu muốn yêu cầu xác minh mật khẩu/OTP trước khi tắt 2FA, cần bổ sung ở command/handler.
    /// </remarks>
    /// <param name="cancellationToken">Token hủy request khi client ngắt kết nối hoặc server dừng xử lý.</param>
    /// <returns>Thông báo kết quả tắt 2FA.</returns>
    /// <response code="200">Tắt 2FA thành công.</response>
    /// <response code="401">Chưa đăng nhập.</response>
    [HttpPost("me/2fa/disable")]
    [ProducesResponseType(typeof(CommonResponse<string>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Disable2FA(CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId == null)
            return Unauthorized(UnauthString());

        var result = await _mediator.Send(new Disable2FACommand { AccountId = userId.Value }, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Liên kết tài khoản Google vào account hiện tại.
    /// </summary>
    /// <remarks>
    /// Endpoint này dùng khi user đã đăng nhập bằng tài khoản local và muốn thêm phương thức đăng nhập Google.
    ///
    /// Body request:
    /// - <c>IdToken</c>: Google ID token lấy từ Google Sign-In phía client.
    ///
    /// Cách hoạt động:
    /// - AccountId được lấy từ JWT.
    /// - Handler verify Google ID token.
    /// - Email trong Google token phải khớp với email account hiện tại theo policy hiện có.
    /// - Nếu hợp lệ, lưu Google subject/id vào account để lần sau có thể đăng nhập bằng Google.
    ///
    /// Lỗi thường gặp:
    /// - Google token rỗng hoặc không hợp lệ.
    /// - Email Google không trùng email account hiện tại.
    /// - Google account đã được liên kết với account khác.
    /// </remarks>
    /// <param name="command">Google ID token dùng để liên kết.</param>
    /// <param name="cancellationToken">Token hủy request khi client ngắt kết nối hoặc server dừng xử lý.</param>
    /// <returns>Kết quả liên kết Google.</returns>
    /// <response code="200">Liên kết Google thành công.</response>
    /// <response code="400">Dữ liệu đầu vào hoặc Google token không hợp lệ.</response>
    /// <response code="401">Chưa đăng nhập.</response>
    /// <response code="404">Không tìm thấy tài khoản hiện tại.</response>
    /// <response code="409">Google account/email đã liên kết hoặc xung đột với dữ liệu hiện có.</response>
    [HttpPost("me/link-google")]
    [ProducesResponseType(typeof(AccountActionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(AccountActionResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(AccountActionResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(AccountActionResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(AccountActionResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> LinkGoogle([FromBody] LinkGoogleCommand command, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId == null)
            return Unauthorized(Unauth());

        command.AccountId = userId.Value;
        var result = await _mediator.Send(command, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Bỏ liên kết Google khỏi account hiện tại.
    /// </summary>
    /// <remarks>
    /// Endpoint này xóa liên kết Google để user không còn đăng nhập bằng Google account đó.
    ///
    /// Cách hoạt động:
    /// - AccountId được lấy từ JWT.
    /// - Handler kiểm tra account có thể bỏ Google link hay không.
    /// - Nếu account không có mật khẩu local, việc unlink bị từ chối để tránh làm user mất mọi phương thức đăng nhập.
    ///
    /// Lưu ý:
    /// - Nếu user đăng ký ban đầu bằng Google-only, cần tạo mật khẩu local trước khi unlink.
    /// - Endpoint này không revoke session hiện tại trừ khi handler có logic riêng.
    /// </remarks>
    /// <param name="cancellationToken">Token hủy request khi client ngắt kết nối hoặc server dừng xử lý.</param>
    /// <returns>Kết quả bỏ liên kết Google.</returns>
    /// <response code="200">Bỏ liên kết Google thành công.</response>
    /// <response code="400">Không thể bỏ liên kết do account không có phương thức đăng nhập thay thế.</response>
    /// <response code="401">Chưa đăng nhập.</response>
    [HttpPost("me/unlink-google")]
    [ProducesResponseType(typeof(AccountActionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(AccountActionResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(AccountActionResponse), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> UnlinkGoogle(CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId == null)
            return Unauthorized(Unauth());

        var result = await _mediator.Send(new UnlinkGoogleCommand { AccountId = userId.Value }, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Tự vô hiệu hóa tài khoản hiện tại.
    /// </summary>
    /// <remarks>
    /// Endpoint này cho phép user chủ động chuyển account của mình sang trạng thái inactive.
    ///
    /// Cách hoạt động:
    /// - AccountId được lấy từ JWT.
    /// - Handler chuyển trạng thái account sang <c>Inactive</c>.
    /// - Toàn bộ refresh token/session của account bị revoke để đăng xuất khỏi các thiết bị.
    ///
    /// Lưu ý:
    /// - Đây không phải xóa mềm account; dữ liệu account vẫn tồn tại.
    /// - Sau khi deactivate, user có thể không đăng nhập được cho đến khi admin mở lại hoặc có luồng kích hoạt lại.
    /// </remarks>
    /// <param name="cancellationToken">Token hủy request khi client ngắt kết nối hoặc server dừng xử lý.</param>
    /// <returns>Kết quả vô hiệu hóa tài khoản.</returns>
    /// <response code="200">Vô hiệu hóa tài khoản thành công.</response>
    /// <response code="401">Chưa đăng nhập.</response>
    [HttpPost("me/deactivate")]
    [ProducesResponseType(typeof(AccountActionResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> DeactivateMe(CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId == null)
            return Unauthorized(Unauth());

        var result = await _mediator.Send(new DeactivateMeCommand { AccountId = userId.Value }, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Tự xóa mềm tài khoản hiện tại.
    /// </summary>
    /// <remarks>
    /// Endpoint này cho phép user yêu cầu xóa account của chính mình theo cơ chế soft delete.
    ///
    /// Cách hoạt động:
    /// - AccountId được lấy từ JWT.
    /// - Handler đánh dấu account là đã xóa thay vì xóa vật lý khỏi database.
    /// - Toàn bộ refresh token/session bị revoke.
    ///
    /// Lưu ý nghiệp vụ:
    /// - Soft delete giúp giữ lại dữ liệu cần cho audit hoặc ràng buộc hệ thống.
    /// - Các dữ liệu liên quan ở service khác có thể cần xử lý riêng nếu có yêu cầu xóa dữ liệu toàn hệ thống.
    /// - Sau khi xóa mềm, user thường không thể đăng nhập lại bằng account này.
    /// </remarks>
    /// <param name="cancellationToken">Token hủy request khi client ngắt kết nối hoặc server dừng xử lý.</param>
    /// <returns>Kết quả xóa mềm tài khoản.</returns>
    /// <response code="200">Xóa mềm tài khoản thành công.</response>
    /// <response code="401">Chưa đăng nhập.</response>
    [HttpDelete("me")]
    [ProducesResponseType(typeof(AccountActionResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> DeleteMe(CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId == null)
            return Unauthorized(Unauth());

        var result = await _mediator.Send(new DeleteMeCommand { AccountId = userId.Value }, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Xem lịch sử login của tài khoản đang đăng nhập.
    /// </summary>
    /// <remarks>
    /// Trả về các login attempt (thành công + thất bại) của chính user, sort theo thời gian giảm dần.
    /// Mỗi entry kèm IP, User-Agent, DeviceId, lý do nếu fail.
    ///
    /// Use case:
    /// - Trang "Hoạt động đăng nhập" giúp user phát hiện thiết bị/IP lạ.
    /// - Filter <c>onlyFailed=true</c> để xem các attempt bị chặn.
    /// </remarks>
    /// <response code="200">Lấy login history thành công.</response>
    /// <response code="400">Filter không hợp lệ (FromUtc >= ToUtc).</response>
    /// <response code="401">Chưa đăng nhập.</response>
    [HttpGet("me/login-history")]
    [ProducesResponseType(typeof(LoginAttemptListResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(LoginAttemptListResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(LoginAttemptListResponse), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetMyLoginHistory(
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] LoginAttemptResult? result = null,
        [FromQuery] bool? onlyFailed = null,
        [FromQuery] DateTime? fromUtc = null,
        [FromQuery] DateTime? toUtc = null,
        CancellationToken cancellationToken = default)
    {
        var userId = GetCurrentUserId();
        if (userId == null)
            return Unauthorized(new LoginAttemptListResponse { IsSuccess = false, StatusCode = 401, Message = "Chưa đăng nhập." });

        var query = new GetLoginHistoryQuery
        {
            AccountId = userId.Value,
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

    private Guid? GetCurrentUserId()
    {
        var raw = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(raw, out var id) ? id : null;
    }

    private bool IsSelf(Guid targetId)
    {
        var current = GetCurrentUserId();
        return current.HasValue && current.Value == targetId;
    }

    private static AccountActionResponse Unauth() => new()
    {
        IsSuccess = false,
        StatusCode = 401,
        Message = "Chưa đăng nhập."
    };

    private static CommonResponse<string> UnauthString() => new()
    {
        IsSuccess = false,
        StatusCode = 401,
        Message = "Chưa đăng nhập."
    };
}
