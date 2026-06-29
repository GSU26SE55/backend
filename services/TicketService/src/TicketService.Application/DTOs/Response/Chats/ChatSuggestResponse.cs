using SharedContracts.Common.Responses;

namespace TicketService.Application.DTOs.Response.Chats;

public class ChatSuggestResponse : CommonResponse<ChatSuggestDTO> { }

public class ChatSuggestDTO
{
    /// <summary>Id của row ChatAiSuggestion đã log — dùng để cập nhật SelectedIndex sau (#559).</summary>
    public string SuggestionId { get; set; } = string.Empty;

    public List<string> Suggestions { get; set; } = new();
}
