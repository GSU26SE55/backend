using System.Collections.Concurrent;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NotificationService.Application.Services;
using NotificationService.Domain.Enums;
using NotificationService.Infrastructure.Services;
using SharedContracts.Interfaces;

namespace NotificationService.UnitTests.Services;

/// <summary>
/// Sprint 6.3 NOTI3-06 (#706) — hạn mức per-user.
///
/// Mục tiêu của hạn mức là giữ cho kênh thông báo còn đáng tin: một pin lỗi dao động quanh ngưỡng
/// có thể sinh hàng chục cảnh báo/giờ, người dùng sẽ tắt thông báo, và khi đó cảnh báo P1 thật
/// cũng không tới được nữa.
/// </summary>
public class NotificationRateLimiterTests
{
    /// <summary>Cache in-memory mô phỏng <c>INCR</c> atomic của Redis.</summary>
    private sealed class FakeCache : ICacheService
    {
        private readonly ConcurrentDictionary<string, long> _counters = new();
        public bool ThrowOnIncrement { get; set; }

        public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
        {
            if (!_counters.TryGetValue(key, out var value))
                return Task.FromResult<T?>(default);

            // Chỉ dùng cho long/long? — đủ cho rate limiter.
            return Task.FromResult((T?)(object)value);
        }

        public Task SetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<bool> TrySetIfNotExistsAsync(string key, string value, TimeSpan expiration, CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task<long> IncrementAsync(string key, TimeSpan expiration, CancellationToken cancellationToken = default)
        {
            if (ThrowOnIncrement)
                throw new InvalidOperationException("Redis down");

            return Task.FromResult(_counters.AddOrUpdate(key, 1, (_, v) => v + 1));
        }

        public Task<long?> GetCounterAsync(string key, CancellationToken cancellationToken = default)
            => Task.FromResult(_counters.TryGetValue(key, out var value) ? (long?)value : null);

        public Task<bool> TryRefreshLeaseAsync(string key, string ownerToken, TimeSpan expiration, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public Task<bool> TryReleaseLeaseAsync(string key, string ownerToken, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }
    }

    private static NotificationRateLimiter Sut(NotificationRateLimitOptions options, ICacheService? cache = null) =>
        new(cache ?? new FakeCache(),
            Options.Create(options),
            NullLogger<NotificationRateLimiter>.Instance);

    [Fact]
    public async Task UnderLimit_Allows()
    {
        var sut = Sut(new NotificationRateLimitOptions { MaxPerUserPerHour = 5, MaxPerUserPerType = 5 });
        var userId = Guid.NewGuid();

        for (var i = 0; i < 5; i++)
        {
            var decision = await sut.TryConsumeAsync(userId, NotificationTypeEnum.TicketCreated);
            decision.Allowed.Should().BeTrue($"lần thứ {i + 1} vẫn trong hạn mức 5");
        }
    }

    [Fact]
    public async Task OverHourlyLimit_IsRejected_WithPerHourReason()
    {
        var sut = Sut(new NotificationRateLimitOptions { MaxPerUserPerHour = 3, MaxPerUserPerType = 100 });
        var userId = Guid.NewGuid();

        for (var i = 0; i < 3; i++)
            (await sut.TryConsumeAsync(userId, NotificationTypeEnum.TicketCreated)).Allowed.Should().BeTrue();

        var decision = await sut.TryConsumeAsync(userId, NotificationTypeEnum.TicketCreated);

        decision.Allowed.Should().BeFalse();
        decision.Reason.Should().Be("per_hour");
    }

    /// <summary>Một loại sự kiện lặp lại không được chiếm hết hạn mức chung và che mất loại khác.</summary>
    [Fact]
    public async Task OverPerTypeLimit_IsRejected_WithPerTypeReason()
    {
        var sut = Sut(new NotificationRateLimitOptions { MaxPerUserPerHour = 100, MaxPerUserPerType = 2 });
        var userId = Guid.NewGuid();

        await sut.TryConsumeAsync(userId, NotificationTypeEnum.BatteryAnomalyWarning);
        await sut.TryConsumeAsync(userId, NotificationTypeEnum.BatteryAnomalyWarning);

        var decision = await sut.TryConsumeAsync(userId, NotificationTypeEnum.BatteryAnomalyWarning);

        decision.Allowed.Should().BeFalse();
        decision.Reason.Should().Be("per_type");
    }

    /// <summary>Chạm trần loại A không được chặn loại B — đây chính là lý do có hai trần.</summary>
    [Fact]
    public async Task PerTypeLimit_DoesNotBlockOtherTypes()
    {
        var sut = Sut(new NotificationRateLimitOptions { MaxPerUserPerHour = 100, MaxPerUserPerType = 1 });
        var userId = Guid.NewGuid();

        await sut.TryConsumeAsync(userId, NotificationTypeEnum.BatteryAnomalyWarning);
        (await sut.TryConsumeAsync(userId, NotificationTypeEnum.BatteryAnomalyWarning)).Allowed.Should().BeFalse();

        (await sut.TryConsumeAsync(userId, NotificationTypeEnum.TicketCreated)).Allowed
            .Should().BeTrue("loại khác vẫn còn hạn mức riêng");
    }

    /// <summary>Hạn mức tính riêng từng người — người này gửi nhiều không được ảnh hưởng người kia.</summary>
    [Fact]
    public async Task LimitIsPerUser()
    {
        var sut = Sut(new NotificationRateLimitOptions { MaxPerUserPerHour = 1, MaxPerUserPerType = 1 });
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();

        await sut.TryConsumeAsync(userA, NotificationTypeEnum.TicketCreated);
        (await sut.TryConsumeAsync(userA, NotificationTypeEnum.TicketCreated)).Allowed.Should().BeFalse();

        (await sut.TryConsumeAsync(userB, NotificationTypeEnum.TicketCreated)).Allowed.Should().BeTrue();
    }

    [Fact]
    public async Task Disabled_AlwaysAllows()
    {
        var sut = Sut(new NotificationRateLimitOptions { Enabled = false, MaxPerUserPerHour = 1, MaxPerUserPerType = 1 });
        var userId = Guid.NewGuid();

        for (var i = 0; i < 50; i++)
            (await sut.TryConsumeAsync(userId, NotificationTypeEnum.TicketCreated)).Allowed.Should().BeTrue();
    }

    /// <summary>
    /// Fail-open có chủ đích: Redis chết mà chặn hết notification thì một sự cố hạ tầng phụ trợ
    /// sẽ làm câm luôn cả cảnh báo an toàn.
    /// </summary>
    [Fact]
    public async Task CacheFailure_FailsOpen()
    {
        var cache = new FakeCache { ThrowOnIncrement = true };
        var sut = Sut(new NotificationRateLimitOptions { MaxPerUserPerHour = 1 }, cache);

        var decision = await sut.TryConsumeAsync(Guid.NewGuid(), NotificationTypeEnum.TicketCreated);

        decision.Allowed.Should().BeTrue();
    }

    [Fact]
    public async Task ZeroLimit_MeansUnlimited()
    {
        var sut = Sut(new NotificationRateLimitOptions { MaxPerUserPerHour = 0, MaxPerUserPerType = 0 });
        var userId = Guid.NewGuid();

        for (var i = 0; i < 30; i++)
            (await sut.TryConsumeAsync(userId, NotificationTypeEnum.TicketCreated)).Allowed.Should().BeTrue();
    }
}
