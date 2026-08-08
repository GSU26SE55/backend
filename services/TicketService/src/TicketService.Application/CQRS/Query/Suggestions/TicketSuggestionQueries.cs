using MediatR;
using SharedContracts.Common.Responses;
using TicketService.Application.DTOs.Response.Suggestions;

namespace TicketService.Application.CQRS.Query.Suggestions;

/// <summary>
/// Manager triage: xin AI xếp hạng nhân viên phù hợp xử lý ticket.
/// Chỉ đọc — không phân công, không ghi gì.
/// </summary>
public class TicketStaffSuggestionsQuery : IRequest<CommonResponse<StaffSuggestionListDto>>
{
    public Guid TicketId { get; set; }
    public int TopN { get; set; } = 5;
}

/// <summary>
/// Kỹ thuật viên được phân công: xin AI xếp hạng bài viết KB để tham khảo.
/// Chỉ đọc — bấm áp dụng mới tạo <c>TicketKbReference</c> qua lệnh riêng.
/// </summary>
public class TicketKbSuggestionsQuery : IRequest<CommonResponse<KbSuggestionListDto>>
{
    public Guid TicketId { get; set; }
    public int TopN { get; set; } = 5;
}
