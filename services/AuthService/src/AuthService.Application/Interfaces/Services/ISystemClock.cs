namespace AuthService.Application.Interfaces.Services;

/// <summary>
/// #AUTH-38: abstraction cho time để handler/test có thể mock được "thời điểm hiện tại".
///
/// Production: <see cref="UtcNow"/> trả về <see cref="DateTime.UtcNow"/>.
/// Test: cung cấp <c>FakeSystemClock</c> trong fixture để khoá thời gian.
///
/// Migration plan: handlers mới TRỰC TIẾP dùng <see cref="ISystemClock"/>. Handler cũ chuyển dần
/// theo từng PR — không refactor đồng loạt để tránh diff rộng. <see cref="DateTime.UtcNow"/> vẫn
/// hoạt động cho code chưa migrate.
/// </summary>
public interface ISystemClock
{
    /// <summary>Thời điểm hiện tại tính theo UTC.</summary>
    DateTime UtcNow { get; }
}

/// <summary>Production implementation — wrap <see cref="DateTime.UtcNow"/>.</summary>
public sealed class SystemClock : ISystemClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}
