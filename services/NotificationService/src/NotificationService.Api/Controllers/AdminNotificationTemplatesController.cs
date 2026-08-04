using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NotificationService.Application.CQRS.Command.NotificationTemplate;
using NotificationService.Application.CQRS.Query.NotificationTemplate;
using NotificationService.Application.DTOs.Response.Notification;

namespace NotificationService.Api.Controllers;

/// <summary>
/// Sprint 6.3 NOTI3-12 (#712) — quản trị template notification.
///
/// Ba việc trước sprint này không làm được:
/// <list type="bullet">
/// <item><b>Xem trước</b> — sửa template xong chỉ biết đúng/sai khi có sự kiện thật xảy ra.</item>
/// <item><b>Gửi thử</b> — kiểm chứng bản dựng thật trong hộp thư, không phải đoán qua HTML.</item>
/// <item><b>Quay lui</b> — bản mới sai chính tả gửi cho hàng trăm khách thì phải sửa tay lại.</item>
/// </list>
///
/// <para><b>02/08/2026 — bổ sung soạn thảo (tạo / sửa / xoá).</b> Trước đó controller chỉ có 4
/// endpoint đọc-và-bật, không có đường nào tạo hay sửa template: nội dung CHỈ đến từ seeder, mà
/// seeder lại idempotent theo cặp (Type × Channel) nên sửa catalog rồi deploy lại cũng KHÔNG ghi đè
/// bản đã có. Hệ quả là mục tiêu của chính tính năng này — "có template trong DB thì người vận hành
/// sửa được ngay, khỏi build lại" — chưa bao giờ đạt được, và toàn bộ cơ chế phiên bản (cột
/// <c>Version</c>, index unique có điều kiện, endpoint <c>activate</c>, nút "Kích hoạt" trên giao
/// diện) là code chết vì không gì tạo ra được phiên bản thứ hai.</para>
/// </summary>
[ApiController]
[Route("api/admin/notification-templates")]
// Quy ước thật đang chạy trong repo là role-based, KHÔNG phải policy: JWT phát ra claim
// `role = "Admin"` (chuỗi), còn policy "AdminOnly" trong SharedInfrastructure đã bị comment
// toàn bộ từ lâu — và định nghĩa cũ của nó (`RequireClaim("Role","1")`) cũng không khớp token
// hiện tại. Dùng `[Authorize(Roles = "Admin")]` như 4 controller admin khác của dự án.
[Authorize(Roles = "Admin")]
[Produces("application/json")]
public class AdminNotificationTemplatesController : ControllerBase
{
    private readonly IMediator _mediator;

    public AdminNotificationTemplatesController(IMediator mediator) => _mediator = mediator;

    /// <summary>Danh sách template có phân trang, lọc theo type/channel.</summary>
    /// <remarks>
    /// **Quyền:** Admin. Mặc định bao gồm cả bản không active để thấy lịch sử phiên bản;
    /// truyền `activeOnly=true` để chỉ lấy bản đang dùng của mỗi cặp.
    ///
    /// Phân trang theo `pageNumber`/`pageSize` (`PaginationRequest` tự kẹp: `pageNumber &lt; 1` → 1,
    /// `pageSize` ngoài khoảng `1..100` → 10 hoặc 100).
    ///
    /// `type`/`channel` trong response trả về dạng **SỐ** — client tự ánh xạ sang nhãn hiển thị.
    /// </remarks>
    /// <response code="200">Trả về một trang template.</response>
    [HttpGet]
    [ProducesResponseType(typeof(NotificationTemplateListResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetList(
        [FromQuery] NotificationTemplateGetListQuery query, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(query, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>Biến dùng được cho từng loại thông báo.</summary>
    /// <remarks>
    /// **Quyền:** Admin. Không chạm DB — trả về hợp đồng tĩnh giữa consumer (bên ghi payload) và
    /// template (bên đọc `{{bien}}`).
    ///
    /// Dùng để trình soạn template gợi ý đúng tên biến. Trước khi có endpoint này người soạn phải
    /// tự đoán, và đoán sai thì Handlebars render ra rỗng chứ không báo lỗi — đó là cách
    /// `{{ticketCode}}` tồn tại hàng tháng trong khi consumer ghi khoá `code`.
    ///
    /// `builtin` giống nhau ở mọi loại; `payload` rỗng nghĩa là consumer của loại đó không ghi
    /// payload nên chỉ dùng được `builtin`.
    /// </remarks>
    /// <response code="200">Trả về danh mục biến theo từng loại.</response>
    [HttpGet("variables")]
    [ProducesResponseType(typeof(NotificationTemplateVariableListResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetVariables(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new NotificationTemplateVariableListQuery(), cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>Độ phủ template so với thông báo thật đã sinh.</summary>
    /// <remarks>
    /// **Quyền:** Admin. Gom mọi cặp (loại × kênh) **đã từng sinh thông báo thật** rồi đối chiếu với
    /// template đang hoạt động.
    ///
    /// - `hasActiveTemplate = false` ⇒ mọi thông báo của cặp đó đang dùng chuỗi hardcode trong
    ///   consumer; muốn sửa câu chữ phải sửa code rồi deploy lại.
    /// - `unknownVariables` không rỗng ⇒ template có tồn tại nhưng đang gọi biến không có trong dữ
    ///   liệu, chỗ đó sẽ render ra rỗng.
    ///
    /// Sắp xếp sẵn: thiếu template lên đầu, rồi tới template có biến hỏng, rồi theo lượng thông báo.
    /// </remarks>
    /// <response code="200">Trả về bảng độ phủ.</response>
    [HttpGet("coverage")]
    [ProducesResponseType(typeof(NotificationTemplateCoverageResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCoverage(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new NotificationTemplateCoverageQuery(), cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>Chi tiết một template theo Id (kể cả bản không active).</summary>
    /// <response code="200">Trả về template.</response>
    /// <response code="404">Không tìm thấy template.</response>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(NotificationTemplateResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(NotificationTemplateResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new NotificationTemplateGetByIdQuery { Id = id }, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>Tạo template đầu tiên cho một cặp (Type × Channel) chưa có.</summary>
    /// <remarks>
    /// Cặp đã có template thì trả **409** — dùng `PUT` (sửa) để sinh phiên bản mới thay vì ghi đè,
    /// nhờ vậy còn quay lui được khi bản mới sai.
    ///
    /// Cú pháp Handlebars được kiểm ngay lúc lưu: hỏng thì trả **400**. Không kiểm ở đây thì template
    /// hỏng vẫn lưu được, và lúc gửi thật dispatcher lặng lẽ rơi về chuỗi hardcode trong consumer.
    /// </remarks>
    /// <response code="201">Đã tạo, `data` là Id bản mới.</response>
    /// <response code="400">Dữ liệu không hợp lệ hoặc template hỏng cú pháp.</response>
    /// <response code="409">Cặp (Type × Channel) đã có template.</response>
    [HttpPost]
    [ProducesResponseType(typeof(NotificationTemplateActionResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(NotificationTemplateActionResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(NotificationTemplateActionResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(
        [FromBody] NotificationTemplateCreateCommand command, CancellationToken cancellationToken)
    {
        command.ActorUserId = GetActorUserId();
        var result = await _mediator.Send(command, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>Sửa nội dung: sinh **phiên bản mới** rồi bật lên, không ghi đè bản cũ.</summary>
    /// <remarks>
    /// `id` là bản bất kỳ của cặp cần sửa — Type/Channel lấy từ nó, KHÔNG nhận từ body để không ai
    /// biến bản ghi này thành template của một cặp khác và phá chuỗi phiên bản của cả hai cặp.
    /// </remarks>
    /// <response code="200">Đã tạo phiên bản mới và bật lên, `data` là Id bản mới.</response>
    /// <response code="400">Dữ liệu không hợp lệ hoặc template hỏng cú pháp.</response>
    /// <response code="404">Không tìm thấy template.</response>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(NotificationTemplateActionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(NotificationTemplateActionResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(NotificationTemplateActionResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Revise(
        Guid id, [FromBody] NotificationTemplateReviseCommand command, CancellationToken cancellationToken)
    {
        command.Id = id;
        command.ActorUserId = GetActorUserId();
        var result = await _mediator.Send(command, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>Xoá mềm một phiên bản không còn dùng.</summary>
    /// <remarks>
    /// **Không xoá được bản đang dùng** (trả 409): cặp mất bản active thì dispatcher lặng lẽ rơi về
    /// chuỗi hardcode trong consumer — thông báo vẫn gửi nhưng mất nội dung tuỳ biến. Muốn bỏ bản
    /// đang dùng thì kích hoạt một phiên bản khác trước.
    /// </remarks>
    /// <response code="200">Đã xoá.</response>
    /// <response code="404">Không tìm thấy template.</response>
    /// <response code="409">Đang là bản active.</response>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(typeof(NotificationTemplateActionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(NotificationTemplateActionResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(NotificationTemplateActionResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var command = new NotificationTemplateDeleteCommand { Id = id, ActorUserId = GetActorUserId() };
        var result = await _mediator.Send(command, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>Dựng thử template với dữ liệu mẫu — **KHÔNG gửi đi đâu cả**.</summary>
    /// <remarks>
    /// Trả `title`/`body` sau khi render. Placeholder không có trong `sampleData` sẽ rỗng — đó chính
    /// là cách phát hiện template gọi tên biến sai.
    /// </remarks>
    /// <response code="200">Render thành công.</response>
    /// <response code="400">Template hỏng cú pháp Handlebars.</response>
    /// <response code="404">Không tìm thấy template.</response>
    [HttpPost("{id:guid}/preview")]
    [ProducesResponseType(typeof(NotificationTemplatePreviewResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(NotificationTemplatePreviewResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(NotificationTemplatePreviewResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Preview(
        Guid id, [FromBody] NotificationTemplatePreviewQuery? query, CancellationToken cancellationToken)
    {
        var request = query ?? new NotificationTemplatePreviewQuery();
        request.Id = id;
        var result = await _mediator.Send(request, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>Gửi thử template tới **chính admin đang đăng nhập**.</summary>
    /// <remarks>
    /// **Không nhận địa chỉ tự do** (R-46): endpoint nhận địa chỉ tuỳ ý sẽ biến hệ thống thành cổng
    /// gửi thư rác có xác thực. Địa chỉ nhận LUÔN lấy từ danh tính người gọi.
    ///
    /// Giới hạn **5 lần/giờ mỗi admin**, ghi audit mỗi lần. Chỉ hỗ trợ template kênh **Email**.
    /// </remarks>
    /// <response code="200">Đã xếp hàng gửi tới email của admin.</response>
    /// <response code="400">Template không phải kênh Email, hoặc admin chưa có email.</response>
    /// <response code="404">Không tìm thấy template.</response>
    /// <response code="429">Vượt 5 lần/giờ.</response>
    [HttpPost("{id:guid}/test-send")]
    [ProducesResponseType(typeof(NotificationTemplateTestSendResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(NotificationTemplateTestSendResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(NotificationTemplateTestSendResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(NotificationTemplateTestSendResponse), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> TestSend(
        Guid id, [FromBody] NotificationTemplateTestSendCommand? command, CancellationToken cancellationToken)
    {
        var request = command ?? new NotificationTemplateTestSendCommand();
        request.Id = id;
        request.ActorUserId = GetActorUserId();
        request.ActorEmailFromClaim = User.FindFirstValue(ClaimTypes.Email) ?? User.FindFirstValue("email");

        var result = await _mediator.Send(request, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>Quay lui: kích hoạt lại một phiên bản template cũ.</summary>
    /// <remarks>
    /// Trong cùng cặp (Type × Channel) chỉ được có đúng một bản active, nên thao tác này tắt bản đang
    /// dùng rồi bật bản được chọn trong **một giao dịch**. Idempotent: bản vốn đã active thì trả 200
    /// và không làm gì.
    /// </remarks>
    /// <response code="200">Đã chuyển sang phiên bản được chọn.</response>
    /// <response code="404">Không tìm thấy template.</response>
    [HttpPost("{id:guid}/activate")]
    [ProducesResponseType(typeof(NotificationTemplateActionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(NotificationTemplateActionResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Activate(Guid id, CancellationToken cancellationToken)
    {
        var command = new NotificationTemplateActivateCommand { Id = id, ActorUserId = GetActorUserId() };
        var result = await _mediator.Send(command, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Danh tính người thực hiện, lấy từ JWT. Trả <c>Guid.Empty</c> khi không đọc được — command tự
    /// từ chối ở <c>ValidateAsync</c> để lỗi hiện ra dưới dạng 400 có thông báo, thay vì ghi audit
    /// với actor rỗng.
    /// </summary>
    private Guid GetActorUserId()
    {
        var raw = User.FindFirstValue("UserId") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(raw, out var userId) ? userId : Guid.Empty;
    }
}
