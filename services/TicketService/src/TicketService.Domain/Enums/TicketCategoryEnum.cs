namespace TicketService.Domain.Enums;

/// <summary>
/// Phân loại lỗi/yêu cầu của Ticket.
/// </summary>
public enum TicketCategoryEnum
{
    /// <summary>Lỗi sạc.</summary>
    Charging = 1,
    /// <summary>Quá nhiệt.</summary>
    Overheat = 2,
    /// <summary>Mất nguồn.</summary>
    NoPower = 3,
    /// <summary>Hiệu năng kém.</summary>
    Performance = 4,
    /// <summary>Khác.</summary>
    Other = 5,
    /// <summary>Yêu cầu sửa chữa.</summary>
    Repair = 6
}
