using SharedContracts.Common.Responses;

namespace TicketService.Application.DTOs.Response.Chats;

public class ChatSummarizeResponse : CommonResponse<ChatSummarizeDTO> { }

public class ChatSummarizeDTO
{
    /// <summary>Tóm tắt thread dạng bullet list, mỗi dòng 1 ý chính.</summary>
    public string Summary { get; set; } = string.Empty;
}
