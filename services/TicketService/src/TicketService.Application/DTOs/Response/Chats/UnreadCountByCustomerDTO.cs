namespace TicketService.Application.DTOs.Response.Chats;

/// <summary>
/// Số tin nhắn chưa đọc của actor trên toàn bộ ticket của 1 Customer.
/// </summary>
public class UnreadCountByCustomerDTO
{
    /// <summary>Id Customer (Guid dạng string, theo convention DTO).</summary>
    public string CustomerId { get; set; } = string.Empty;

    /// <summary>
    /// Số CHAT chưa đọc. Đếm theo bản ghi TicketChat nên 1 tin nhắn dù có
    /// kèm bao nhiêu @mention vẫn chỉ tính 1 — mention là bảng con của chat,
    /// không phải tin nhắn riêng.
    /// </summary>
    public int UnreadCount { get; set; }
}
