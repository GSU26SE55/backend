namespace TicketService.Application.DTOs.Response.KnowledgeBases;

public class KbArticleSuggestDTO
{
    /// <summary>
    /// Id.
    /// </summary>
    public string Id { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    /// <summary>
    /// Symptoms.
    /// </summary>
    public string Symptoms { get; set; } = string.Empty;
    public int HelpfulCount { get; set; }
    public int ViewCount { get; set; }

    /// <summary>
    /// true = bài viết nội bộ, không được cung cấp cho khách hàng (không gán ReferenceType ProvidedToCustomer).
    /// Lưu ý: các endpoint suggest hiện lọc sẵn bài nội bộ nên giá trị thường là false —
    /// field tồn tại để FE cảnh báo/ẩn khi nguồn danh sách bài thay đổi.
    /// </summary>
    public bool IsInternalOnly { get; set; }
}
