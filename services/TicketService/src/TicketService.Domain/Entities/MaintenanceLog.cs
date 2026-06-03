using System.Collections.Generic;
using SharedKernels.Domain;
using TicketService.Domain.Enums;

namespace TicketService.Domain.Entities;

public class MaintenanceLog : AuditableEntity
{
    public Guid TicketId { get; set; }
    public Guid StaffId { get; set; }
    public MaintenanceLogTypeEnum LogType { get; set; }
    public string Summary { get; set; } = string.Empty;
    public string? DiagnosisDetails { get; set; }
    public string? ActionsTaken { get; set; }
    public int DurationMinutes { get; set; }
    public string? ResolutionNote { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? PartsUsed { get; set; } // JSONB
    public List<Guid> AttachmentFileIds { get; set; } = new();
    public List<Guid> BeforePhotosFileIds { get; set; } = new();
    public List<Guid> AfterPhotosFileIds { get; set; } = new();
    public List<Guid> RelatedKbArticleIds { get; set; } = new();
    public decimal? CheckInLatitude { get; set; }
    public decimal? CheckInLongitude { get; set; }
    public DateTime? CheckInAt { get; set; }

    public Ticket Ticket { get; set; } = null!;
}
