namespace AuthService.Domain.Enums;

/// <summary>
/// Mục đích của OTP đang lưu trên Account để phân biệt context khi verify.
/// </summary>
public enum OtpPurposeEnum
{
    Register = 1,
    PasswordReset = 2,
    PhoneVerify = 3,
    EmailChange = 4
}
