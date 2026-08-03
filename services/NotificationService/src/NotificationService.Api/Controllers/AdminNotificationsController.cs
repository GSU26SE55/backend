using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NotificationService.Application.CQRS.Command.Notification;
using NotificationService.Application.CQRS.Query.Notification;
using NotificationService.Application.DTOs.Response.Notification;

namespace NotificationService.Api.Controllers;

/// <summary>
/// Sprint 6.4 NOTI4-07/09 — gửi thông báo hàng loạt và tra lịch sử gửi.
///
/// <para>Trước sprint này endpoint gửi tay (<c>POST /api/notifications</c>) nhận đúng <b>một</b>
/// <c>UserId</c>; muốn báo cho 20 người thì bấm 20 lần, và 20 lần đó không có gì nối lại thành một
/// sự kiện để mà thống kê hay thu hồi.</para>
/// </summary>
[ApiController]
[Route("api/admin/notifications")]
[Authorize(Roles = "Admin")]
[Produces("application/json")]
public class AdminNotificationsController : ControllerBase
{
    private readonly IMediator _mediator;

    public AdminNotificationsController(IMediator mediator) => _mediator = mediator;

    /// <summary>Xem trước số người nhận — **không gửi gì**.</summary>
    /// <remarks>
    /// **Quyền:** Admin.
    ///
    /// Dùng trước khi bấm gửi. Con số trả về là **sau khi gom trùng**: cộng `memberCount` của từng
    /// nhóm ở phía client sẽ **sai** khi các nhóm giao nhau — người ở hai nhóm bị đếm hai lần.
    ///
    /// So `recipientCount` với `rawCount` để biết các nhóm có giao nhau không: `rawCount` lớn hơn
    /// nghĩa là có người trùng giữa các nhóm.
    ///
    /// Endpoint này dùng **đúng đoạn logic** của lần gửi thật, nên hai con số không thể lệch nhau.
    /// </remarks>
    /// <response code="200">Trả về số người nhận dự kiến.</response>
    [HttpPost("broadcast/preview")]
    [ProducesResponseType(typeof(NotificationBroadcastPreviewResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> PreviewBroadcast(
        [FromBody] NotificationBroadcastPreviewQuery query, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(query, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>Xem trước nội dung theo từng kênh khi bật "dùng mẫu" — **không gửi gì**.</summary>
    /// <remarks>
    /// **Quyền:** Admin. *(MỚI 03/08/2026)*
    ///
    /// Trả về **một dòng cho mỗi kênh**, vì mẫu được khoá theo cặp `(Loại × Kênh)` và bản SMS được
    /// nén ngắn lại (tính tiền theo đoạn) — cùng một lần gửi 3 kênh cho ra 3 nội dung khác nhau. Một
    /// ô xem trước duy nhất sẽ nói dối về 2 trong 3 kênh.
    ///
    /// Model dựng theo **đúng khuôn** `NotificationDispatcher.BuildTemplateModel`, nên nội dung ở
    /// đây bằng đúng nội dung lúc gửi thật. Đây là bài học đắt nhất của Sprint 6.5: màn hình xem
    /// trước cũ nhận dữ liệu mẫu do client tự gõ nên "xem trước thấy đúng nhưng gửi đi lại khác".
    ///
    /// | Trường | Ý nghĩa |
    /// |---|---|
    /// | `hasTemplate = false` | Cặp này không có mẫu ⇒ kênh đó dùng tiêu đề/nội dung admin gõ |
    /// | `missingVariables` | Mẫu gọi biến mà payload không có giá trị ⇒ chỗ đó render ra rỗng |
    /// | `renderError` | Mẫu hỏng cú pháp ⇒ lúc gửi thật sẽ rơi về nội dung dự phòng |
    /// </remarks>
    /// <response code="200">Trả về nội dung dự kiến của từng kênh.</response>
    /// <response code="400">Chưa chọn kênh nào.</response>
    [HttpPost("broadcast/template-preview")]
    [ProducesResponseType(typeof(NotificationBroadcastTemplatePreviewResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(NotificationBroadcastTemplatePreviewResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> PreviewBroadcastTemplate(
        [FromBody] NotificationBroadcastTemplatePreviewQuery query, CancellationToken cancellationToken)
    {
        query.ActorUserId = GetActorUserId();
        var result = await _mediator.Send(query, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>Gửi một thông báo cho nhiều nhóm và/hoặc nhiều cá nhân.</summary>
    /// <remarks>
    /// **Quyền:** Admin.
    ///
    /// Cho phép **trộn** nhóm với cá nhân: "gửi cho nhóm Quản lý và thêm anh A" là **một** lần gửi.
    /// Người vừa ở nhóm vừa được thêm đích danh chỉ nhận **một** lần.
    ///
    /// Người nhận được lọc theo trạng thái tài khoản — tài khoản đã nghỉ / bị đình chỉ / chưa xác
    /// thực **không** nhận, kể cả khi được chỉ định đích danh trong `userIds`.
    ///
    /// Trả **400** khi không còn người nhận hợp lệ nào, kèm lý do cụ thể, và **không** tạo bản ghi
    /// lần gửi mồ côi. Đây là điểm rút kinh nghiệm trực tiếp: nhánh "không có người nhận → ghi log
    /// rồi lặng lẽ bỏ qua" từng giấu một lỗi nghiêm trọng suốt thời gian dài.
    ///
    /// Thông báo sinh ra ở trạng thái `Pending` và đi qua **đúng đường giao** của mọi thông báo
    /// khác, nên vẫn tôn trọng tuỳ chọn nhận tin và khung giờ yên tĩnh của từng người.
    /// </remarks>
    /// <response code="201">Đã gửi. `data.batchId` dùng để mở màn hình thống kê.</response>
    /// <response code="400">Dữ liệu không hợp lệ, hoặc không còn người nhận hợp lệ nào.</response>
    [HttpPost("broadcast")]
    [ProducesResponseType(typeof(NotificationBroadcastResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(NotificationBroadcastResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Broadcast(
        [FromBody] NotificationBroadcastCommand command, CancellationToken cancellationToken)
    {
        command.ActorUserId = GetActorUserId();
        var result = await _mediator.Send(command, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>Lịch sử các lần gửi, có phân trang.</summary>
    /// <remarks>
    /// **Quyền:** Admin. Sắp xếp mới nhất trước.
    ///
    /// Chỉ hiển thị các lần gửi **từ khi bật tính năng này**. Thông báo cũ hơn không thuộc lần gửi
    /// nào: dữ liệu cũ không mang đủ thông tin để gom lại, và gom theo thời gian là suy đoán đã được
    /// chứng minh là sai — thà thiếu còn hơn bịa ra lần gửi chưa từng tồn tại.
    /// </remarks>
    /// <response code="200">Trả về một trang lịch sử gửi.</response>
    [HttpGet("batches")]
    [ProducesResponseType(typeof(NotificationBatchListResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetBatches(
        [FromQuery] NotificationBatchGetListQuery query, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(query, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>Chi tiết một lần gửi, kèm thống kê đã gửi / đã đọc / thất bại.</summary>
    /// <remarks>
    /// Nhóm đã bị xoá vẫn xuất hiện trong danh sách mục tiêu **kèm đúng tên nó mang lúc được
    /// gửi** — "đã từng gửi cho nhóm này" là sự thật lịch sử, xoá nhóm không làm nó chưa từng xảy ra.
    /// </remarks>
    /// <response code="200">Trả về chi tiết lần gửi.</response>
    /// <response code="404">Không tìm thấy lần gửi.</response>
    [HttpGet("batches/{id:guid}")]
    [ProducesResponseType(typeof(NotificationBatchDetailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(NotificationBatchDetailResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetBatchById(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new NotificationBatchGetByIdQuery { Id = id }, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Danh tính người thực hiện, lấy từ JWT. Trả <c>Guid.Empty</c> khi không đọc được — command tự
    /// từ chối ở <c>ValidateAsync</c> để lỗi hiện ra dưới dạng 400 có thông báo.
    /// </summary>
    private Guid GetActorUserId()
    {
        var raw = User.FindFirstValue("UserId") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(raw, out var userId) ? userId : Guid.Empty;
    }
}
