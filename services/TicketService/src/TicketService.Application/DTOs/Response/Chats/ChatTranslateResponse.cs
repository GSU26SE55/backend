using SharedContracts.Common.Responses;

namespace TicketService.Application.DTOs.Response.Chats;

public class ChatTranslateResponse : CommonResponse<ChatTranslateDTO> { }

public class ChatTranslateDTO
{
    /// <summary>
    /// Translated body.
    /// </summary>
    public string TranslatedBody { get; set; } = string.Empty;
    public string TargetLanguage { get; set; } = string.Empty;
    public string? OriginalLanguage { get; set; }
    /// <summary>
    /// Provider.
    /// </summary>
    public string Provider { get; set; } = string.Empty;
    public bool FromCache { get; set; }
}
