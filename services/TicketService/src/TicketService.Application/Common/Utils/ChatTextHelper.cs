namespace TicketService.Application.Common.Utils;

public static class ChatTextHelper
{
    public static string Truncate(string text, int maxLength = 50)
        => text[..Math.Min(text.Length, maxLength)];
}
