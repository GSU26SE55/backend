using TicketService.Domain.Enums;

namespace TicketService.Application.DTOs.Response.Comments;

public class CommentActionDTO
{
    public string Id { get; set; } = string.Empty;
    public string? TicketId { get; set; }
    public string Code { get; set; } = string.Empty;
    public TicketStatusEnum Status { get; set; }
}
