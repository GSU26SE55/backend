using BatteryService.Application.CQRS.Command.AnomalyClassification;
using BatteryService.Application.CQRS.Query.AnomalyClassification;
using BatteryService.Application.DTOs;
using BatteryService.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SharedContracts.Common.Responses;
using SharedInfrastructure.Services;

namespace BatteryService.Api.Controllers;

/// <summary>
/// Sprint Bonus NS-26 (#666, F2 — spec §30.7/§30.12) — endpoint Staff feedback cho AI classification.
/// Insert flow (AI populate) do Sprint AI (aibeiotrealtime.md); đây là feedback loop cho precision/recall.
/// </summary>
[ApiController]
[Route("api/v1/anomaly-classifications")]
[Produces("application/json")]
public class AnomalyClassificationsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ICurrentUserService _currentUser;

    public AnomalyClassificationsController(IMediator mediator, ICurrentUserService currentUser)
    {
        _mediator = mediator;
        _currentUser = currentUser;
    }

    /// <summary>
    /// BE-AI — GET danh sách classification của 1 pin (AI populate qua SohPredictionBackgroundService).
    /// Dùng cho FE hiển thị lịch sử phân loại + gắn nút feedback.
    /// </summary>
    /// <param name="query">Filter: <c>batteryAssetId</c> (bắt buộc), <c>classification</c>, <c>from/to</c>, phân trang.</param>
    /// <param name="ct">Token hủy request.</param>
    [HttpGet]
    [Authorize(Roles = "Admin,Manager,Staff")]
    [ProducesResponseType(typeof(CommonResponse<PaginationResponse<AnomalyClassificationDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetList([FromQuery] GetAnomalyClassificationsQuery query, CancellationToken ct)
    {
        var result = await _mediator.Send(query, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Sprint Bonus NS-26 (#666, F2) — Staff xác nhận kết quả phân loại AI (Correct/FalsePositive/FalseNegative) cho 1 classification sau khi xử lý — feedback loop đo precision/recall + xuất dữ liệu retrain.
    /// </summary>
    /// <remarks>
    /// Cách dùng: sau khi Staff resolve ticket/alert có classification AI (Normal/Degrading/Failed), UI hỏi "AI phân loại có đúng không?" → gửi feedback về classification tương ứng.
    ///
    /// Body:
    /// - <c>feedback</c> (<c>StaffFeedbackEnum</c>, bắt buộc): <c>1</c> Correct (AI đúng) · <c>2</c> FalsePositive (AI báo bất thường nhưng thực tế bình thường) · <c>3</c> FalseNegative (AI bỏ sót bất thường thật).
    ///
    /// Cách hoạt động:
    /// - <c>id</c> lấy từ route; <c>staffFeedbackByUserId</c> lấy từ token (client KHÔNG set được — chống mạo danh).
    /// - Ghi <c>staffFeedback</c> + <c>staffFeedbackByUserId</c> + <c>staffFeedbackAt = UtcNow</c> vào bản ghi <c>AnomalyClassification</c>.
    /// - Trả về <c>AnomalyClassificationDto</c> đã cập nhật (đủ score/confidence/latency/modelVersion + feedback).
    ///
    /// Lưu ý: bảng <c>anomaly_classifications</c> được AI populate ở luồng Sprint AI (chưa chạy) — hiện tại chỉ có endpoint feedback này. Enum <c>Classification</c>: 1 Normal / 2 Degrading / 3 Failed.
    /// </remarks>
    /// <param name="id">Id của bản ghi <c>AnomalyClassification</c> (route).</param>
    /// <param name="body">Body chứa <c>feedback</c> (<c>StaffFeedbackEnum</c>). User id lấy từ token, không nhận từ body.</param>
    /// <param name="ct">Token hủy request.</param>
    /// <returns><see cref="CommonResponse{T}"/> chứa <see cref="AnomalyClassificationDto"/> đã cập nhật feedback.</returns>
    /// <response code="200">Ghi nhận feedback thành công — trả classification đã cập nhật.</response>
    /// <response code="400"><c>feedback</c> không thuộc {1,2,3} hoặc <c>id</c> rỗng (field-level <c>listErrors</c>).</response>
    /// <response code="401">Chưa đăng nhập.</response>
    /// <response code="403">Không có role Admin/Manager/Staff.</response>
    /// <response code="404">Classification không tồn tại (message-only, <c>listErrors</c> null).</response>
    [HttpPost("{id:guid}/feedback")]
    [Authorize(Roles = "Admin,Manager,Staff")]
    [ProducesResponseType(typeof(CommonResponse<AnomalyClassificationDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(CommonResponse<AnomalyClassificationDto>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(CommonResponse<AnomalyClassificationDto>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> SubmitFeedback(Guid id, [FromBody] AnomalyClassificationFeedbackRequest body, CancellationToken ct)
    {
        var actor = Guid.TryParse(_currentUser.UserId, out var u) ? u : Guid.Empty;
        var result = await _mediator.Send(new SubmitAnomalyClassificationFeedbackCommand
        {
            Id = id,
            Feedback = body.Feedback,
            StaffFeedbackByUserId = actor
        }, ct);
        return StatusCode(result.StatusCode, result);
    }
}
