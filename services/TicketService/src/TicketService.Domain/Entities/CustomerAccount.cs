using SharedKernels.Domain;
using TicketService.Domain.Enums;

namespace TicketService.Domain.Entities;

public class CustomerAccount : AuditableEntity
{
    public Guid AccountId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }

    /// <summary>Ảnh đại diện đồng bộ từ AuthService — dùng vẽ avatar "đã xem" trong chat.</summary>
    public string? AvatarUrl { get; set; }

    public AccountStatusEnum Status { get; set; }
    public DateTime LastSyncedAt { get; set; }
    public DateTime? LastSourceEventAtUtc { get; set; }
}
