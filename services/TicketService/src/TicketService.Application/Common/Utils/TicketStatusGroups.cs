using TicketService.Domain.Enums;

namespace TicketService.Application.Common.Utils;

public static class TicketStatusGroups
{
    public static readonly TicketStatusEnum[] Terminal =
    {
        TicketStatusEnum.Closed,
        TicketStatusEnum.ClosedRejected
    };

    public static readonly TicketStatusEnum[] ResolvedGroup =
    {
        TicketStatusEnum.Completed,
        TicketStatusEnum.Closed
    };

    public static readonly TicketStatusEnum[] SlaMonitored =
    {
        TicketStatusEnum.InProgress
    };

    public static readonly TicketStatusEnum[] Active =
    {
        TicketStatusEnum.Open,
        TicketStatusEnum.Pending,
        TicketStatusEnum.InProgress,
        TicketStatusEnum.Request,
        TicketStatusEnum.ReAssign
    };
}
