using System.Security.Cryptography;
using AuthService.Api.Extensions;
using AuthService.Application.CQRS.Command.Auth;
using AuthService.Application.DTOs.Response.Auth;
using AuthService.Application.Interfaces.Helpers;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using SharedContracts.Common.Responses;

namespace AuthService.Api.Controllers;

/// <summary>
/// Nhóm endpoint xác thực: đăng nhập, đăng ký, verify OTP, refresh token, đăng xuất, quên mật khẩu, Google OAuth.
/// Toàn bộ endpoint trong controller này KHÔNG yêu cầu access token (anonymous).
/// </summary>
[ApiController]
[Route("api/auth")]
[Produces("application/json")]
public class AuthController : ControllerBase
{
    private const string OAuthStateCookie = "g_oauth_state";

    private readonly IMediator _mediator;
    private readonly IGoogleOAuthHelper _googleOAuthHelper;
    private readonly IConfiguration _configuration;

    public AuthController(IMediator mediator, IGoogleOAuthHelper googleOAuthHelper, IConfiguration configuration)
    {
        _mediator = mediator;
        _googleOAuthHelper = googleOAuthHelper;
        _configuration = configuration;
    }

    /// <summary>
    /// Đăng nhập bằng email và mật khẩu để nhận cặp access token và refresh token.
    /// </summary>
    /// <remarks>
    /// Endpoint này dùng cho luồng đăng nhập local, tức là tài khoản có mật khẩu trong hệ thống.
    ///
    /// Body request:
    /// - <c>Email</c>: email đăng nhập, bắt buộc, tối đa 256 ký tự và phải đúng định dạng email.
    /// - <c>Password</c>: mật khẩu, bắt buộc, từ 6 đến 100 ký tự và không chứa khoảng trắng.
    ///
    /// Cách hoạt động:
    /// - Hệ thống validate email/mật khẩu đầu vào.
    /// - Tìm tài khoản theo email và kiểm tra trạng thái tài khoản.
    /// - So khớp mật khẩu với password hash đang lưu.
    /// - Nếu hợp lệ, tạo access token ngắn hạn và refresh token dài hạn.
    /// - Refresh token được lưu như một session đăng nhập, kèm thông tin thiết bị/IP/User-Agent nếu handler thu thập được.
    ///
    /// Lưu ý bảo mật:
    /// - Access token dùng để gọi các API cần đăng nhập.
    /// - Refresh token chỉ dùng để xin token mới hoặc đăng xuất, không nên lưu ở nơi dễ bị JavaScript đọc nếu FE chạy trên browser.
    /// - Nếu tài khoản bị khóa, bị vô hiệu hóa hoặc chưa đủ điều kiện đăng nhập, API trả mã lỗi tương ứng.
    /// </remarks>
    /// <param name="command">Thông tin email và mật khẩu đăng nhập.</param>
    /// <param name="cancellationToken">Token hủy request khi client ngắt kết nối hoặc server dừng xử lý.</param>
    /// <returns><see cref="LoginResponse"/> chứa access token, refresh token và thông tin đăng nhập nếu thành công.</returns>
    /// <response code="200">Đăng nhập thành công, trả về cặp token.</response>
    /// <response code="400">Dữ liệu đầu vào không hợp lệ.</response>
    /// <response code="401">Email hoặc mật khẩu không đúng.</response>
    /// <response code="403">Tài khoản không được phép đăng nhập, ví dụ bị vô hiệu hóa hoặc chưa xác minh theo policy.</response>
    /// <response code="423">Tài khoản đang bị khóa do quá nhiều lần đăng nhập/OTP sai hoặc do trạng thái lock.</response>
    [HttpPost("login")]
    [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status423Locked)]
    public async Task<IActionResult> Login([FromBody] LoginCommand command, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(command, cancellationToken);
        return StatusCode(result.StatusCode == 0 ? StatusCodes.Status200OK : result.StatusCode, result);
    }

    /// <summary>
    /// Đăng ký tài khoản mới cho người dùng tự tạo tài khoản.
    /// </summary>
    /// <remarks>
    /// Endpoint này tạo account ở trạng thái chờ xác thực email và gửi OTP đăng ký qua email.
    ///
    /// Body request:
    /// - <c>Email</c>: bắt buộc, tối đa 256 ký tự, đúng định dạng email.
    /// - <c>Password</c>: bắt buộc, tối đa 100 ký tự, tối thiểu 8 ký tự và phải có chữ hoa, chữ thường, số, ký tự đặc biệt.
    /// - <c>FullName</c>: bắt buộc, tối đa 150 ký tự.
    /// - <c>PhoneNumber</c>: tùy chọn, tối đa 20 ký tự.
    /// - <c>DateOfBirth</c>: tùy chọn, không được là ngày trong tương lai và năm sinh không được trước 1900.
    /// - <c>Address</c>: tùy chọn, tối đa 500 ký tự.
    ///
    /// Cách hoạt động:
    /// - Validate dữ liệu đăng ký.
    /// - Kiểm tra trùng email/số điện thoại theo logic nghiệp vụ hiện tại.
    /// - Tạo account ở trạng thái <c>PendingVerification</c>.
    /// - Sinh OTP 6 chữ số cho mục đích đăng ký.
    /// - Gửi event qua message bus để EmailService gửi email OTP cho người dùng.
    ///
    /// Idempotent re-register:
    /// - Nếu email đã tồn tại nhưng account vẫn đang <c>PendingVerification</c>, hệ thống có thể tái sử dụng record đó và ghi đè OTP/profile mới.
    /// - Nếu email đã active hoặc ở trạng thái không cho đăng ký lại, API trả HTTP 409.
    ///
    /// Sau khi đăng ký thành công, client cần gọi <c>POST /api/auth/verify-otp</c> để kích hoạt tài khoản.
    ///
    /// Idempotency:
    /// - Client NÊN gửi header <c>Idempotency-Key</c> (UUID v4) để chống tạo trùng account khi double-click hoặc retry mạng.
    /// - Server cache response 24h theo key này; request lặp với cùng key trả lại response cũ thay vì xử lý lại.
    /// </remarks>
    /// <param name="command">Thông tin đăng ký tài khoản.</param>
    /// <param name="cancellationToken">Token hủy request khi client ngắt kết nối hoặc server dừng xử lý.</param>
    /// <returns><see cref="RegisterResponse"/> mô tả kết quả đăng ký và trạng thái gửi OTP.</returns>
    /// <response code="201">Đăng ký thành công, OTP đã gửi.</response>
    /// <response code="400">Dữ liệu không hợp lệ.</response>
    /// <response code="409">Email/Phone đã được sử dụng / Idempotency-Key đang xử lý parallel.</response>
    [HttpPost("register")]
    [EnableRateLimiting(RateLimitingExtensions.PolicyAnonOtp)]
    [ProducesResponseType(typeof(RegisterResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(RegisterResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(RegisterResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Register([FromBody] RegisterCommand command, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(command, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Xác thực OTP đăng ký để kích hoạt tài khoản và đăng nhập lần đầu.
    /// </summary>
    /// <remarks>
    /// Endpoint này được gọi sau khi người dùng đăng ký và nhận OTP qua email.
    ///
    /// Body request:
    /// - <c>Email</c>: email đã đăng ký.
    /// - <c>Otp</c>: mã OTP gồm đúng 6 chữ số.
    ///
    /// Cách hoạt động:
    /// - Kiểm tra email và OTP đúng định dạng.
    /// - Tìm account đang chờ xác thực.
    /// - Kiểm tra OTP còn hạn, đúng mục đích đăng ký và chưa dùng.
    /// - Nếu OTP đúng, kích hoạt account, xác nhận email và gán role mặc định <c>Customer</c>.
    /// - Sau khi kích hoạt, hệ thống cấp access token và refresh token để người dùng đăng nhập ngay.
    ///
    /// Lưu ý:
    /// - Nếu nhập sai OTP nhiều lần, tài khoản có thể bị khóa tạm thời.
    /// - OTP đăng ký không dùng cho reset password hoặc xác minh số điện thoại.
    /// </remarks>
    /// <param name="command">Email và OTP đăng ký.</param>
    /// <param name="cancellationToken">Token hủy request khi client ngắt kết nối hoặc server dừng xử lý.</param>
    /// <returns><see cref="LoginResponse"/> chứa cặp token nếu xác thực thành công.</returns>
    /// <response code="200">Verify thành công, trả AccessToken + RefreshToken.</response>
    /// <response code="400">OTP hết hạn / không phải dành cho register / đã verify.</response>
    /// <response code="401">OTP sai (kèm số lần thử còn lại).</response>
    /// <response code="404">Tài khoản không tồn tại.</response>
    /// <response code="423">Bị khóa do sai OTP nhiều lần.</response>
    [HttpPost("verify-otp")]
    [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status423Locked)]
    public async Task<IActionResult> VerifyOtp([FromBody] VerifyOtpCommand command, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(command, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Gửi lại OTP đăng ký cho tài khoản đang chờ xác thực.
    /// </summary>
    /// <remarks>
    /// Endpoint này dùng khi người dùng chưa nhận được OTP hoặc OTP cũ đã hết hạn.
    ///
    /// Body request:
    /// - <c>Email</c>: email của account đang ở trạng thái <c>PendingVerification</c>.
    ///
    /// Cách hoạt động:
    /// - Kiểm tra email hợp lệ.
    /// - Chỉ cho gửi lại OTP nếu account tồn tại và vẫn đang chờ xác thực.
    /// - Sinh OTP mới, cập nhật thời gian hết hạn và gửi lại qua EmailService.
    ///
    /// Rate limit:
    /// - Có giới hạn tối thiểu 60 giây giữa các lần gửi lại.
    /// - Nếu gọi quá nhanh, API trả HTTP 429 để client hiển thị thông báo chờ.
    ///
    /// Lưu ý:
    /// - Endpoint này không dùng cho quên mật khẩu; reset password có endpoint resend riêng.
    ///
    /// Idempotency:
    /// - Client NÊN gửi header <c>Idempotency-Key</c> (UUID v4) để chống resend trùng. Cache 24h.
    /// </remarks>
    /// <param name="command">Email cần gửi lại OTP đăng ký.</param>
    /// <param name="cancellationToken">Token hủy request khi client ngắt kết nối hoặc server dừng xử lý.</param>
    /// <returns><see cref="CommonResponse{T}"/> thông báo kết quả gửi lại OTP.</returns>
    /// <response code="200">Đã gửi lại OTP.</response>
    /// <response code="400">Tài khoản đã verify hoặc không ở trạng thái PendingVerification.</response>
    /// <response code="404">Tài khoản không tồn tại.</response>
    /// <response code="429">Resend quá nhanh, cần đợi.</response>
    [HttpPost("resend-otp")]
    [EnableRateLimiting(RateLimitingExtensions.PolicyAnonOtp)]
    [ProducesResponseType(typeof(CommonResponse<string>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(CommonResponse<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(CommonResponse<string>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(CommonResponse<string>), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> ResendOtp([FromBody] ResendOtpCommand command, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(command, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Làm mới access token bằng refresh token.
    /// </summary>
    /// <remarks>
    /// Endpoint này dùng khi access token hết hạn nhưng refresh token vẫn còn hợp lệ.
    ///
    /// Body request:
    /// - <c>RefreshToken</c>: refresh token đã nhận từ login, verify OTP, Google login hoặc lần refresh trước đó.
    ///
    /// Cách hoạt động:
    /// - Kiểm tra refresh token có tồn tại, còn hạn và đang ở trạng thái active.
    /// - Áp dụng refresh token rotation: token cũ bị đánh dấu đã dùng, hệ thống cấp một access token và refresh token mới.
    /// - Refresh token mới trở thành session tiếp theo của cùng thiết bị/luồng đăng nhập.
    ///
    /// Bảo vệ reuse attack:
    /// - Nếu refresh token đã dùng bị gửi lại, hệ thống xem đây là dấu hiệu token bị lộ.
    /// - Khi phát hiện reuse, hệ thống có thể revoke toàn bộ refresh token của tài khoản để buộc đăng nhập lại.
    ///
    /// Client cần thay refresh token cũ bằng refresh token mới sau mỗi lần gọi thành công.
    /// </remarks>
    /// <param name="command">Refresh token hiện tại.</param>
    /// <param name="cancellationToken">Token hủy request khi client ngắt kết nối hoặc server dừng xử lý.</param>
    /// <returns><see cref="LoginResponse"/> chứa cặp token mới.</returns>
    /// <response code="200">Cấp lại pair token thành công.</response>
    /// <response code="401">Token không hợp lệ / hết hạn / phát hiện reuse.</response>
    [HttpPost("refresh-token")]
    [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> RefreshToken([FromBody] RefreshTokenCommand command, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(command, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Đăng xuất một phiên đăng nhập bằng cách thu hồi refresh token.
    /// </summary>
    /// <remarks>
    /// Endpoint này dùng cho logout thông thường trên một thiết bị hoặc một browser.
    ///
    /// Body request:
    /// - <c>RefreshToken</c>: refresh token của phiên cần đăng xuất.
    ///
    /// Cách hoạt động:
    /// - Validate refresh token không rỗng.
    /// - Tìm session tương ứng với refresh token.
    /// - Đánh dấu refresh token là revoked để không thể dùng refresh token đó xin access token mới.
    ///
    /// Lưu ý:
    /// - Access token đã cấp trước đó có thể vẫn còn hiệu lực cho đến khi hết hạn, vì JWT thường là stateless.
    /// - Nếu muốn đăng xuất tất cả thiết bị, dùng <c>POST /api/sessions/revoke-all</c>.
    /// </remarks>
    /// <param name="command">Refresh token cần thu hồi.</param>
    /// <param name="cancellationToken">Token hủy request khi client ngắt kết nối hoặc server dừng xử lý.</param>
    /// <returns>Thông báo kết quả đăng xuất.</returns>
    /// <response code="200">Đăng xuất thành công hoặc refresh token đã được xử lý theo nghiệp vụ.</response>
    /// <response code="400">Thiếu refresh token.</response>
    [HttpPost("logout")]
    [ProducesResponseType(typeof(CommonResponse<string>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Logout([FromBody] LogoutCommand command, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(command, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Yêu cầu gửi OTP đặt lại mật khẩu qua email.
    /// </summary>
    /// <remarks>
    /// Endpoint này là bước đầu tiên của luồng quên mật khẩu.
    ///
    /// Body request:
    /// - <c>Email</c>: email cần đặt lại mật khẩu.
    ///
    /// Cách hoạt động:
    /// - Validate định dạng email.
    /// - Nếu email tồn tại và account đủ điều kiện, hệ thống sinh OTP reset password và gửi qua email.
    /// - Nếu email không tồn tại, API vẫn trả response thành công theo chính sách chống dò tài khoản.
    ///
    /// Lưu ý bảo mật:
    /// - Endpoint luôn trả HTTP 200 trong các trường hợp hợp lệ về format để không tiết lộ email có tồn tại hay không.
    /// - Client nên hiển thị thông báo trung lập như: "Nếu email tồn tại, mã xác thực đã được gửi".
    ///
    /// Idempotency:
    /// - Client NÊN gửi header <c>Idempotency-Key</c> (UUID v4) để chống tạo OTP reset trùng. Cache 24h.
    /// </remarks>
    /// <param name="command">Email cần nhận OTP reset password.</param>
    /// <param name="cancellationToken">Token hủy request khi client ngắt kết nối hoặc server dừng xử lý.</param>
    /// <returns>Thông báo trung lập về kết quả gửi OTP.</returns>
    /// <response code="200">Đã xử lý yêu cầu gửi OTP theo chính sách không tiết lộ email.</response>
    [HttpPost("forgot-password")]
    [EnableRateLimiting(RateLimitingExtensions.PolicyAnonOtp)]
    [ProducesResponseType(typeof(CommonResponse<string>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordCommand command, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(command, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Xác thực OTP reset password và nhận reset token ngắn hạn.
    /// </summary>
    /// <remarks>
    /// Endpoint này là bước thứ hai của luồng quên mật khẩu.
    ///
    /// Body request:
    /// - <c>Email</c>: email đã yêu cầu reset password.
    /// - <c>Otp</c>: mã OTP gồm đúng 6 chữ số.
    ///
    /// Cách hoạt động:
    /// - Kiểm tra OTP reset password đúng, còn hạn và chưa dùng.
    /// - Nếu hợp lệ, hệ thống trả về <c>ResetToken</c>.
    /// - <c>ResetToken</c> là JWT ngắn hạn, có mục đích riêng cho reset password và không phải access token đăng nhập.
    ///
    /// Client dùng <c>ResetToken</c> này để gọi <c>POST /api/auth/reset-password</c>.
    ///
    /// Lưu ý:
    /// - Reset token có thời hạn ngắn, hiện response có <c>ExpiresInSeconds</c> để FE đếm ngược.
    /// - Nếu OTP sai nhiều lần, account có thể bị khóa tạm thời.
    /// </remarks>
    /// <param name="command">Email và OTP reset password.</param>
    /// <param name="cancellationToken">Token hủy request khi client ngắt kết nối hoặc server dừng xử lý.</param>
    /// <returns><see cref="ResetTokenDto"/> chứa reset token và thời gian hết hạn.</returns>
    [HttpPost("verify-reset-otp")]
    [ProducesResponseType(typeof(CommonResponse<ResetTokenDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(CommonResponse<ResetTokenDto>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(CommonResponse<ResetTokenDto>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(CommonResponse<ResetTokenDto>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(CommonResponse<ResetTokenDto>), StatusCodes.Status423Locked)]
    public async Task<IActionResult> VerifyResetOtp([FromBody] VerifyResetOtpCommand command, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(command, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Đặt lại mật khẩu bằng reset token đã xác thực.
    /// </summary>
    /// <remarks>
    /// Endpoint này là bước cuối của luồng quên mật khẩu.
    ///
    /// Body request:
    /// - <c>ResetToken</c>: token nhận được từ <c>POST /api/auth/verify-reset-otp</c>.
    /// - <c>NewPassword</c>: mật khẩu mới, tối thiểu 8 ký tự, có chữ hoa, chữ thường, số và ký tự đặc biệt.
    ///
    /// Cách hoạt động:
    /// - Validate reset token đúng chữ ký, còn hạn và có purpose là reset password.
    /// - Tìm account từ claim trong reset token.
    /// - Cập nhật password hash bằng mật khẩu mới.
    /// - Revoke toàn bộ refresh token active của tài khoản để buộc các thiết bị đăng nhập lại.
    ///
    /// Lưu ý:
    /// - Reset token không thể dùng để gọi API cần đăng nhập.
    /// - Sau khi đổi mật khẩu thành công, client nên chuyển người dùng về màn hình login.
    /// </remarks>
    /// <param name="command">Reset token và mật khẩu mới.</param>
    /// <param name="cancellationToken">Token hủy request khi client ngắt kết nối hoặc server dừng xử lý.</param>
    /// <returns>Thông báo kết quả đặt lại mật khẩu.</returns>
    [HttpPost("reset-password")]
    [ProducesResponseType(typeof(CommonResponse<string>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(CommonResponse<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(CommonResponse<string>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(CommonResponse<string>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordCommand command, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(command, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Gửi lại OTP đặt lại mật khẩu.
    /// </summary>
    /// <remarks>
    /// Endpoint này dùng khi người dùng không nhận được OTP reset password hoặc OTP cũ đã hết hạn.
    ///
    /// Body request:
    /// - <c>Email</c>: email cần gửi lại OTP reset password.
    ///
    /// Cách hoạt động:
    /// - Validate định dạng email.
    /// - Nếu account tồn tại và đủ điều kiện, hệ thống sinh OTP reset password mới và gửi qua email.
    /// - Có rate limit tối thiểu 60 giây giữa các lần gửi lại.
    ///
    /// Lưu ý bảo mật:
    /// - Handler được thiết kế để hạn chế tiết lộ email tồn tại hay không.
    /// - Client nên hiển thị thông báo trung lập tương tự endpoint forgot password.
    ///
    /// Idempotency:
    /// - Client NÊN gửi header <c>Idempotency-Key</c> (UUID v4). Cache 24h.
    /// </remarks>
    /// <param name="command">Email cần gửi lại OTP reset password.</param>
    /// <param name="cancellationToken">Token hủy request khi client ngắt kết nối hoặc server dừng xử lý.</param>
    /// <returns>Thông báo kết quả gửi lại OTP.</returns>
    [HttpPost("resend-reset-otp")]
    [EnableRateLimiting(RateLimitingExtensions.PolicyAnonOtp)]
    [ProducesResponseType(typeof(CommonResponse<string>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(CommonResponse<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(CommonResponse<string>), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> ResendResetOtp([FromBody] ResendResetOtpCommand command, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(command, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    ///// <summary>
    ///// Đăng nhập / đăng ký bằng Google ID token. Auto-link nếu email đã tồn tại + EmailConfirmed=true,
    ///// auto-create nếu email chưa tồn tại (gán role Customer, EmailConfirmed=true ngay).
    ///// </summary>
    //[HttpPost("google")]
    //[ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
    //[ProducesResponseType(typeof(LoginResponse), StatusCodes.Status401Unauthorized)]
    //[ProducesResponseType(typeof(LoginResponse), StatusCodes.Status403Forbidden)]
    //[ProducesResponseType(typeof(LoginResponse), StatusCodes.Status409Conflict)]
    //public async Task<IActionResult> GoogleAuth([FromBody] GoogleAuthCommand command, CancellationToken cancellationToken)
    //{
    //    var result = await _mediator.Send(command, cancellationToken);
    //    return StatusCode(result.StatusCode, result);
    //}

    /// <summary>
    /// Bắt đầu luồng đăng nhập Google OAuth bằng cách redirect người dùng sang Google.
    /// </summary>
    /// <remarks>
    /// Endpoint này là bước 1 của server-side Google OAuth.
    ///
    /// Cách gọi:
    /// - Frontend có thể gán <c>window.location.href = "/api/auth/google/login"</c>.
    /// - Browser sẽ được redirect sang trang chọn tài khoản Google.
    ///
    /// Cách hoạt động:
    /// - Hệ thống tạo giá trị <c>state</c> ngẫu nhiên để chống CSRF OAuth.
    /// - <c>state</c> được lưu trong cookie HttpOnly tên <c>g_oauth_state</c>, thời hạn 10 phút.
    /// - URL authorization của Google được tạo với redirect URI cấu hình trong <c>GoogleOAuth:RedirectUri</c>.
    /// - Sau khi người dùng chọn Google account, Google redirect về <c>GET /api/auth/google/callback</c>.
    ///
    /// Lưu ý:
    /// - Endpoint này trả redirect, không trả JSON token trực tiếp.
    /// - Nếu thiếu cấu hình redirect URI, API trả HTTP 500.
    /// </remarks>
    /// <returns>HTTP redirect tới Google OAuth consent/selection page.</returns>
    /// <response code="302">Redirect sang Google OAuth.</response>
    /// <response code="500">Thiếu cấu hình Google OAuth redirect URI.</response>
    [HttpGet("google/login")]
    public IActionResult GoogleLogin()
    {
        var redirectUri = ResolveJsonRedirectUri();
        if (string.IsNullOrWhiteSpace(redirectUri))
            return StatusCode(500, new { isSuccess = false, message = "GoogleOAuth:RedirectUri chưa được cấu hình." });
        return BuildLoginRedirect(redirectUri);
    }

    /// <summary>
    /// Callback Google OAuth, xử lý authorization code và trả token đăng nhập.
    /// </summary>
    /// <remarks>
    /// Endpoint này là bước 2 của server-side Google OAuth. Google redirect về endpoint này với query string
    /// <c>?code=...&amp;state=...</c>.
    ///
    /// Query parameters:
    /// - <c>code</c>: authorization code do Google cấp.
    /// - <c>state</c>: giá trị state Google trả lại, phải trùng với cookie <c>g_oauth_state</c>.
    /// - <c>error</c>: lỗi Google trả về nếu người dùng hủy hoặc OAuth thất bại.
    ///
    /// Cách hoạt động:
    /// - Xóa cookie state sau khi đọc để tránh reuse.
    /// - Nếu Google trả <c>error</c>, API trả lỗi 401.
    /// - Nếu thiếu <c>code</c> hoặc <c>state</c>, API trả lỗi 400.
    /// - Nếu state không khớp cookie, API trả lỗi 401 để chặn CSRF.
    /// - Nếu hợp lệ, hệ thống exchange authorization code lấy Google token, verify danh tính Google,
    ///   sau đó đăng nhập/link/tạo tài khoản theo logic Google auth trong application layer.
    ///
    /// Response thành công:
    /// - Trả JSON <see cref="LoginResponse"/> chứa access token và refresh token.
    /// - Endpoint này không redirect về frontend sau callback, phù hợp cho Postman, WebView hoặc custom client flow.
    /// </remarks>
    /// <param name="code">Authorization code do Google trả về.</param>
    /// <param name="state">State chống CSRF, phải khớp cookie đã lưu ở bước login.</param>
    /// <param name="error">Mã lỗi từ Google nếu người dùng hủy hoặc OAuth thất bại.</param>
    /// <param name="cancellationToken">Token hủy request khi client ngắt kết nối hoặc server dừng xử lý.</param>
    /// <returns><see cref="LoginResponse"/> chứa token nếu Google OAuth thành công.</returns>
    /// <response code="200">Login thành công, body: `LoginResponse` chứa AccessToken + RefreshToken.</response>
    /// <response code="400">Code/state thiếu.</response>
    /// <response code="401">State mismatch / token exchange fail / Google verify fail.</response>
    /// <response code="403">Tài khoản banned/suspended.</response>
    /// <response code="409">Email đã tồn tại nhưng PendingVerification hoặc link Google khác.</response>
    /// <response code="500">Cấu hình thiếu.</response>
    [HttpGet("google/callback")]
    [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GoogleCallback(
        [FromQuery] string? code,
        [FromQuery] string? state,
        [FromQuery] string? error,
        CancellationToken cancellationToken)
    {
        var redirectUri = ResolveJsonRedirectUri();
        if (string.IsNullOrWhiteSpace(redirectUri))
            return StatusCode(500, new { isSuccess = false, message = "GoogleOAuth:RedirectUri chưa được cấu hình." });

        var (response, statusCode) = await ProcessCallbackAsync(code, state, error, redirectUri, cancellationToken);
        return StatusCode(statusCode, response);
    }

    // ============== shared helpers ==============

    private string? ResolveJsonRedirectUri()
        => _configuration["GoogleOAuth:RedirectUri"]
           ?? Environment.GetEnvironmentVariable("GOOGLE_REDIRECT_URI");

    private IActionResult BuildLoginRedirect(string redirectUri)
    {
        var state = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        Response.Cookies.Append(OAuthStateCookie, state, new CookieOptions
        {
            HttpOnly = true,
            Secure = Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            MaxAge = TimeSpan.FromMinutes(10),
            Path = "/api/auth"
        });

        var authUrl = _googleOAuthHelper.BuildAuthorizationUrl(state, redirectUri);
        return Redirect(authUrl);
    }

    /// <summary>
    /// Logic dùng chung cho cả 2 callback: verify state, gọi MediatR, trả LoginResponse + status code.
    /// </summary>
    private async Task<(LoginResponse response, int statusCode)> ProcessCallbackAsync(
        string? code, string? state, string? error, string redirectUri, CancellationToken cancellationToken)
    {
        var savedState = Request.Cookies[OAuthStateCookie];
        Response.Cookies.Delete(OAuthStateCookie, new CookieOptions { Path = "/api/auth" });

        if (!string.IsNullOrEmpty(error))
            return (Fail(401, error), 401);

        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(state))
            return (Fail(400, "missing_code_or_state"), 400);

        if (string.IsNullOrEmpty(savedState) || !string.Equals(savedState, state, StringComparison.Ordinal))
            return (Fail(401, "invalid_state"), 401);

        var result = await _mediator.Send(
            new GoogleCallbackCommand { Code = code, RedirectUri = redirectUri },
            cancellationToken);

        var status = result.StatusCode == 0 ? (result.IsSuccess ? 200 : 500) : result.StatusCode;
        return (result, status);
    }

    private static LoginResponse Fail(int status, string message) => new()
    {
        IsSuccess = false,
        StatusCode = status,
        Message = message,
        ListErrors = new List<Errors> { new Errors { Field = "Auth", Detail = message } }
    };
}
