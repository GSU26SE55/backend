namespace AuthService.Application.DTOs.Response.Auth;

/// <summary>Response của <c>POST /api/accounts/me/2fa/backup-codes/regenerate</c> — plain codes chỉ trả về 1 lần.</summary>
public class BackupCodesDto
{
    public List<string> BackupCodes { get; set; } = new();
}
