using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SharedContracts.Common.Responses;
using TicketService.Application.CQRS.Query.Suggestions;
using TicketService.Application.DTOs.Response.Suggestions;

namespace TicketService.Api.Controllers;

/// <summary>
/// Gợi ý do AI đưa ra cho một Ticket — nhân viên phù hợp và tài liệu KB tham khảo.
/// </summary>
/// <remarks>
/// Human-in-the-loop: mọi endpoint ở đây CHỈ ĐỌC. AI xếp hạng và nêu lý do; Manager
/// quyết định phân công cho ai, kỹ thuật viên quyết định đọc tài liệu nào. Không có
/// hành động nào tự động phân công hay tự gắn tài liệu vào ticket.
/// <para>
/// AI không phản hồi được thì trả danh sách rỗng kèm <c>aiAvailable=false</c> —
/// không bao giờ chặn luồng triage hay sửa chữa.
/// </para>
/// </remarks>
[ApiController]
[Route("api/tickets/{ticketId:guid}")]
[Produces("application/json")]
public class TicketSuggestionsController : ControllerBase
{
    private readonly IMediator _mediator;

    public TicketSuggestionsController(IMediator mediator) => _mediator = mediator;

    /// <summary>
    /// Xếp hạng nhân viên phù hợp xử lý ticket (Manager dùng khi triage).
    /// </summary>
    /// <remarks>
    /// Danh sách đã lọc theo đúng điều kiện mà lệnh phân công kiểm tra (đủ tier theo
    /// priority, chưa đầy tải, đang sẵn sàng) — nên người được gợi ý luôn phân công được.
    /// Manager vẫn có thể chọn người ngoài danh sách.
    /// </remarks>
    /// <response code="200">Trả danh sách đã xếp hạng (có thể rỗng — xem <c>note</c>).</response>
    /// <response code="404">Không tìm thấy Ticket.</response>
    [HttpGet("staff-suggestions")]
    [Authorize(Roles = "Manager,Admin")]
    [ProducesResponseType(typeof(CommonResponse<StaffSuggestionListDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(CommonResponse<StaffSuggestionListDto>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetStaffSuggestions(
        Guid ticketId, [FromQuery] int topN = 5, CancellationToken ct = default)
    {
        var result = await _mediator.Send(
            new TicketStaffSuggestionsQuery { TicketId = ticketId, TopN = topN }, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Xếp hạng bài viết Knowledge Base để tham khảo khi sửa chữa.
    /// </summary>
    /// <remarks>
    /// Staff phải được phân công vào ticket (PrimaryHandler hoặc Supporter). Xem được
    /// gợi ý KHÔNG đồng nghĩa được gắn tài liệu — việc gắn vẫn chỉ dành cho PrimaryHandler.
    /// </remarks>
    /// <response code="200">Trả danh sách đã xếp hạng (có thể rỗng — xem <c>note</c>).</response>
    /// <response code="403">Staff không được phân công xử lý ticket này.</response>
    /// <response code="404">Không tìm thấy Ticket.</response>
    [HttpGet("kb-suggestions")]
    [Authorize(Roles = "Staff,Manager,Admin")]
    [ProducesResponseType(typeof(CommonResponse<KbSuggestionListDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(CommonResponse<KbSuggestionListDto>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(CommonResponse<KbSuggestionListDto>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetKbSuggestions(
        Guid ticketId, [FromQuery] int topN = 5, CancellationToken ct = default)
    {
        var result = await _mediator.Send(
            new TicketKbSuggestionsQuery { TicketId = ticketId, TopN = topN }, ct);
        return StatusCode(result.StatusCode, result);
    }
}
