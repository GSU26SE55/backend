using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NotificationService.Application.CQRS.Command.NotificationGroup;
using NotificationService.Application.CQRS.Query.NotificationGroup;
using NotificationService.Application.DTOs.Response.Notification;

namespace NotificationService.Api.Controllers;

/// <summary>
/// Sprint 6.4 NOTI4-02/03 — quản trị nhóm người nhận thông báo.
///
/// <para>Trước sprint này hệ thống chỉ gửi được cho <b>đúng một người mỗi lệnh</b>, còn "nhóm" chỉ
/// là 4 chuỗi role viết cứng trong code tại 15 chỗ — không tạo, không sửa, không đặt tên, không
/// nhìn thấy từ giao diện được.</para>
///
/// <para><b>Hai loại nhóm.</b> <c>Static</c> (kind = 1) có thành viên tường minh do admin thêm/bớt.
/// <c>Role</c> (kind = 2) suy ra thành viên lúc gửi từ read-model tài khoản. API này chỉ tạo được
/// nhóm <c>Static</c>; 4 nhóm <c>Role</c> do seeder sinh, đánh dấu <c>isSystem</c> và không
/// sửa/xoá được.</para>
/// </summary>
[ApiController]
[Route("api/admin/notification-groups")]
// Quy ước thật đang chạy trong repo là role-based, KHÔNG phải policy — xem chú thích cùng chủ đề ở
// AdminNotificationTemplatesController.
[Authorize(Roles = "Admin")]
[Produces("application/json")]
public class AdminNotificationGroupsController : ControllerBase
{
    private readonly IMediator _mediator;

    public AdminNotificationGroupsController(IMediator mediator) => _mediator = mediator;

    /// <summary>Danh sách nhóm có phân trang.</summary>
    /// <remarks>
    /// **Quyền:** Admin.
    ///
    /// `memberCount` là **số người nhận thực tế** — đã loại người có tài khoản ngừng hoạt động hoặc
    /// đã xoá. Đây là con số cần nhìn trước khi gửi, không phải số dòng trong bảng thành viên.
    ///
    /// `kind` trả về dạng **SỐ** (1 = Static, 2 = Role); client tự ánh xạ sang nhãn hiển thị.
    /// </remarks>
    /// <response code="200">Trả về một trang nhóm.</response>
    [HttpGet]
    [ProducesResponseType(typeof(NotificationGroupListResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetList(
        [FromQuery] NotificationGroupGetListQuery query, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(query, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>Chi tiết một nhóm, kèm số người nhận thực tế.</summary>
    /// <response code="200">Trả về nhóm.</response>
    /// <response code="404">Không tìm thấy nhóm.</response>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(NotificationGroupResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(NotificationGroupResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new NotificationGroupGetByIdQuery { Id = id }, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>Tạo nhóm mới (luôn là nhóm <c>Static</c>).</summary>
    /// <remarks>
    /// Nhóm `Role` **không tạo được qua API** — 4 nhóm theo vai trò đã được seeder tạo sẵn và phủ đủ
    /// `Admin`/`Manager`/`Staff`/`Customer`.
    ///
    /// Tên nhóm không trùng nhau **không phân biệt hoa-thường**.
    /// </remarks>
    /// <response code="201">Đã tạo. `data` là Id nhóm mới.</response>
    /// <response code="400">Tên trống hoặc quá dài.</response>
    /// <response code="409">Đã có nhóm trùng tên.</response>
    [HttpPost]
    [ProducesResponseType(typeof(NotificationGroupActionResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(NotificationGroupActionResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(NotificationGroupActionResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(
        [FromBody] NotificationGroupCreateCommand command, CancellationToken cancellationToken)
    {
        command.ActorUserId = GetActorUserId();
        var result = await _mediator.Send(command, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>Đổi tên / mô tả một nhóm.</summary>
    /// <remarks>
    /// Không đổi được loại nhóm: đổi `Static` thành `Role` sẽ làm tập người nhận thay đổi hoàn toàn
    /// mà không ai nhận ra. Muốn khác thì tạo nhóm mới.
    /// </remarks>
    /// <response code="200">Đã cập nhật.</response>
    /// <response code="404">Không tìm thấy nhóm.</response>
    /// <response code="409">Nhóm hệ thống, hoặc trùng tên với nhóm khác.</response>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(NotificationGroupActionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(NotificationGroupActionResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(NotificationGroupActionResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(
        Guid id, [FromBody] NotificationGroupUpdateCommand command, CancellationToken cancellationToken)
    {
        command.Id = id;
        command.ActorUserId = GetActorUserId();
        var result = await _mediator.Send(command, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>Xoá mềm một nhóm cùng toàn bộ thành viên của nó.</summary>
    /// <remarks>
    /// **Lịch sử gửi không bị ảnh hưởng** — các lần gửi đã thực hiện nằm ở bảng khác và giữ nguyên.
    ///
    /// Nhóm hệ thống trả **409**: 4 nhóm theo vai trò là chỗ dựa của bộ phân giải người nhận, xoá đi
    /// thì toàn bộ thông báo tự động (SLA, ticket, cảnh báo pin) mất người nhận.
    /// </remarks>
    /// <response code="200">Đã xoá.</response>
    /// <response code="404">Không tìm thấy nhóm.</response>
    /// <response code="409">Nhóm hệ thống không xoá được.</response>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(typeof(NotificationGroupActionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(NotificationGroupActionResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(NotificationGroupActionResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var command = new NotificationGroupDeleteCommand { Id = id, ActorUserId = GetActorUserId() };
        var result = await _mediator.Send(command, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>Thành viên của một nhóm, có phân trang.</summary>
    /// <remarks>
    /// Mặc định trả **cả** người có tài khoản đang ngừng hoạt động, kèm cờ `isActive = false`, để
    /// admin thấy mà dọn. Truyền `activeOnly=true` để chỉ lấy người thực sự nhận được thông báo.
    ///
    /// Với nhóm `Role`, danh sách được **suy ra** từ read-model tài khoản nên `addedAt` luôn `null`.
    /// </remarks>
    /// <response code="200">Trả về một trang thành viên.</response>
    /// <response code="404">Không tìm thấy nhóm.</response>
    [HttpGet("{id:guid}/members")]
    [ProducesResponseType(typeof(NotificationGroupMemberListResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(NotificationGroupMemberListResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMembers(
        Guid id, [FromQuery] NotificationGroupGetMembersQuery query, CancellationToken cancellationToken)
    {
        query.GroupId = id;
        var result = await _mediator.Send(query, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>Thêm nhiều người vào nhóm trong một lệnh.</summary>
    /// <remarks>
    /// **Bỏ qua thay vì báo lỗi cả lô.** Id đã có trong nhóm, hoặc id không tồn tại trong read-model
    /// tài khoản, đều bị bỏ qua và **đếm riêng** trong `data` (`alreadyMembers`, `unknownAccounts`).
    /// Chọn 30 người rồi bị từ chối toàn bộ chỉ vì 1 người trùng là hành vi khó chịu.
    ///
    /// `unknownAccounts` &gt; 0 thường là tài khoản vừa tạo mà snapshot đồng bộ chưa tới — chạy
    /// `POST /api/admin/accounts/resync` bên AuthService rồi thử lại.
    ///
    /// Nhóm `Role` trả **409**.
    /// </remarks>
    /// <response code="200">Đã xử lý. Xem `data` để biết bao nhiêu người thực sự được thêm.</response>
    /// <response code="400">Danh sách rỗng, quá dài, hoặc chứa id rỗng.</response>
    /// <response code="404">Không tìm thấy nhóm.</response>
    /// <response code="409">Nhóm theo vai trò không thêm tay được.</response>
    [HttpPost("{id:guid}/members")]
    [ProducesResponseType(typeof(NotificationGroupAddMembersResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(NotificationGroupAddMembersResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(NotificationGroupAddMembersResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(NotificationGroupAddMembersResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> AddMembers(
        Guid id, [FromBody] NotificationGroupAddMembersCommand command, CancellationToken cancellationToken)
    {
        command.GroupId = id;
        command.ActorUserId = GetActorUserId();
        var result = await _mediator.Send(command, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>Bỏ một người khỏi nhóm.</summary>
    /// <response code="200">Đã bỏ.</response>
    /// <response code="404">Không tìm thấy nhóm, hoặc người này không có trong nhóm.</response>
    /// <response code="409">Nhóm theo vai trò không bỏ tay được.</response>
    [HttpDelete("{id:guid}/members/{userId:guid}")]
    [ProducesResponseType(typeof(NotificationGroupActionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(NotificationGroupActionResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(NotificationGroupActionResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RemoveMember(Guid id, Guid userId, CancellationToken cancellationToken)
    {
        var command = new NotificationGroupRemoveMemberCommand
        {
            GroupId = id,
            UserId = userId,
            ActorUserId = GetActorUserId(),
        };
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
