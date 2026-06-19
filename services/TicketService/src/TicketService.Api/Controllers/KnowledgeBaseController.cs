using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SharedContracts.Common.Responses;
using TicketService.Application.CQRS.Command.KnowledgeBase;
using TicketService.Application.CQRS.Query.KnowledgeBase;
using TicketService.Application.DTOs.Response.KnowledgeBases;

namespace TicketService.Api.Controllers;

/// <summary>
/// Controller chung cho việc tra cứu Knowledge Base.
/// Áp dụng cho mọi role đã đăng nhập (Customer, Staff, Manager, Admin).
/// </summary>
[ApiController]
[Route("api/knowledge-base")]
[Authorize]
[Produces("application/json")]
public class KnowledgeBaseController : ControllerBase
{
    private readonly IMediator _mediator;

    public KnowledgeBaseController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Tìm kiếm và lấy danh sách bài viết Knowledge Base.
    /// </summary>
    /// <remarks>
    /// Các tham số lọc:
    /// - <c>Category</c>: Lọc theo danh mục bài viết.
    /// - <c>Status</c>: Lọc theo trạng thái (Draft, PendingReview, Published, Archived).
    /// - <c>Tag</c>: Lọc theo thẻ.
    /// - <c>Q</c>: Từ khóa tìm kiếm trong tiêu đề và nội dung triệu chứng.
    /// - <c>PageNumber</c>, <c>PageSize</c>: Phân trang.
    /// </remarks>
    /// <param name="query">Tiêu chí lọc và phân trang.</param>
    /// <param name="ct">Token hủy request.</param>
    /// <response code="200">Lấy danh sách thành công.</response>
    [HttpGet]
    [ProducesResponseType(typeof(CommonResponse<PaginationResponse<KbArticleListItemDTO>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetList([FromQuery] GetKbArticleListQuery query, CancellationToken ct)
    {
        var result = await _mediator.Send(query, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Lấy thông tin chi tiết của một bài viết Knowledge Base theo ID.
    /// </summary>
    /// <param name="id">ID của bài viết.</param>
    /// <param name="ct">Token hủy request.</param>
    /// <response code="200">Tìm thấy bài viết.</response>
    /// <response code="404">Không tìm thấy bài viết hoặc bài viết đã bị xóa.</response>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(CommonResponse<KbArticleDTO>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var query = new GetKbArticleByIdQuery { ArticleId = id };
        var result = await _mediator.Send(query, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Gợi ý các bài viết Knowledge Base phù hợp dựa trên thông tin Ticket.
    /// </summary>
    /// <remarks>
    /// Hệ thống tìm kiếm các bài viết đã xuất bản (Published) có cùng danh mục (Category) với Ticket
    /// và ưu tiên các bài có độ hữu ích (HelpfulCount) và lượt xem (ViewCount) cao.
    /// </remarks>
    /// <param name="query">Thông tin Ticket ID để gợi ý.</param>
    /// <param name="ct">Token hủy request.</param>
    /// <response code="200">Lấy danh sách gợi ý thành công (tối đa 5 bài).</response>
    /// <response code="404">Không tìm thấy Ticket.</response>
    [HttpGet("suggest")]
    [ProducesResponseType(typeof(CommonResponse<List<KbArticleSuggestDTO>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Suggest([FromQuery] SuggestKbArticlesQuery query, CancellationToken ct)
    {
        var result = await _mediator.Send(query, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Đánh dấu một bài viết là hữu ích.
    /// </summary>
    /// <remarks>
    /// Tăng chỉ số HelpfulCount của bài viết nhằm cải thiện độ tin cậy và xếp hạng gợi ý.
    /// </remarks>
    /// <param name="id">ID của bài viết.</param>
    /// <param name="ct">Token hủy request.</param>
    /// <response code="200">Đánh giá thành công.</response>
    /// <response code="404">Không tìm thấy bài viết.</response>
    [HttpPost("{id:guid}/helpful")]
    [ProducesResponseType(typeof(CommonResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> MarkHelpful(Guid id, CancellationToken ct)
    {
        var command = new MarkHelpfulCommand { ArticleId = id };
        var result = await _mediator.Send(command, ct);
        return StatusCode(result.StatusCode, result);
    }
}
