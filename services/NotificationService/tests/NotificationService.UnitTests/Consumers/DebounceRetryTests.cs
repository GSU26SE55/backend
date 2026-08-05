using System.Collections.Concurrent;
using MassTransit.Testing;
using NotificationService.Application.Consumers;
using NotificationService.UnitTests.Helpers;
using SharedContracts.Events;
using SharedContracts.Interfaces;

namespace NotificationService.UnitTests.Consumers;

/// <summary>
/// GH-765 — khoá debounce bị chiếm TRƯỚC khi ghi notification.
///
/// <para>
/// Lỗi gốc: 16 consumer gọi <c>TryBeginByMessageAsync</c> chiếm key Redis 30 phút ngay từ đầu —
/// trước cả khi resolve recipient và trước khi ghi DB. Một lỗi DB/resolver ở lần đầu là mọi lần
/// MassTransit gửi lại trong 30 phút đều thấy key, log "duplicate" rồi return. Notification biến
/// mất hẳn dù broker đã làm đúng phần việc của nó.
/// </para>
/// <para>
/// Các test dưới đây dùng cache GIẢ CÓ TRẠNG THÁI (SET NX + gia hạn + nhả theo dấu sở hữu) chứ
/// không phải mock trả cứng true/false: mock trả cứng sẽ xanh ở cả bản cũ lẫn bản mới, tức là
/// không kiểm được gì cả.
/// </para>
/// </summary>
public class DebounceRetryTests
{
    /// <summary>Cache trong bộ nhớ bám đúng ngữ nghĩa Redis mà debounce dựa vào.</summary>
    private sealed class StatefulCache : ICacheService
    {
        private readonly ConcurrentDictionary<string, string> _entries = new();

        public int Claims { get; private set; }
        public int Releases { get; private set; }
        public int Refreshes { get; private set; }

        public Task<bool> TrySetIfNotExistsAsync(
            string key, string value, TimeSpan expiration, CancellationToken cancellationToken = default)
        {
            Claims++;
            return Task.FromResult(_entries.TryAdd(key, value));
        }

        public Task<bool> TryReleaseLeaseAsync(string key, string ownerToken, CancellationToken cancellationToken = default)
        {
            Releases++;
            if (_entries.TryGetValue(key, out var v) && v == ownerToken)
                return Task.FromResult(_entries.TryRemove(key, out _));
            return Task.FromResult(false);
        }

        public Task<bool> TryRefreshLeaseAsync(
            string key, string ownerToken, TimeSpan expiration, CancellationToken cancellationToken = default)
        {
            Refreshes++;
            return Task.FromResult(_entries.TryGetValue(key, out var v) && v == ownerToken);
        }

        public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
            => Task.FromResult(default(T));

        // Bộ đếm của IncrementAsync lưu dạng chuỗi thuần, khác với giá trị ghi qua SetAsync<T>.
        public Task<long?> GetCounterAsync(string key, CancellationToken cancellationToken = default)
            => Task.FromResult(_entries.TryGetValue(key, out var v) && long.TryParse(v, out var n)
                ? (long?)n
                : null);

        public Task SetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
        {
            _entries.TryRemove(key, out _);
            return Task.CompletedTask;
        }

        public Task<long> IncrementAsync(string key, TimeSpan expiration, CancellationToken cancellationToken = default)
            => Task.FromResult(1L);
    }

    /// <summary>
    /// Chờ tới khi điều kiện đúng, tối đa 10 giây. Cố ý KHÔNG dùng một lần <c>Task.Delay</c> cố
    /// định: máy nghẽn thì delay ngắn sẽ đỏ oan, mà delay dài thì mọi lần chạy đều chậm theo.
    /// </summary>
    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return;
            await Task.Delay(25);
        }
    }

    [Fact]
    public async Task WriteFailsFirstTime_RetryStillCreatesTheNotification_ExactlyOnce()
    {
        // Tiêu chí nghiệm thu của issue: lần đầu hỏng, lần thứ hai vẫn tạo notification, và chỉ một lần.
        var cache = new StatefulCache();
        var (harness, written, _) = await ConsumerTestHarness.StartAsync<TicketCreatedConsumer>(
            cache: cache,
            recipients: new[] { ConsumerTestHarness.DefaultRecipient },
            failWriteOnAttempt: attempt => attempt == 1);

        var evt = new TicketCreatedEvent(Guid.NewGuid(), "TKT-765", Guid.NewGuid(), "P2High");
        await harness.Bus.Publish(evt);

        (await harness.Consumed.Any<TicketCreatedEvent>()).Should().BeTrue();

        // Lần đầu ném lỗi ⇒ chỗ giữ phải được NHẢ, nếu không lần gửi lại sẽ bị coi là trùng.
        cache.Releases.Should().BeGreaterThan(0, "lỗi khi ghi phải nhả chỗ giữ");
        written.Should().BeEmpty("lần đầu hỏng nên chưa có notification nào");

        // Lần gửi lại. Không dùng `Consumed.Any<T>()` để chờ: nó trả về ngay vì lần tiêu thụ ĐẦU
        // đã có trong sổ, nên sẽ kiểm nhầm trạng thái cũ. Chờ đúng thứ mình quan tâm — có
        // notification được ghi hay không.
        await harness.Bus.Publish(evt);
        await WaitUntilAsync(() => written.Count > 0);

        written.Should().NotBeEmpty("lần gửi lại phải chạy thật chứ không bị nuốt vì trùng");
        // 2 bản ghi = 1 recipient × 2 kênh (InApp + Push) — đúng bằng một lượt xử lý thành công,
        // không hơn. Xem TicketCreatedConsumerTests, nơi lượt chạy sạch cũng ra 2.
        written.Should().HaveCount(2, "và chỉ tạo đúng MỘT lượt notification, không nhân đôi");

        await harness.Stop();
    }

    [Fact]
    public async Task SuccessfulWrite_ExtendsToTheFullDedupeWindow_SoRealDuplicatesAreStillSkipped()
    {
        // Chống hồi quy theo chiều ngược lại: sửa xong vẫn phải chặn trùng thật. Nếu chỉ nhả mà
        // không nâng lên cửa sổ dài thì mọi lần gửi lại đều tạo notification mới — nhân bản spam.
        var cache = new StatefulCache();
        var (harness, written, _) = await ConsumerTestHarness.StartAsync<TicketCreatedConsumer>(cache: cache);

        var evt = new TicketCreatedEvent(Guid.NewGuid(), "TKT-766", Guid.NewGuid(), "P3Standard");
        await harness.Bus.Publish(evt);
        (await harness.Consumed.Any<TicketCreatedEvent>()).Should().BeTrue();

        written.Should().NotBeEmpty();
        cache.Refreshes.Should().BeGreaterThan(0,
            "ghi xong phải nâng chỗ giữ lên cửa sổ chống trùng, nếu không trùng thật sẽ lọt");
        cache.Releases.Should().Be(0, "không có lỗi thì không được nhả chỗ giữ");

        await harness.Stop();
    }
}
