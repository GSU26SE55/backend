using TicketService.Domain.Enums;

namespace TicketService.Application.DTOs.Response.Chats;

public class ChatReactionUserDTO
{
    /// <summary>
    /// ID của người dùng.
    /// </summary>
    public string UserId { get; set; } = string.Empty;
    public ActorRoleEnum Role { get; set; }
}

public class ChatReactionGroupDTO
{
    /// <summary>
    /// Count.
    /// </summary>
    public int Count { get; set; }
    public List<ChatReactionUserDTO> Users { get; set; } = new();
}

public class TicketChatReactionsAggregateDTO
{
    /// <summary>
    /// Thumbs up.
    /// </summary>
    public ChatReactionGroupDTO ThumbsUp { get; set; } = new();
    public ChatReactionGroupDTO Acknowledged { get; set; } = new();
    public ChatReactionGroupDTO Resolved { get; set; } = new();
    /// <summary>
    /// Need more info.
    /// </summary>
    public ChatReactionGroupDTO NeedMoreInfo { get; set; } = new();
    public ChatReactionGroupDTO Disagree { get; set; } = new();
}
