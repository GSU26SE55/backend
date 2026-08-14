using MassTransit;
using SharedContracts.Events.Root;
using SharedInfrastructure.Idempotency;

namespace SharedInfrastructure.UnitTests.Idempotency;

// Public để Moq DynamicProxy có thể tạo proxy (assembly MassTransit.Abstractions là strong-named).
public record IdempotencySampleEvent : IntegrationEvent
{
    public string Payload { get; set; } = string.Empty;
}

public class IdempotentConsumerExtensionsTests
{
    [Fact]
    public async Task ProcessOnceAsync_FirstCall_RunsAction_ReturnsTrue()
    {
        var inbox = new Mock<IInboxStore>();
        inbox.Setup(s => s.TryBeginAsync(It.IsAny<Guid>(), "MyConsumer", It.IsAny<CancellationToken>()))
             .ReturnsAsync(new InboxClaim(InboxClaimStatus.Claimed, "gh764-test-token"));

        var ctxMock = new Mock<ConsumeContext<IdempotencySampleEvent>>();
        ctxMock.Setup(c => c.Message).Returns(new IdempotencySampleEvent { Payload = "x" });
        ctxMock.Setup(c => c.CancellationToken).Returns(CancellationToken.None);

        var actionRan = 0;

        var ran = await ctxMock.Object.ProcessOnceAsync(inbox.Object, "MyConsumer", () =>
        {
            actionRan++;
            return Task.CompletedTask;
        });

        ran.Should().BeTrue();
        actionRan.Should().Be(1);
    }

    [Fact]
    public async Task ProcessOnceAsync_DuplicateCall_SkipsAction_ReturnsFalse()
    {
        var inbox = new Mock<IInboxStore>();
        inbox.Setup(s => s.TryBeginAsync(It.IsAny<Guid>(), "C", It.IsAny<CancellationToken>()))
             .ReturnsAsync(InboxClaim.Completed);

        var ctxMock = new Mock<ConsumeContext<IdempotencySampleEvent>>();
        ctxMock.Setup(c => c.Message).Returns(new IdempotencySampleEvent());
        ctxMock.Setup(c => c.CancellationToken).Returns(CancellationToken.None);

        var actionRan = 0;
        var ran = await ctxMock.Object.ProcessOnceAsync(inbox.Object, "C", () =>
        {
            actionRan++;
            return Task.CompletedTask;
        });

        ran.Should().BeFalse();
        actionRan.Should().Be(0);
    }

    [Fact]
    public async Task ProcessOnceAsync_UsesIntegrationEventId_AsDedupeKey()
    {
        var evt = new IdempotencySampleEvent();

        var inbox = new Mock<IInboxStore>();
        Guid captured = Guid.Empty;
        inbox.Setup(s => s.TryBeginAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
             .Callback<Guid, string, CancellationToken>((id, _, _) => captured = id)
             .ReturnsAsync(new InboxClaim(InboxClaimStatus.Claimed, "gh764-test-token"));

        var ctxMock = new Mock<ConsumeContext<IdempotencySampleEvent>>();
        ctxMock.Setup(c => c.Message).Returns(evt);
        ctxMock.Setup(c => c.CancellationToken).Returns(CancellationToken.None);

        await ctxMock.Object.ProcessOnceAsync(inbox.Object, "C", () => Task.CompletedTask);

        captured.Should().Be(evt.Id);
    }

    [Fact]
    public async Task ProcessOnceAsync_ActionThrows_PropagatesException()
    {
        var inbox = new Mock<IInboxStore>();
        inbox.Setup(s => s.TryBeginAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(new InboxClaim(InboxClaimStatus.Claimed, "gh764-test-token"));

        var ctxMock = new Mock<ConsumeContext<IdempotencySampleEvent>>();
        ctxMock.Setup(c => c.Message).Returns(new IdempotencySampleEvent());
        ctxMock.Setup(c => c.CancellationToken).Returns(CancellationToken.None);

        var act = async () => await ctxMock.Object.ProcessOnceAsync(inbox.Object, "C",
            () => throw new InvalidOperationException("downstream error"));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("downstream error");
    }

    // ── GH-764 ───────────────────────────────────────────────────────────────────
    // Bản cũ đánh dấu "đã xử lý" TRƯỚC khi gọi action và không bao giờ gỡ dấu khi action lỗi.
    // Hậu quả: một lỗi tạm thời (nhà cung cấp email chập chờn, DB đích lỗi) biến thành MẤT
    // MESSAGE VĨNH VIỄN — MassTransit gửi lại, thấy dấu, bỏ qua, rồi ACK. Email/SMS/đồng bộ DB
    // không bao giờ chạy, và không có gì báo cho ai biết.

    /// <summary>Inbox giả bám đúng vòng đời ba bước — đủ để chạy hai lần thử liên tiếp.</summary>
    private sealed class FakeInboxStore : IInboxStore
    {
        private readonly Dictionary<string, (bool Completed, string Token)> _entries = new();

        public int BeginCalls { get; private set; }
        public int CompleteCalls { get; private set; }
        public int ReleaseCalls { get; private set; }

        public Task<InboxClaim> TryBeginAsync(Guid messageId, string consumerName, CancellationToken ct = default)
        {
            BeginCalls++;
            var key = $"{consumerName}:{messageId}";
            if (_entries.TryGetValue(key, out var e))
                return Task.FromResult(e.Completed ? InboxClaim.Completed : InboxClaim.Busy);

            var token = Guid.NewGuid().ToString("N");
            _entries[key] = (false, token);
            return Task.FromResult(new InboxClaim(InboxClaimStatus.Claimed, token));
        }

        public Task CompleteAsync(Guid messageId, string consumerName, string token, CancellationToken ct = default)
        {
            CompleteCalls++;
            var key = $"{consumerName}:{messageId}";
            if (_entries.TryGetValue(key, out var e) && e.Token == token)
                _entries[key] = (true, token);
            return Task.CompletedTask;
        }

        public Task ReleaseAsync(Guid messageId, string consumerName, string token, CancellationToken ct = default)
        {
            ReleaseCalls++;
            var key = $"{consumerName}:{messageId}";
            if (_entries.TryGetValue(key, out var e) && e.Token == token && !e.Completed)
                _entries.Remove(key);
            return Task.CompletedTask;
        }
    }

    private static Mock<ConsumeContext<IdempotencySampleEvent>> ContextFor(IdempotencySampleEvent evt)
    {
        var ctx = new Mock<ConsumeContext<IdempotencySampleEvent>>();
        ctx.Setup(c => c.Message).Returns(evt);
        ctx.Setup(c => c.CancellationToken).Returns(CancellationToken.None);
        return ctx;
    }

    [Fact]
    public async Task RetryAfterSideEffectFailure_RunsTheActionAgain_AndSucceeds()
    {
        // Tiêu chí nghiệm thu #1 + #3 của issue: lần đầu ném lỗi, lần gửi lại PHẢI chạy thật;
        // nhà cung cấp được gọi đúng hai lần và chỉ có MỘT kết quả cuối cùng.
        var store = new FakeInboxStore();
        var evt = new IdempotencySampleEvent { Payload = "otp" };
        var providerCalls = 0;
        var delivered = 0;

        async Task SendAsync()
        {
            providerCalls++;
            if (providerCalls == 1)
                throw new InvalidOperationException("provider tạm thời lỗi");
            delivered++;
            await Task.CompletedTask;
        }

        // Lần thử 1 — lỗi, và lỗi phải nổi lên để MassTransit giữ nguyên chính sách thử lại.
        var first = async () => await ContextFor(evt).Object.ProcessOnceAsync(store, "C", SendAsync);
        await first.Should().ThrowAsync<InvalidOperationException>();

        // Lần thử 2 — cùng message id (evt.Id không đổi).
        var ran = await ContextFor(evt).Object.ProcessOnceAsync(store, "C", SendAsync);

        ran.Should().BeTrue("lần gửi lại phải thực sự chạy chứ không bị coi là trùng");
        providerCalls.Should().Be(2);
        delivered.Should().Be(1, "chỉ được có một kết quả cuối cùng");
        store.ReleaseCalls.Should().Be(1);
        store.CompleteCalls.Should().Be(1);

        // Lần thử 3 — giờ mới thực sự là trùng.
        var third = await ContextFor(evt).Object.ProcessOnceAsync(store, "C", SendAsync);
        third.Should().BeFalse();
        providerCalls.Should().Be(2);
    }

    [Fact]
    public async Task ConcurrentDuplicate_ProducesExactlyOneSideEffect()
    {
        // Tiêu chí nghiệm thu #2: hai bản sao chạy song song vẫn chỉ một side effect. Bản thua
        // KHÔNG được bỏ qua trong im lặng — nó phải quay lại sau, nếu không ta lại ACK một
        // message mà side effect có thể chưa xong.
        var store = new FakeInboxStore();
        var evt = new IdempotencySampleEvent();
        var sideEffects = 0;
        var started = new SemaphoreSlim(0, 1);
        var release = new TaskCompletionSource();

        async Task SlowAction()
        {
            Interlocked.Increment(ref sideEffects);
            started.Release();
            await release.Task;
        }

        var winner = ContextFor(evt).Object.ProcessOnceAsync(store, "C", SlowAction);
        await started.WaitAsync();   // chắc chắn bản thứ nhất đang giữ chỗ

        var loser = async () => await ContextFor(evt).Object.ProcessOnceAsync(store, "C", SlowAction);
        await loser.Should().ThrowAsync<InboxLeaseHeldException>();

        release.SetResult();
        (await winner).Should().BeTrue();
        sideEffects.Should().Be(1);
    }

    [Fact]
    public async Task InProgressElsewhere_IsNotTreatedAsDuplicate()
    {
        // Ném ra thay vì trả false là điểm mấu chốt: trả false sẽ khiến consumer kết thúc êm đẹp
        // và message bị ACK, dù side effect của người kia có thể đang hỏng dở.
        var inbox = new Mock<IInboxStore>();
        inbox.Setup(s => s.TryBeginAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(InboxClaim.Busy);

        var actionRan = 0;
        var act = async () => await ContextFor(new IdempotencySampleEvent()).Object
            .ProcessOnceAsync(inbox.Object, "C", () => { actionRan++; return Task.CompletedTask; });

        await act.Should().ThrowAsync<InboxLeaseHeldException>();
        actionRan.Should().Be(0);
    }

    [Fact]
    public async Task ActionThrows_ReleasesClaim_AndNeverMarksCompleted()
    {
        var inbox = new Mock<IInboxStore>();
        inbox.Setup(s => s.TryBeginAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(new InboxClaim(InboxClaimStatus.Claimed, "tok"));

        var act = async () => await ContextFor(new IdempotencySampleEvent()).Object
            .ProcessOnceAsync(inbox.Object, "C", () => throw new InvalidOperationException("boom"));

        await act.Should().ThrowAsync<InvalidOperationException>();

        inbox.Verify(s => s.ReleaseAsync(It.IsAny<Guid>(), "C", "tok", It.IsAny<CancellationToken>()), Times.Once);
        inbox.Verify(s => s.CompleteAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task WhenReleaseItselfFails_TheOriginalErrorStillSurfaces()
    {
        // Lỗi gốc mới là thứ quyết định chính sách thử lại của MassTransit. Để lỗi lúc nhả chỗ
        // giữ đè lên nó là làm mất thông tin chẩn đoán, và có thể đổi cả cách phân loại lỗi.
        var inbox = new Mock<IInboxStore>();
        inbox.Setup(s => s.TryBeginAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(new InboxClaim(InboxClaimStatus.Claimed, "tok"));
        inbox.Setup(s => s.ReleaseAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
             .ThrowsAsync(new Exception("redis down"));

        var act = async () => await ContextFor(new IdempotencySampleEvent()).Object
            .ProcessOnceAsync(inbox.Object, "C", () => throw new InvalidOperationException("lỗi gốc"));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("lỗi gốc");
    }

    [Fact]
    public async Task SuccessfulAction_MarksCompleted_WithTheClaimToken()
    {
        var inbox = new Mock<IInboxStore>();
        inbox.Setup(s => s.TryBeginAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(new InboxClaim(InboxClaimStatus.Claimed, "tok"));

        await ContextFor(new IdempotencySampleEvent()).Object
            .ProcessOnceAsync(inbox.Object, "C", () => Task.CompletedTask);

        inbox.Verify(s => s.CompleteAsync(It.IsAny<Guid>(), "C", "tok", It.IsAny<CancellationToken>()), Times.Once);
        inbox.Verify(s => s.ReleaseAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
