using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TicketService.Domain.Entities;

namespace TicketService.Application.Interfaces.Services;

/// <summary>
/// Kết quả check quyền cho hành động edit/delete chat — tách riêng "không có quyền" (Forbidden),
/// "có quyền nhưng thiếu reason bắt buộc" (ReasonRequired) và "author nhưng hết edit window"
/// (EditWindowExpired) để handler giữ đúng status code/message hiện có (#515).
/// </summary>
public enum ChatAuthorizationResult
{
    Allowed,
    Forbidden,
    ReasonRequired,
    EditWindowExpired
}

public interface IChatAuthorizationService
{
    Task<bool> CanAccessTicketAsync(Guid ticketId, Guid actorUserId, IReadOnlyCollection<string> actorRoles, CancellationToken cancellationToken = default);
    bool CanViewInternalChats(IReadOnlyCollection<string> actorRoles);

    /// <summary>Overload có xét participant.CanViewInternal (#522) — dùng khi cần check theo ticket cụ thể.</summary>
    Task<bool> CanViewInternalChatsAsync(Guid ticketId, Guid actorUserId, IReadOnlyCollection<string> actorRoles, CancellationToken cancellationToken = default);

    /// <summary>
    /// Permission-aware (#515/#516) — Author trong <paramref name="editWindowMinutes"/> hoặc actor có
    /// permission <c>chat.edit.any</c> (kèm <paramref name="reasonProvided"/>) thì được edit.
    /// </summary>
    ChatAuthorizationResult CanEditChat(
        TicketChat chat,
        Guid actorUserId,
        IReadOnlyCollection<string> actorPermissions,
        bool reasonProvided,
        int editWindowMinutes);

    /// <summary>
    /// Permission-aware (#515/#516) — Author được xóa của mình bất cứ lúc nào; actor có permission
    /// <c>chat.delete.any</c> (kèm <paramref name="reasonProvided"/>) được xóa của người khác.
    /// </summary>
    ChatAuthorizationResult CanDeleteChat(
        TicketChat chat,
        Guid actorUserId,
        IReadOnlyCollection<string> actorPermissions,
        bool reasonProvided);

    /// <summary>
    /// Permission-aware (#515/#516) — yêu cầu permission <c>chat.create.internal</c> khi
    /// <paramref name="isInternal"/>, ngược lại yêu cầu <c>chat.create.public</c>.
    /// </summary>
    bool CanCreateChat(bool isInternal, IReadOnlyCollection<string> actorPermissions);

    /// <summary>Permission-aware (#515/#516) — yêu cầu permission <c>chat.pin</c>.</summary>
    bool CanPinChat(IReadOnlyCollection<string> actorPermissions);

    /// <summary>
    /// Permission-aware (#515/#516) — yêu cầu permission <c>chat.view.internal</c>.
    /// CHƯA có call site — Read/Query layer của chat dùng <see cref="CanViewInternalChatsAsync"/>
    /// (role + participant.CanViewInternal, từ #522). Permission system hiện tại resolve 1-1 theo
    /// Role (xem <c>AuthService.PermissionResolver</c> — không có per-account override), và
    /// <c>chat.view.internal</c> seed đúng cho cùng 3 role (Admin/Manager/Staff) mà
    /// <c>CanViewInternalChats(actorRoles)</c> đã check — wire method này vào Query layer hiện tại
    /// sẽ KHÔNG đổi bất kỳ hành vi thực tế nào, chỉ có giá trị nếu sau này hệ thống hỗ trợ gán
    /// permission riêng theo account (chưa có trong roadmap — xem `logs/sprint-chat-checklist.md`).
    /// </summary>
    bool CanViewInternalChat(IReadOnlyCollection<string> actorPermissions);
}
