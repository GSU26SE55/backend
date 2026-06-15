namespace AuthService.Application.DTOs.Response.Auth;

/// <summary>
/// Discriminated union response của <c>POST /api/auth/login</c>.
/// - <see cref="Tokens"/> set ⇔ login complete (account không bật 2FA, hoặc đã verify 2FA xong)
/// - <see cref="Challenge"/> set ⇔ account bật 2FA, client cần gọi tiếp <c>/login/verify-2fa</c>
///
/// Đúng 1 trong 2 nullable property sẽ có giá trị trong thành công case. <see cref="RequiresTwoFactor"/>
/// tiện cho FE check.
/// </summary>
public class LoginResultDto
{
    public TokenDTO? Tokens { get; set; }
    public TwoFactorChallengeDto? Challenge { get; set; }

    public bool RequiresTwoFactor => Challenge != null;
}
