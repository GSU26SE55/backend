using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SharedContracts.Common.Responses;
using TicketService.Application.CQRS.Command.KnowledgeBase;
using TicketService.Application.CQRS.Query.KnowledgeBase;
using TicketService.Application.DTOs.Response.KnowledgeBases;
using TicketService.Application.Interfaces.Services;

namespace TicketService.Api.Controllers;

/// <summary>
/// Controller dành cho nội bộ (Staff, Manager, Admin) để soạn thảo và quản lý nội dung bài viết Knowledge Base.
/// </summary>
[ApiController]
[Route("api/internal/knowledge-base")]
[Authorize(Roles = "Staff,Manager,Admin")]
[Produces("application/json")]
public class InternalKnowledgeBaseController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ITicketCurrentUserService _currentUser;

    public InternalKnowledgeBaseController(IMediator mediator, ITicketCurrentUserService currentUser)
    {
        _mediator = mediator;
        _currentUser = currentUser;
    }

    /// <summary>
    /// Tạo mới một bài viết Knowledge Base.
    /// </summary>
    /// <remarks>
    /// - Yêu cầu nhập đầy đủ tiêu đề, triệu chứng, các bước chẩn đoán và xử lý.
    /// - Bài viết sẽ được khởi tạo với Version 0 và trạng thái Chờ phê duyệt (PendingReview).
    /// - Quản lý (Manager/Admin) cần duyệt (Approve) để bài viết chuyển sang trạng thái Published (Version 1).
    /// </remarks>
    /// <param name="command">Thông tin bài viết mới.</param>
    /// <param name="ct">Token hủy request.</param>
    /// <response code="201">Tạo thành công.</response>
    /// <response code="400">Dữ liệu không hợp lệ.</response>
    [HttpPost]
    [ProducesResponseType(typeof(CommonResponse<KbArticleActionDTO>), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] CreateKbArticleCommand command, CancellationToken ct)
    {
        command.CurrentUserId = GetCurrentUserId();
        command.CurrentUserRole = _currentUser.Role ?? string.Empty;

        var result = await _mediator.Send(command, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Cập nhật nội dung bài viết hiện có.
    /// </summary>
    /// <remarks>
    /// - Trước khi cập nhật, hệ thống tự động lưu lại phiên bản hiện tại vào lịch sử.
    /// - Nếu Staff thực hiện thay đổi bài viết không phải do mình tạo, bài viết sẽ được chuyển sang trạng thái "Chờ phê duyệt" (PendingReview).
    /// - Manager/Admin hoặc chủ sở hữu bài viết có quyền cập nhật trực tiếp mà không cần phê duyệt lại.
    /// </remarks>
    /// <param name="id">ID bài viết cần cập nhật.</param>
    /// <param name="command">Nội dung cập nhật và lý do thay đổi.</param>
    /// <param name="ct">Token hủy request.</param>
    /// <response code="200">Cập nhật thành công.</response>
    /// <response code="403">Không có quyền cập nhật.</response>
    /// <response code="404">Không tìm thấy bài viết.</response>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(CommonResponse<KbArticleDTO>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateKbArticleCommand command, CancellationToken ct)
    {
        command.ArticleId = id;
        command.CurrentUserId = GetCurrentUserId();
        command.CurrentUserRole = _currentUser.Role ?? string.Empty;

        var result = await _mediator.Send(command, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Xem danh sách các phiên bản cũ (lịch sử thay đổi) của bài viết.
    /// </summary>
    /// <param name="id">ID bài viết.</param>
    /// <param name="ct">Token hủy request.</param>
    /// <response code="200">Lấy lịch sử thành công.</response>
    [HttpGet("{id:guid}/versions")]
    [ProducesResponseType(typeof(CommonResponse<List<KbArticleVersionDTO>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetVersions(Guid id, CancellationToken ct)
    {
        var query = new GetKbArticleVersionsQuery { ArticleId = id };
        var result = await _mediator.Send(query, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Xem nội dung chi tiết của một phiên bản bài viết cụ thể trong quá khứ.
    /// </summary>
    /// <param name="id">ID bài viết.</param>
    /// <param name="versionId">ID của phiên bản cụ thể.</param>
    /// <param name="ct">Token hủy request.</param>
    /// <response code="200">Lấy nội dung phiên bản thành công.</response>
    /// <response code="404">Không tìm thấy phiên bản.</response>
    [HttpGet("{id:guid}/versions/{versionId:guid}")]
    [ProducesResponseType(typeof(CommonResponse<KbArticleVersionDTO>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetVersionById(Guid id, Guid versionId, CancellationToken ct)
    {
        var query = new GetKbArticleVersionByIdQuery { VersionId = versionId };
        var result = await _mediator.Send(query, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// So sánh sự khác biệt giữa hai phiên bản của bài viết.
    /// </summary>
    /// <remarks>
    /// Tham số <c>toVersion</c> nếu truyền giá trị 0 sẽ mặc định so sánh với phiên bản hiện tại của bài viết.
    /// </remarks>
    /// <param name="id">ID bài viết.</param>
    /// <param name="query">Chứa thông tin version gốc (fromVersion) và version đích (toVersion).</param>
    /// <param name="ct">Token hủy request.</param>
    /// <response code="200">Trả về kết quả so sánh (Diff).</response>
    [HttpGet("{id:guid}/compare")]
    [ProducesResponseType(typeof(CommonResponse<KbArticleDiffDTO>), StatusCodes.Status200OK)]
    public async Task<IActionResult> CompareVersions(Guid id, CancellationToken ct)
    {
        var query = new CompareKbArticleVersionsQuery { ArticleId = id };
        var result = await _mediator.Send(query, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Sao chép cấu trúc bài viết mẫu để tạo bài viết mới.
    /// </summary>
    /// <remarks>
    /// Chỉ áp dụng cho các bài viết có gắn thẻ "template" hoặc "example".
    /// Trả về khung nội dung (triệu chứng, các bước xử lý) để Staff điền tiếp.
    /// </remarks>
    /// <param name="id">ID bài viết mẫu.</param>
    /// <param name="ct">Token hủy request.</param>
    /// <response code="200">Lấy template thành công.</response>
    /// <response code="400">Bài viết không phải là bản mẫu.</response>
    [HttpGet("{id:guid}/copy-template")]
    [ProducesResponseType(typeof(CommonResponse<KbArticleTemplateDTO>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CopyTemplate(Guid id, CancellationToken ct)
    {
        var query = new CopyKbArticleTemplateQuery { ArticleId = id };
        var result = await _mediator.Send(query, ct);
        return StatusCode(result.StatusCode, result);
    }

    private Guid GetCurrentUserId()
    {
        return string.IsNullOrEmpty(_currentUser.UserId) ? Guid.Empty : Guid.Parse(_currentUser.UserId);
    }
}
