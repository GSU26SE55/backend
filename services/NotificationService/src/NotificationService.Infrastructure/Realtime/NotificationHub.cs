using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace NotificationService.Infrastructure.Realtime;

/// <summary>
/// Sprint 6.3 NOTI3-13 (#713) — kênh realtime cho feed in-app.
///
/// Clone khuôn <c>TicketChatHub</c> bên TicketService, nhưng đơn giản hơn nhiều: ở đây không có
/// khái niệm "phòng" cần kiểm tra quyền — mỗi người chỉ nhận thông báo của **chính mình**.
///
/// **Nhóm theo user, không nhận tham số từ client:** id nhóm lấy từ claim trong JWT chứ không phải
/// từ đối số mà client truyền lên. Nếu để client tự khai "tôi muốn nghe nhóm của user X" thì bất kỳ
/// ai cũng đọc được thông báo của người khác — kể cả khi đã <c>[Authorize]</c>.
///
/// Client chỉ cần kết nối, không phải gọi thêm phương thức nào: <c>OnConnectedAsync</c> tự ghép nhóm.
/// </summary>
[Authorize]
public class NotificationHub : Hub
{
    private readonly ILogger<NotificationHub> _logger;

    public NotificationHub(ILogger<NotificationHub> logger)
    {
        _logger = logger;
    }

    /// <summary>Tên nhóm SignalR của một người dùng.</summary>
    public static string UserGroup(Guid userId) => $"user:{userId:N}";

    public override async Task OnConnectedAsync()
    {
        var userId = GetCurrentUserId();

        if (userId is null)
        {
            // Đã [Authorize] nên hiếm khi xảy ra; nghĩa là token thiếu claim định danh.
            _logger.LogWarning("[NotificationHub] Kết nối không xác định được UserId — từ chối ghép nhóm.");
            await base.OnConnectedAsync();
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, UserGroup(userId.Value));

        _logger.LogDebug("[NotificationHub] User {UserId} đã kết nối (ConnId {ConnId}).",
            userId, Context.ConnectionId);

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var userId = GetCurrentUserId();

        if (userId is not null)
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, UserGroup(userId.Value));

        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Lấy id người dùng từ claim. Chấp nhận nhiều tên claim vì JWT của hệ thống phát cả
    /// <c>UserId</c> lẫn <c>AccountId</c> tuỳ đường phát hành.
    /// </summary>
    private Guid? GetCurrentUserId()
    {
        var raw = Context.User?.FindFirstValue("UserId")
                  ?? Context.User?.FindFirstValue("AccountId")
                  ?? Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);

        return Guid.TryParse(raw, out var userId) ? userId : null;
    }
}
