namespace AuthService.Application.DTOs.Response.Auth;

/// <summary>
/// Response của <c>POST /api/accounts/me/2fa/confirm</c>. <see cref="BackupCodes"/> chỉ trả về 1 lần
/// — sau response này không cách nào lấy lại; user phải lưu ngay.
/// </summary>
public class TwoFactorConfirmDto
{
    public bool Enabled { get; set; }
    public List<string> BackupCodes { get; set; } = new();
}
