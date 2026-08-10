using System.Collections.Concurrent;
using SharedInfrastructure.Idempotency;

namespace EmailService.IntegrationTests.Fixtures;

/// <summary>
/// Inbox trong bộ nhớ cho test — GH-764: mô phỏng ĐÚNG vòng đời ba bước của bản Redis, kể cả
/// việc nhả chỗ giữ khi side effect lỗi. Nếu bản giả này cứ đánh dấu "đã xử lý" ngay từ đầu thì
/// test sẽ không bao giờ thấy được lỗi mà issue mô tả.
/// </summary>
public class InMemoryInboxStore : IInboxStore
{
    private enum State { InProgress, Completed }

    private readonly ConcurrentDictionary<string, (State State, string Token)> _entries = new();

    private static string Key(Guid messageId, string consumerName) => $"{consumerName}:{messageId:N}";

    public Task<InboxClaim> TryBeginAsync(
        Guid messageId, string consumerName, CancellationToken cancellationToken = default)
    {
        var key = Key(messageId, consumerName);
        var token = Guid.NewGuid().ToString("N");

        if (_entries.TryAdd(key, (State.InProgress, token)))
            return Task.FromResult(new InboxClaim(InboxClaimStatus.Claimed, token));

        var current = _entries[key];
        return Task.FromResult(current.State == State.Completed
            ? InboxClaim.Completed
            : InboxClaim.Busy);
    }

    public Task CompleteAsync(
        Guid messageId, string consumerName, string token, CancellationToken cancellationToken = default)
    {
        var key = Key(messageId, consumerName);
        // Chỉ chốt khi chỗ giữ vẫn là của người gọi — khớp script Lua bên bản Redis.
        if (_entries.TryGetValue(key, out var current) && current.Token == token)
            _entries[key] = (State.Completed, token);
        return Task.CompletedTask;
    }

    public Task ReleaseAsync(
        Guid messageId, string consumerName, string token, CancellationToken cancellationToken = default)
    {
        var key = Key(messageId, consumerName);
        if (_entries.TryGetValue(key, out var current)
            && current.Token == token
            && current.State == State.InProgress)
        {
            _entries.TryRemove(key, out _);
        }
        return Task.CompletedTask;
    }
}
