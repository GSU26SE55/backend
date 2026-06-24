namespace BatteryService.Domain.Enums;

/// <summary>
/// Sprint 7 B4 (§31.7) — cách đấu nối điện của pin trong site, dùng để đánh giá
/// rủi ro lan truyền (cascade risk) khi 1 pin hỏng.
/// </summary>
public enum ElectricalTopologyEnum
{
    /// <summary>Pin đơn lẻ, không kết nối điện với pin khác — hỏng không lây.</summary>
    Independent = 1,

    /// <summary>Mắc nối tiếp (string voltage) — mất 1 pin có thể ngắt cả string.</summary>
    SeriesString = 2,

    /// <summary>Mắc song song (bank capacity) — 1 pin hỏng tăng tải pin còn lại.</summary>
    ParallelBank = 3,

    /// <summary>Hỗn hợp nối tiếp + song song.</summary>
    SeriesParallel = 4
}
