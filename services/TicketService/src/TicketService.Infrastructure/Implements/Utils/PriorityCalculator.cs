using TicketService.Application.Interfaces.Utils;
using TicketService.Domain.Enums;

namespace TicketService.Infrastructure.Implements.Utils;

public class PriorityCalculator : IPriorityCalculator
{
    public TicketPriorityEnum Calculate(ImpactScopeEnum impact, UrgencyLevelEnum urgency)
    {
        // Ma trận theo User Guide §3.9 "Cách hệ thống tính mức ưu tiên":
        //
        //   Phạm vi \ Độ khẩn   Low   Medium   High
        //   MultiSite            P1     P1      P1
        //   Site                 P3     P2      P1
        //   SingleAsset          P3     P3      P2

        // MultiSite (3) — LUÔN P1 bất kể độ khẩn cấp: nhiều trạm cùng lỗi thì nguyên nhân
        // nằm ở hệ thống chứ không phải một viên pin, nên xếp mức cao nhất ngay từ đầu.
        if (impact == ImpactScopeEnum.MultiSite)
            return TicketPriorityEnum.P1Critical;

        // Site (2)
        if (impact == ImpactScopeEnum.Site)
        {
            if (urgency == UrgencyLevelEnum.High)
                return TicketPriorityEnum.P1Critical;
            // Low → P3 (không phải P2): cả trạm bị ảnh hưởng nhưng không gấp thì vẫn
            // xử lý theo lịch thường.
            return urgency == UrgencyLevelEnum.Medium
                ? TicketPriorityEnum.P2High
                : TicketPriorityEnum.P3Normal;
        }

        // SingleAsset (1)
        if (impact == ImpactScopeEnum.SingleAsset)
        {
            if (urgency == UrgencyLevelEnum.High)
                return TicketPriorityEnum.P2High;
            return TicketPriorityEnum.P3Normal;
        }

        return TicketPriorityEnum.P3Normal;
    }
}
