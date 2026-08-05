using FluentAssertions;
using SharedContracts.Interfaces;
using TicketService.Infrastructure.Implements.Services;

namespace TicketService.UnitTests.Services;

public class SpamDetectorTests
{
    [Fact]
    public async Task IsSpamAsync_FirstAndSecondAcceptedPost_ReturnsFalse()
    {
        var detector = new SpamDetector(new InMemoryCacheService());
        var ticketId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        (await detector.IsSpamAsync(ticketId, userId, "same body")).Should().BeFalse();
        await detector.RecordAcceptedMessageAsync(ticketId, userId, "same body");

        (await detector.IsSpamAsync(ticketId, userId, "same body")).Should().BeFalse();
    }

    [Fact]
    public async Task IsSpamAsync_ThirdSameBody_ReturnsTrueWithoutRecordingRejectedAttempt()
    {
        var detector = new SpamDetector(new InMemoryCacheService());
        var ticketId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        await detector.RecordAcceptedMessageAsync(ticketId, userId, "same body");
        await detector.RecordAcceptedMessageAsync(ticketId, userId, "same body");

        (await detector.IsSpamAsync(ticketId, userId, "same body")).Should().BeTrue();
        (await detector.IsSpamAsync(ticketId, userId, "same body")).Should().BeTrue();
    }

    [Fact]
    public async Task Lease_IsReleasedOnlyByItsOwner()
    {
        var detector = new SpamDetector(new InMemoryCacheService());
        var ticketId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var lease = await detector.TryAcquireLeaseAsync(ticketId, userId);

        lease.Should().NotBeNull();
        (await detector.TryAcquireLeaseAsync(ticketId, userId)).Should().BeNull();
        (await detector.RenewLeaseAsync(lease!)).Should().BeTrue();
        await detector.ReleaseLeaseAsync(lease!);
        (await detector.TryAcquireLeaseAsync(ticketId, userId)).Should().NotBeNull();
    }

    private sealed class InMemoryCacheService : ICacheService
    {
        private readonly Dictionary<string, object> _store = new();

        public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
            => Task.FromResult(_store.TryGetValue(key, out var value) ? (T?)value : default);

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

        public Task<bool> TrySetIfNotExistsAsync(string key, string value, TimeSpan expiration, CancellationToken cancellationToken = default)
        {
            if (_store.ContainsKey(key))
                return Task.FromResult(false);

            _store[key] = value;
            return Task.FromResult(true);
        }

        public Task<long> IncrementAsync(string key, TimeSpan expiration, CancellationToken cancellationToken = default)
        {
            var next = _store.TryGetValue(key, out var value) && value is long count ? count + 1 : 1;
            _store[key] = next;
            return Task.FromResult(next);
        }

        public Task<long?> GetCounterAsync(string key, CancellationToken cancellationToken = default)
            => Task.FromResult(_store.TryGetValue(key, out var value) && value is long count
                ? (long?)count
                : null);

        public Task<bool> TryRefreshLeaseAsync(string key, string ownerToken, TimeSpan expiration, CancellationToken cancellationToken = default)
            => Task.FromResult(_store.TryGetValue(key, out var value) && string.Equals(value as string, ownerToken, StringComparison.Ordinal));

        public Task<bool> TryReleaseLeaseAsync(string key, string ownerToken, CancellationToken cancellationToken = default)
        {
            if (!_store.TryGetValue(key, out var value) || !string.Equals(value as string, ownerToken, StringComparison.Ordinal))
                return Task.FromResult(false);

            _store.Remove(key);
            return Task.FromResult(true);
        }
    }
}
