using BatteryService.Application.Interfaces;

namespace BatteryService.UnitTests.Helpers;

/// <summary>
/// IOT3-29 — test double cho <see cref="IMqttPasswordFileSync"/>.
/// </summary>
/// <remarks>
/// <para>
/// Phần lớn bài test không quan tâm tới việc ghi file <c>passwd</c>; chúng chỉ cần handler chạy
/// được. Bản mặc định vì thế không làm gì cả và <see cref="CallCount"/> cho bài nào cần thì đếm.
/// </para>
/// <para>
/// <see cref="Throwing"/> dựng bản LUÔN NÉM để chốt bất biến: đồng bộ hỏng KHÔNG được làm
/// provision/create/rotate thất bại — thiết bị vẫn phải dùng được đường HTTPS.
/// </para>
/// </remarks>
public sealed class NoopMqttPasswordFileSync : IMqttPasswordFileSync
{
    private readonly bool _throws;

    private NoopMqttPasswordFileSync(bool throws) => _throws = throws;

    public static NoopMqttPasswordFileSync Instance() => new(false);

    /// <summary>Bản luôn ném — dùng để chứng minh lời gọi đã được bọc try-catch.</summary>
    public static NoopMqttPasswordFileSync Throwing() => new(true);

    public int CallCount { get; private set; }

    public Task SyncOnceAsync(CancellationToken ct)
    {
        CallCount++;
        if (_throws)
            throw new IOException("test: mount read-only");
        return Task.CompletedTask;
    }
}
