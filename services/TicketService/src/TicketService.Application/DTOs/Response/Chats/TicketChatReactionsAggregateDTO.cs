using TicketService.Domain.Enums;

namespace TicketService.Application.DTOs.Response.Chats;

public class ChatReactionUserDTO
{
    public string UserId { get; set; } = string.Empty;
    public ActorRoleEnum Role { get; set; }
}

public class ChatReactionGroupDTO
{
    public int Count { get; set; }
    public List<ChatReactionUserDTO> Users { get; set; } = new();
}

public class TicketChatReactionsAggregateDTO
{
    public ChatReactionGroupDTO ThumbsUp { get; set; } = new();
    public ChatReactionGroupDTO Acknowledged { get; set; } = new();
    public ChatReactionGroupDTO Resolved { get; set; } = new();
    public ChatReactionGroupDTO NeedMoreInfo { get; set; } = new();
    public ChatReactionGroupDTO Disagree { get; set; } = new();
}
