using TicketService.Domain.Enums;

namespace TicketService.Application.DTOs.Response.Maintenances;

public class MaintenanceLogDTO
{
    /// <summary>
    /// Id.
    /// </summary>
    public string Id { get; set; } = string.Empty;
    public string StaffId { get; set; } = string.Empty;
    /// <summary>
    /// Tên Staff đã ghi log — tra từ StaffAccount đã sync, để Admin/Manager biết ai nộp
    /// báo cáo mà không phải gọi /api/staff. Null khi tài khoản đó không còn tra được.
    /// </summary>
    public string? StaffName { get; set; }
    public MaintenanceLogTypeEnum LogType { get; set; }
    /// <summary>
    /// Summary.
    /// </summary>
    public string Summary { get; set; } = string.Empty;
    public string? DiagnosisDetails { get; set; }
    public string? ActionsTaken { get; set; }
    /// <summary>
    /// Duration minutes.
    /// </summary>
    public int DurationMinutes { get; set; }
    public string? ResolutionNote { get; set; }
    /// <summary>
    /// Linh kiện đã dùng. Entity vẫn lưu cột này, nhưng DTO trước đây không có nên
    /// FE/mobile mở log ra sửa là ô này rỗng và lưu lại sẽ xoá mất dữ liệu cũ.
    /// </summary>
    public string? PartsUsed { get; set; }
    public DateTime StartedAt { get; set; }
    /// <summary>
    /// Completed at.
    /// </summary>
    public DateTime? CompletedAt { get; set; }
    public List<string> AttachmentFileIds { get; set; } = new();
    public List<string> BeforePhotosFileIds { get; set; } = new();
    /// <summary>
    /// After photos file ids.
    /// </summary>
    public List<string> AfterPhotosFileIds { get; set; } = new();
    public List<string> RelatedKbArticleIds { get; set; } = new();
    public DateTime CreatedAt { get; set; }
}
