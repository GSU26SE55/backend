namespace BatteryService.Application.Services;

/// <summary>
/// Đánh thức <c>OutboxRelayBackgroundService</c> ngay khi có event mới, thay vì đợi hết tick.
/// </summary>
/// <remarks>
/// Relay chạy theo `OutboxRelayIntervalSeconds` (5 s). Với cảnh báo môi trường thì 5 s đó là
/// phần chờ LỚN NHẤT còn lại của cả chuỗi: thiết bị gửi → BE chấm ngưỡng ngay trong POST →
/// rồi nằm im chờ lượt quét kế tiếp mới đẩy được event đi.
///
/// <para>Tín hiệu này chỉ để đi NHANH HƠN, không phải để đảm bảo. Timer vẫn chạy song song làm
/// lưới an toàn: tín hiệu lỡ mất (process vừa restart, hàng đợi đầy) thì lượt quét sau vẫn cuốn
/// nốt — nên không có event nào kẹt lại vĩnh viễn.</para>
/// </remarks>
public interface IOutboxSignal
{
    /// <summary>Báo có event mới cần đẩy. Gọi nhiều lần liên tiếp chỉ đánh thức một lượt.</summary>
    void Notify();

    /// <summary>Chờ tới khi có <see cref="Notify"/>, hoặc tới khi <paramref name="ct"/> bị huỷ.</summary>
    Task WaitAsync(CancellationToken ct);
}
