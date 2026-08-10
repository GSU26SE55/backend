using System.Text.Json;
using MassTransit;
using SharedContracts.Events.Root;
using SharedInfrastructure.Idempotency;

namespace SharedInfrastructure.UnitTests.Idempotency;

/// <summary>
/// GH-789 — hàng rào chống trùng phải đứng vững qua đúng đường đi thật của một event.
/// </summary>
/// <remarks>
/// <para>
/// Đường đi đó là: handler tạo event → serialize xuống outbox → relay đọc lên, deserialize →
/// publish → consumer <c>ProcessOnceAsync</c>. Relay chạy lại (retry, service khởi động lại, hai
/// instance cùng đọc một dòng) sẽ deserialize LẠI cùng một payload.
/// </para>
/// <para>
/// Với <c>private set</c>, mỗi lần deserialize sinh <c>Id</c> mới ⇒ mỗi lần một khoá inbox khác ⇒
/// side effect chạy lại. Test ở đây đo trực tiếp SỐ LẦN side effect chạy, chứ không chỉ so hai
/// chuỗi GUID — đó mới là điều người dùng cảm nhận được: email/SMS/notification gửi trùng.
/// </para>
/// </remarks>
public class OutboxRoundTripIdempotencyTests
{
    /// <summary>Kho inbox tối giản trong bộ nhớ — đủ để đếm khoá đã cấp.</summary>
    private sealed class InMemoryInboxStore : IInboxStore
    {
        private readonly HashSet<string> _done = [];

        public Task<InboxClaim> TryBeginAsync(Guid messageId, string consumerName, CancellationToken ct = default)
            => Task.FromResult(_done.Add($"{messageId}|{consumerName}")
                ? new InboxClaim(InboxClaimStatus.Claimed, "token")
                : InboxClaim.Completed);

        public Task CompleteAsync(Guid messageId, string consumerName, string token, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task ReleaseAsync(Guid messageId, string consumerName, string token, CancellationToken ct = default)
        {
            _done.Remove($"{messageId}|{consumerName}");
            return Task.CompletedTask;
        }
    }

    private static ConsumeContext<IdempotencySampleEvent> ContextFor(IdempotencySampleEvent evt)
    {
        var ctx = new Mock<ConsumeContext<IdempotencySampleEvent>>();
        ctx.Setup(c => c.Message).Returns(evt);
        ctx.Setup(c => c.CancellationToken).Returns(CancellationToken.None);
        return ctx.Object;
    }

    /// <summary>Mô phỏng một lượt relay: đọc payload từ outbox rồi dựng lại event.</summary>
    private static IdempotencySampleEvent RelayPass(string outboxPayload)
        => JsonSerializer.Deserialize<IdempotencySampleEvent>(outboxPayload)!;

    [Fact]
    public async Task RelayRetry_OfTheSameOutboxRow_ProducesExactlyOneSideEffect()
    {
        var stored = JsonSerializer.Serialize(new IdempotencySampleEvent { Payload = "gui email kich hoat" });
        var inbox = new InMemoryInboxStore();
        var sideEffects = 0;

        // Ba lượt relay trên CÙNG một dòng outbox — retry, restart, và một instance thứ hai.
        for (var pass = 0; pass < 3; pass++)
        {
            await ContextFor(RelayPass(stored)).ProcessOnceAsync(inbox, "EmailConsumer", () =>
            {
                sideEffects++;
                return Task.CompletedTask;
            });
        }

        sideEffects.Should().Be(1,
            "ba lượt relay của cùng một event nghiệp vụ chỉ được gửi email đúng một lần");
    }

    [Fact]
    public async Task TwoDistinctEvents_StillProduceTwoSideEffects()
    {
        // Chiều âm: nếu khoá bị "dính" quá tay (vd cùng khoá cho mọi event) thì test trên vẫn xanh
        // trong khi hệ thống nuốt mất event thật.
        var inbox = new InMemoryInboxStore();
        var sideEffects = 0;

        foreach (var payload in new[] { "event mot", "event hai" })
        {
            var stored = JsonSerializer.Serialize(new IdempotencySampleEvent { Payload = payload });
            await ContextFor(RelayPass(stored)).ProcessOnceAsync(inbox, "EmailConsumer", () =>
            {
                sideEffects++;
                return Task.CompletedTask;
            });
        }

        sideEffects.Should().Be(2);
    }

    [Fact]
    public async Task DifferentConsumers_EachHandleTheSameEventOnce()
    {
        // Khoá gồm cả tên consumer: một event phải tới được mọi consumer quan tâm, mỗi nơi đúng một lần.
        var stored = JsonSerializer.Serialize(new IdempotencySampleEvent { Payload = "x" });
        var inbox = new InMemoryInboxStore();
        var email = 0;
        var sms = 0;

        for (var pass = 0; pass < 2; pass++)
        {
            await ContextFor(RelayPass(stored)).ProcessOnceAsync(inbox, "EmailConsumer",
                () => { email++; return Task.CompletedTask; });
            await ContextFor(RelayPass(stored)).ProcessOnceAsync(inbox, "SmsConsumer",
                () => { sms++; return Task.CompletedTask; });
        }

        email.Should().Be(1);
        sms.Should().Be(1);
    }

    [Fact]
    public void RelayPass_PreservesTheEnvelope_WrittenByTheProducer()
    {
        // Nối nguyên nhân với hậu quả: các khẳng định trên chỉ đúng vì phong bì sống sót.
        var produced = new IdempotencySampleEvent { Payload = "x" };

        var afterRelay = RelayPass(JsonSerializer.Serialize(produced));

        afterRelay.Id.Should().Be(produced.Id);
        afterRelay.OccurredAt.Should().Be(produced.OccurredAt);
    }
}
