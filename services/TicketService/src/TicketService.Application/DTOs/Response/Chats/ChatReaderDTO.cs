using TicketService.Domain.Enums;

namespace TicketService.Application.DTOs.Response.Chats;

public class ChatReaderDTO
{
    public string ChatId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public ActorRoleEnum Role { get; set; }
    public DateTime ReadAt { get; set; }
}
