using NotificationService.Domain.Enums;

namespace NotificationService.Application.DTOs.Response.Notification;

public class NotificationDto
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public NotificationTypeEnum Type { get; set; }
    public NotificationChannelEnum Channel { get; set; }
    public NotificationStatusEnum Status { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string? PayloadJson { get; set; }
    public string? EntityType { get; set; }
    public Guid? EntityId { get; set; }
    public DateTime? SentAt { get; set; }
    public DateTime? ReadAt { get; set; }
    public DateTime CreatedAt { get; set; }
}
