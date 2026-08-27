using SharedKernels.Domain;

namespace BatteryService.Domain.Entities;

public class CustomerAccount : AuditableEntity
{
    public string Email { get; set; } = null!;

    public string FullName { get; set; } = null!;

    public string? PhoneNumber { get; set; }

    /// <summary>
    /// Role hiện tại của account (single — quan hệ 1-N: Role → Account).
    /// Trước đây là <c>RolesCsv</c> (multi-role) — đã đổi sang single sau khi AuthService
    /// chuyển sang quan hệ 1-N.
    /// </summary>
    public string Role { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public DateTime LastSyncedAtUtc { get; set; }

    /// <summary>
    /// Mốc phát sinh mới nhất từ AuthService đã áp vào projection. Khác với
    /// <see cref="LastSyncedAtUtc"/> (thời điểm consumer chạy), mốc này dùng để chặn event tới trễ
    /// kéo dữ liệu lùi về trạng thái cũ.
    /// </summary>
    public DateTime? LastSourceEventAtUtc { get; set; }
}
