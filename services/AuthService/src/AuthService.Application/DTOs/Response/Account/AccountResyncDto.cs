namespace AuthService.Application.DTOs.Response.Account;

/// <summary>
/// 02/08/2026 — Kết quả một lượt đối soát read-model account (<c>POST /api/admin/accounts/resync</c>).
/// Các con số dưới đây mô tả trạng thái mà read-model của service tiêu thụ PHẢI đạt được sau khi
/// tiêu hoá hết snapshot, nên dùng nó làm kỳ vọng khi đi kiểm chứng.
/// </summary>
public class AccountResyncDto
{
    /// <summary>Số account đã quét và phát snapshot (gồm cả account đã xoá mềm).</summary>
    public int TotalAccounts { get; set; }

    /// <summary>
    /// Số account <c>Status = Active</c> và chưa xoá — đây chính là số dòng mà read-model bên
    /// NotificationService phải có với <c>is_active = true</c> sau khi đồng bộ xong.
    /// </summary>
    public int ActiveAccounts { get; set; }

    /// <summary>Số account chưa xoá nhưng không ở trạng thái Active (chờ xác thực, khoá, đình chỉ, cấm).</summary>
    public int InactiveAccounts { get; set; }

    /// <summary>Số account đã xoá mềm — read-model sẽ đánh dấu xoá tương ứng.</summary>
    public int DeletedAccounts { get; set; }
}
