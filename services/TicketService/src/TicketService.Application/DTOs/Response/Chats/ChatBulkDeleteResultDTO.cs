namespace TicketService.Application.DTOs.Response.Chats;

public class ChatBulkDeleteResultDTO
{
    public int Deleted { get; set; }
    public int Skipped { get; set; }
    public List<string> SkippedIds { get; set; } = new();
}
