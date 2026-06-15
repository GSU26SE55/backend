using AuthService.Api.Extensions;
using AuthService.Application.CQRS.Command.Account;
using AuthService.Application.CQRS.Command.Auth;
using AuthService.Application.CQRS.Query.Account;
using AuthService.Application.CQRS.Query.Login;
using AuthService.Application.DTOs.Response.Account;
using AuthService.Application.DTOs.Response.Auth;
using AuthService.Application.DTOs.Response.Login;
using AuthService.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SharedContracts.Common.Responses;

namespace AuthService.Api.Controllers;

/// <summary>
/// Quản lý tài khoản của chính user đang đăng nhập (password, phone verify, 2FA, link Google,
/// deactivate / delete chính mình).
/// Profile read/update dùng <c>GET /api/auth/me</c> và <c>PUT /api/auth/me/profile</c>.
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
    /// Đổi mật khẩu của tài khoản đang đăng nhập — yêu cầu xác minh password cũ trước khi áp dụng password mới (BCrypt rehash). Logout mọi session khác sau khi đổi thành công.
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
    /// <response code="200">Đổi mật khẩu thành công. Toàn bộ session/refresh token đã bị revoke, client cần đăng nhập lại.</response>
    /// <response code="400">Validation lỗi (NewPassword không đạt độ phức tạp, ConfirmPassword không khớp) HOẶC mật khẩu hiện tại không đúng. Đây là input error của user đã authenticated, KHÔNG phải auth fail.</response>
    /// <response code="401">Chưa đăng nhập hoặc JWT không có AccountId hợp lệ (middleware-level).</response>
    /// <response code="404">Không tìm thấy account (AccountId từ JWT không match record nào).</response>
    [HttpPatch("me/password")]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(AccountActionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(AccountActionResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(AccountActionResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(AccountActionResponse), StatusCodes.Status404NotFound)]
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
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
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
    /// <response code="200">Xác minh số điện thoại thành công. <c>PhoneConfirmed</c> được set <c>true</c>.</response>
    /// <response code="400">OTP sai định dạng (validation: phải đủ 6 chữ số).</response>
    /// <response code="401">Chưa đăng nhập HOẶC OTP không chính xác (sai giá trị OTP user nhập).</response>
    /// <response code="404">Không tìm thấy tài khoản.</response>
    /// <response code="409">Số điện thoại đã được xác thực trước đó (state conflict).</response>
    /// <response code="422">OTP hết hạn, OTP không phải dành cho mục đích xác minh phone, hoặc account chưa có OTP nào được gửi (business rule violation).</response>
    /// <response code="423">Tài khoản bị khóa tạm thời do sai OTP nhiều lần.</response>
    [HttpPost("me/verify-phone-otp")]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(CommonResponse<string>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(CommonResponse<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(CommonResponse<string>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(CommonResponse<string>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(CommonResponse<string>), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(CommonResponse<string>), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(CommonResponse<string>), StatusCodes.Status423Locked)]
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
    /// [DEPRECATED — GH-295] Endpoint cũ kích hoạt 2FA 1 bước. Trả 410 Gone.
    /// Dùng flow mới: <c>POST /api/accounts/me/2fa/init</c> rồi <c>POST /api/accounts/me/2fa/confirm</c>.
    /// </summary>
    [HttpPost("me/2fa/enable")]
    [ProducesResponseType(typeof(CommonResponse<TwoFactorSecretDto>), StatusCodes.Status410Gone)]
    [Obsolete("Use POST /api/accounts/me/2fa/init + /confirm (GH-295)")]
    public async Task<IActionResult> Enable2FA(CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId == null)
            return Unauthorized(new CommonResponse<TwoFactorSecretDto> { IsSuccess = false, StatusCode = 401, Message = "Chưa đăng nhập." });

        var result = await _mediator.Send(new Enable2FACommand { AccountId = userId.Value }, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Bước 1/2 của enable 2FA — sinh secret + QR; CHƯA activate. Phải gọi tiếp <c>/2fa/confirm</c>.
    /// </summary>
    /// <remarks>
    /// Cách hoạt động:
    /// - AccountId được lấy từ JWT.
    /// - Handler sinh secret base32 (20 bytes) + cache pending state vào Redis TTL 10 phút.
    /// - Response trả <c>secret</c>, <c>otpAuthUri</c> (cho QR), <c>pendingToken</c> (cần gửi lại khi confirm).
    /// - 2FA CHƯA bật. <c>TwoFactorEnabled</c> chỉ flip true sau khi <c>/2fa/confirm</c> verify TOTP thành công.
    /// </remarks>
    [HttpPost("me/2fa/init")]
    [EnableRateLimiting(RateLimitingExtensions.PolicyAuthOtp)]
    [ProducesResponseType(typeof(CommonResponse<TwoFactorSetupDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(CommonResponse<TwoFactorSetupDto>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(CommonResponse<TwoFactorSetupDto>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(CommonResponse<TwoFactorSetupDto>), StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Init2FA(CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId == null)
            return Unauthorized(new CommonResponse<TwoFactorSetupDto> { IsSuccess = false, StatusCode = 401, Message = "Chưa đăng nhập." });

        var result = await _mediator.Send(new Init2FACommand { AccountId = userId.Value }, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Bước 2/2 của enable 2FA — verify mã TOTP để chứng minh user đã quét QR.
    /// Activate 2FA + encrypt secret + sinh 8 backup codes (trả về plain CHỈ 1 LẦN).
    /// </summary>
    /// <remarks>
    /// Body: <c>{ pendingToken, code }</c>. <c>code</c> là 6 số từ Authenticator app.
    /// </remarks>
    [HttpPost("me/2fa/confirm")]
    [EnableRateLimiting(RateLimitingExtensions.PolicyAuthOtp)]
    [ProducesResponseType(typeof(CommonResponse<TwoFactorConfirmDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(CommonResponse<TwoFactorConfirmDto>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(CommonResponse<TwoFactorConfirmDto>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(CommonResponse<TwoFactorConfirmDto>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(CommonResponse<TwoFactorConfirmDto>), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(CommonResponse<TwoFactorConfirmDto>), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Confirm2FA([FromBody] Confirm2FACommand command, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId == null)
            return Unauthorized(new CommonResponse<TwoFactorConfirmDto> { IsSuccess = false, StatusCode = 401, Message = "Chưa đăng nhập." });

        command.AccountId = userId.Value;
        var result = await _mediator.Send(command, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Tắt two-factor authentication (TOTP) cho tài khoản hiện tại — yêu cầu mã OTP cuối cùng để xác minh quyền sở hữu device, tránh attacker đã chiếm session bypass 2FA.
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
    /// <summary>
    /// Sinh lại 8 backup codes — vô hiệu hóa codes cũ. Yêu cầu TOTP để chứng minh còn giữ device.
    /// </summary>
    [HttpPost("me/2fa/backup-codes/regenerate")]
    [EnableRateLimiting(RateLimitingExtensions.PolicyBackupCodeRegenerate)]
    [ProducesResponseType(typeof(CommonResponse<BackupCodesDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(CommonResponse<BackupCodesDto>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(CommonResponse<BackupCodesDto>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(CommonResponse<BackupCodesDto>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(CommonResponse<BackupCodesDto>), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(CommonResponse<BackupCodesDto>), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> RegenerateBackupCodes([FromBody] RegenerateBackupCodesCommand command, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId == null)
            return Unauthorized(new CommonResponse<BackupCodesDto> { IsSuccess = false, StatusCode = 401, Message = "Chưa đăng nhập." });

        command.AccountId = userId.Value;
        var result = await _mediator.Send(command, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost("me/2fa/disable")]
    [EnableRateLimiting(RateLimitingExtensions.PolicyTwoFactorDisable)]
    [ProducesResponseType(typeof(CommonResponse<string>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(CommonResponse<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(CommonResponse<string>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(CommonResponse<string>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(CommonResponse<string>), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Disable2FA([FromBody] Disable2FACommand command, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId == null)
            return Unauthorized(UnauthString());

        command.AccountId = userId.Value;
        var result = await _mediator.Send(command, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Liên kết tài khoản Google OAuth vào account hiện tại — bind subject ID từ Google ID token vào ProviderLinks; sau đó user có thể login bằng Google nút SSO.
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
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
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
    /// Bỏ liên kết Google OAuth khỏi account hiện tại — xóa entry ProviderLinks; KHÔNG cho phép nếu Google là phương thức đăng nhập duy nhất (chặn lock-out).
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
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
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
    /// Tự vô hiệu hóa tài khoản — chuyển AccountStatus sang Deactivated (reversible bởi Admin). Logout mọi session ngay. Khác Delete: không soft-delete data.
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
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
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
    /// Tự xóa mềm tài khoản (soft delete) — set IsDeleted=true + revoke mọi refresh token. Data giữ 90 ngày cho GDPR audit, sau đó hard delete via cleanup job.
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
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
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
    /// Lấy profile chi tiết của tài khoản đang đăng nhập (self).
    /// </summary>
    /// <remarks>
    /// Trả về full profile gồm: AccountId, Email, FullName, PhoneNumber, AvatarUrl, Role,
    /// AccountStatus, Is2FAEnabled, ProviderLinks (Google), CreatedAt, LastLoginAt.
    /// Endpoint này KHÔNG nhận tham số — userId resolve từ JWT claim <c>nameid</c>.
    /// </remarks>
    /// <param name="cancellationToken">Token hủy request.</param>
    /// <response code="200">Lấy profile thành công.</response>
    /// <response code="401">Chưa đăng nhập hoặc token hết hạn.</response>
    [HttpGet("me/profile")]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(AccountResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(AccountResponse), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetMyProfile(CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId == null)
            return Unauthorized(new AccountResponse { IsSuccess = false, StatusCode = 401, Message = "Chưa đăng nhập." });

        var result = await _mediator.Send(new GetMyProfileQuery(), cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Cập nhật profile theo accountId (chỉ owner — userId trong JWT phải khớp <paramref name="id"/>).
    /// </summary>
    /// <remarks>
    /// Endpoint kế thừa pattern <c>PUT /accounts/{id}</c> cho FE Web Admin cần edit tài khoản trực tiếp.
    /// Ràng buộc: <c>currentUserId == id</c>, nếu không trả 403 — KHÔNG cho phép user A update profile user B
    /// qua endpoint này. Admin override dùng <c>PUT /admin/accounts/{id}</c>.
    ///
    /// Body fields tối thiểu: <c>FullName</c>, <c>PhoneNumber</c>, <c>AvatarUrl</c>. Email/Role/Status
    /// không update qua đây — dùng endpoint Admin riêng.
    /// </remarks>
    /// <param name="id">Account ID — phải khớp current user JWT.</param>
    /// <param name="command">Field cần update.</param>
    /// <param name="cancellationToken">Token hủy request.</param>
    /// <response code="200">Update thành công.</response>
    /// <response code="400">Field validation lỗi (xem ListErrors).</response>
    /// <response code="401">Chưa đăng nhập.</response>
    /// <response code="403">User cố update account khác (id mismatch).</response>
    /// <response code="404">Account không tồn tại.</response>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(AccountActionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(AccountActionResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(AccountActionResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(AccountActionResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(AccountActionResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateAccountCommand command, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId == null || userId.Value != id)
            return StatusCode(403, new AccountActionResponse { IsSuccess = false, StatusCode = 403, Message = "Không có quyền." });

        command.Id = id;
        var result = await _mediator.Send(command, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Cập nhật profile của user hiện tại — alias của <c>PUT /accounts/{id}</c> nhưng không cần truyền id.
    /// </summary>
    /// <remarks>
    /// Endpoint tiện hơn cho Mobile/Web khi không muốn track accountId. Backend resolve userId từ JWT
    /// claim <c>nameid</c> rồi set vào <c>command.Id</c> trước khi gửi MediatR.
    ///
    /// Body fields giống <c>PUT /accounts/{id}</c>: <c>FullName</c>, <c>PhoneNumber</c>, <c>AvatarUrl</c>.
    /// Validation field-level qua <c>UpdateAccountCommand.ValidateAsync</c>.
    /// </remarks>
    /// <param name="command">Field cần update — Id sẽ bị overwrite bằng userId từ JWT.</param>
    /// <param name="cancellationToken">Token hủy request.</param>
    /// <response code="200">Update thành công.</response>
    /// <response code="400">Field validation lỗi.</response>
    /// <response code="401">Chưa đăng nhập.</response>
    /// <response code="404">Account không tồn tại (rất hiếm — JWT valid nhưng account bị xoá).</response>
    [HttpPut("me/profile")]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(AccountActionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(AccountActionResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(AccountActionResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(AccountActionResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateMe([FromBody] UpdateAccountCommand command, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId == null)
            return Unauthorized(Unauth());

        command.Id = userId.Value;
        var result = await _mediator.Send(command, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Xem lịch sử login (thành công + thất bại) của tài khoản đang đăng nhập — sort theo CreatedAt DESC, mỗi entry kèm IP/User-Agent/DeviceId/reason. FE render trang 'Hoạt động đăng nhập'.
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
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
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
