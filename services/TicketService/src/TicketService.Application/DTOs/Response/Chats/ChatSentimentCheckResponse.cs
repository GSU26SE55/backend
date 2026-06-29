using SharedContracts.Common.Responses;

namespace TicketService.Application.DTOs.Response.Chats;

public class ChatSentimentCheckResponse : CommonResponse<ChatSentimentCheckDTO> { }

public class ChatSentimentCheckDTO
{
    /// <summary>Score trong khoảng [-1.0, 1.0]: âm = tiêu cực, dương = tích cực.</summary>
    public double Score { get; set; }

    /// <summary>Positive | Neutral | Negative | Critical</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>True nếu score &lt; threshold và đã gửi SignalR alert tới Manager.</summary>
    public bool IsAlertSent { get; set; }
}
