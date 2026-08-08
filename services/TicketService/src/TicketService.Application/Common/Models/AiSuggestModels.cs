namespace TicketService.Application.Common.Models;

/// <summary>Ứng viên nhân viên gửi sang AI để xếp hạng — BE truy vấn sẵn.</summary>
/// <remarks>
/// ai-module không đọc được DB của TicketService nên dữ liệu phải đi kèm request
/// (cùng cách <c>DuplicateCandidate</c> của VerifyTicket đang làm).
/// </remarks>
public record AiStaffCandidate(
    string StaffId,
    string FullName,
    int SkillTier,
    IReadOnlyList<string> SkillCodes,
    int ActiveTickets,
    int MaxConcurrent);

/// <summary>Một nhân viên được AI đề xuất, kèm lý do để Manager đọc.</summary>
public record AiStaffSuggestion(
    string StaffId,
    string FullName,
    double Score,
    string Reason,
    bool TierOk);

/// <summary>Kết quả xếp hạng nhân viên. <c>Note</c> giải thích khi danh sách rỗng.</summary>
public record AiStaffSuggestResult(
    IReadOnlyList<AiStaffSuggestion> Suggestions,
    string Note);

/// <summary>Ứng viên bài viết KB gửi sang AI — BE đã lọc Status=Published.</summary>
public record AiKbCandidate(
    string KbId,
    string Code,
    string Title,
    IReadOnlyList<string> Tags,
    int Category,
    int HelpfulCount);

/// <summary>Một bài viết KB được AI đề xuất, kèm lý do.</summary>
public record AiKbSuggestion(
    string KbId,
    string Code,
    string Title,
    double Score,
    string Reason);

/// <summary>Kết quả xếp hạng KB. <c>Note</c> giải thích khi danh sách rỗng.</summary>
public record AiKbSuggestResult(
    IReadOnlyList<AiKbSuggestion> Suggestions,
    string Note);
