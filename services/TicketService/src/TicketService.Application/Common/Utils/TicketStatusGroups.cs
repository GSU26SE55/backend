using TicketService.Domain.Enums;

namespace TicketService.Application.Common.Utils;

/// <summary>
/// Nhóm status dùng chung cho dashboard stats và filter SLA — định nghĩa một nơi duy nhất
/// để các endpoint aggregate không lệch nhau về ngữ nghĩa "mở" / "đã xử lý" / "đang theo dõi SLA".
/// </summary>
public static class TicketStatusGroups
{
    /// <summary>
    /// Nhóm kết thúc vòng đời — ticket không còn được coi là "mở".
    /// ClosedRejected nằm trong nhóm này (không còn mở) nhưng KHÔNG thuộc <see cref="ResolvedGroup"/> (bị từ chối, không phải hoàn tất).
    /// </summary>
    public static readonly TicketStatusEnum[] Terminal =
    {
        TicketStatusEnum.Resolved,
        TicketStatusEnum.ClosedPendingRate,
        TicketStatusEnum.Closed,
        TicketStatusEnum.ClosedRejected
    };

    /// <summary>Nhóm "đã xử lý" theo nghĩa nghiệp vụ — Staff đã resolve và đi tiếp luồng đóng ticket.</summary>
    public static readonly TicketStatusEnum[] ResolvedGroup =
    {
        TicketStatusEnum.Resolved,
        TicketStatusEnum.ClosedPendingRate,
        TicketStatusEnum.Closed
    };

    /// <summary>
    /// Nhóm status nằm trong vòng theo dõi SLA (khớp OPEN_STATUSES của trang SLA Monitor phía FE):
    /// đã gán staff và chưa resolve.
    /// </summary>
    public static readonly TicketStatusEnum[] SlaMonitored =
    {
        TicketStatusEnum.Assigned,
        TicketStatusEnum.InProgress,
        TicketStatusEnum.WaitingCustomer,
        TicketStatusEnum.WaitingParts,
        TicketStatusEnum.WaitingOnsiteSchedule,
        TicketStatusEnum.Escalated
    };
}
