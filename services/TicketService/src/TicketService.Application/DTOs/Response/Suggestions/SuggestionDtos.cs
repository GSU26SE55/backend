namespace TicketService.Application.DTOs.Response.Suggestions;

public class StaffSuggestionDto
{
    public string StaffId { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public int SkillTier { get; set; }
    public List<string> SkillCodes { get; set; } = new();
    public int ActiveTickets { get; set; }
    public int MaxConcurrentTickets { get; set; }

    /// <summary>Điểm phù hợp [0..1] do AI chấm.</summary>
    public double Score { get; set; }

    /// <summary>Lý do tiếng Việt — vì sao người này được đề xuất.</summary>
    public string Reason { get; set; } = string.Empty;
}

public class StaffSuggestionListDto
{
    public List<StaffSuggestionDto> Items { get; set; } = new();

    /// <summary>
    /// Ghi chú khi danh sách rỗng hoặc yếu (vd "không ai đủ tier P1").
    /// </summary>
    /// <remarks>
    /// UI phải hiển thị trường này thay vì bảng trắng — người dùng cần biết
    /// "không có ai phù hợp" khác với "hệ thống gợi ý hỏng".
    /// </remarks>
    public string Note { get; set; } = string.Empty;

    /// <summary>
    /// <c>false</c> khi AI không phản hồi được — danh sách rỗng vì lỗi kỹ thuật,
    /// KHÔNG phải vì không có ứng viên phù hợp.
    /// </summary>
    public bool AiAvailable { get; set; } = true;
}

public class KbSuggestionDto
{
    public string KbArticleId { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public double Score { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public class KbSuggestionListDto
{
    public List<KbSuggestionDto> Items { get; set; } = new();
    public string Note { get; set; } = string.Empty;
    public bool AiAvailable { get; set; } = true;
}
