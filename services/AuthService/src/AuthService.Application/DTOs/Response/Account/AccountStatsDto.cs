namespace AuthService.Application.DTOs.Response.Account;

/// <summary>
/// Snapshot thống kê account cho Dashboard Admin — thay cho việc FE tự đếm role trên 1 trang list (bị cap theo pageSize).
/// </summary>
public class AccountStatsDto
{
    /// <summary>Tổng số account (không tính đã xóa) — có thể lớn hơn tổng CountByRole nếu tồn tại account chưa gán role.</summary>
    public int Total { get; set; }

    /// <summary>Số account theo tên role (Admin/Manager/Staff/Customer) — zero-fill đủ mọi role đang có trong hệ thống.</summary>
    public Dictionary<string, int> CountByRole { get; set; } = new();
}
