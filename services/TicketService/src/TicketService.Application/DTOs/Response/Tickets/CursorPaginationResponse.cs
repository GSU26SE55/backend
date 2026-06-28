using System.Collections.Generic;

namespace TicketService.Application.DTOs.Response.Tickets;

public class CursorPaginationResponse<T>
{
    /// <summary>
    /// Items.
    /// </summary>
    public List<T> Items { get; set; } = new();
    public string? NextCursor { get; set; }
    public bool HasMore { get; set; }
}
