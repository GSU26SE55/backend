namespace AuthService.Application.Interfaces.Services;

/// <summary>
/// Sinh + verify backup code cho 2FA recovery.
/// Plain code format: <c>xxxx-xxxx</c> (10 ký tự alphanum, bỏ 0/o/l/1 để tránh nhầm) — chỉ trả về user 1 lần.
/// DB lưu BCrypt hash (cost 11) của plain (đã normalize: lowercase + bỏ dash).
/// </summary>
public interface IBackupCodeGenerator
{
    /// <summary>Sinh <paramref name="count"/> codes plain format <c>xxxx-xxxx</c>.</summary>
    IReadOnlyList<string> Generate(int count = 8);

    /// <summary>Hash plain code bằng BCrypt cost 11. Tự normalize (lowercase + bỏ dash).</summary>
    string Hash(string plainCode);

    /// <summary>Verify plain code khớp hash. Tự normalize input.</summary>
    bool Verify(string plainCode, string hash);

    /// <summary>Normalize input từ user (trim, lowercase, bỏ dash) — dùng trước khi compare.</summary>
    string Normalize(string plainCode);
}
