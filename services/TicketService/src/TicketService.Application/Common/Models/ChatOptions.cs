namespace TicketService.Application.Common.Models;

public class ChatOptions
{
    public const string SectionName = "Chat";

    public int EditWindowMinutes { get; set; } = 15;
    public int MinBodyLength { get; set; } = 1;
    public int MaxBodyLength { get; set; } = 10000;
}
