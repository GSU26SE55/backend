namespace BatteryService.Domain.Enums;

/// <summary>
/// Sprint Bonus NS-26 (#666, F2, Q12=A) — output phân loại AI (Isolation Forest + LSTM) theo spec §30.3.
/// Giá trị: <c>1</c> Normal (bình thường) · <c>2</c> Degrading (đang suy giảm) · <c>3</c> Failed (hỏng/EOL).
/// </summary>
public enum AnomalyClassificationEnum
{
    /// <summary>Pin bình thường.</summary>
    Normal = 1,

    /// <summary>Pin đang suy giảm (degrading) — cần theo dõi.</summary>
    Degrading = 2,

    /// <summary>Pin hỏng / end-of-life (SOH &lt; 80%).</summary>
    Failed = 3
}
