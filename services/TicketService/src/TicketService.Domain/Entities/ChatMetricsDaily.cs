using SharedKernels.Domain;

namespace TicketService.Domain.Entities;

public class ChatMetricsDaily : AuditableEntity
{
    public DateOnly MetricDate { get; set; }
    public Guid StaffId { get; set; }
    public Guid TicketId { get; set; }
    public int ChatCount { get; set; }
    public double AvgResponseTimeMin { get; set; }
    public int InternalCount { get; set; }
    public int MentionReceivedCount { get; set; }
}
