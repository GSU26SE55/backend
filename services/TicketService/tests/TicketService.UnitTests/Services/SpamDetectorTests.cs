using SharedContracts.Interfaces;
using TicketService.Infrastructure.Implements.Services;

namespace TicketService.UnitTests.Services;

public class SpamDetectorTests
{
    [Fact]
    public async Task IsSpamAsync_FirstAndSecondPost_ReturnsFalse()
    {
        var ticketId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var detector = new SpamDetector(new InMemoryCacheService());

        var first = await detector.IsSpamAsync(ticketId, userId, "Hello", CancellationToken.None);
        var second = await detector.IsSpamAsync(ticketId, userId, "Hello", CancellationToken.None);

        first.Should().BeFalse();
        second.Should().BeFalse();
    }

    [Fact]
    public async Task IsSpamAsync_SameBodyThirdTimeWithinWindow_ReturnsTrue()
    {
        var ticketId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var detector = new SpamDetector(new InMemoryCacheService());

        await detector.IsSpamAsync(ticketId, userId, "Same body", CancellationToken.None);
        await detector.IsSpamAsync(ticketId, userId, "Same body", CancellationToken.None);
        var third = await detector.IsSpamAsync(ticketId, userId, "Same body", CancellationToken.None);

        third.Should().BeTrue();
    }

    [Fact]
    public async Task IsSpamAsync_DifferentBody_ReturnsFalse()
    {
        var ticketId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var detector = new SpamDetector(new InMemoryCacheService());

        await detector.IsSpamAsync(ticketId, userId, "Message 1", CancellationToken.None);
        await detector.IsSpamAsync(ticketId, userId, "Message 2", CancellationToken.None);
        var result = await detector.IsSpamAsync(ticketId, userId, "Message 3", CancellationToken.None);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task IsSpamAsync_DifferentTicketSameUserSameBody_ReturnsFalse()
    {
        var userId = Guid.NewGuid();
        var detector = new SpamDetector(new InMemoryCacheService());

        await detector.IsSpamAsync(Guid.NewGuid(), userId, "Same body", CancellationToken.None);
        await detector.IsSpamAsync(Guid.NewGuid(), userId, "Same body", CancellationToken.None);
        var result = await detector.IsSpamAsync(Guid.NewGuid(), userId, "Same body", CancellationToken.None);

        result.Should().BeFalse();
    }

    /// <summary>In-memory fake (không dùng Redis thật) — đủ để test sliding-window logic của SpamDetector.</summary>
    private sealed class InMemoryCacheService : ICacheService
    {
        private readonly Dictionary<string, object> _store = new();

        public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_store.TryGetValue(key, out var value) ? (T?)value : default);
        }

        public Task SetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken cancellationToken = default)
        {
            _store[key] = value!;
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
        {
            _store.Remove(key);
            return Task.CompletedTask;
        }

        /// <summary>Sprint 6.3 NOTI3-09 (#709) — mô phỏng SET NX (không TTL, đủ cho unit test).</summary>
        public Task<bool> TrySetIfNotExistsAsync(
            string key, string value, TimeSpan expiration, CancellationToken cancellationToken = default)
        {
            if (_store.ContainsKey(key))
                return Task.FromResult(false);

            _store[key] = value;
            return Task.FromResult(true);
        }

        /// <summary>Sprint 6.3 NOTI3-06 (#706) — bộ đếm in-memory cho rate limit.</summary>
        public Task<long> IncrementAsync(
            string key, TimeSpan expiration, CancellationToken cancellationToken = default)
        {
            // _store lưu object nên đọc lại phải quy về long thay vì parse chuỗi.
            var next = _store.TryGetValue(key, out var v) && v is long current ? current + 1 : 1L;
            _store[key] = next;
            return Task.FromResult(next);
        }
    }
}
