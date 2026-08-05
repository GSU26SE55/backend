using MassTransit;
using SharedContracts.Events.Root;
using SharedInfrastructure.Metrics;

namespace SharedInfrastructure.Idempotency;

/// <summary>
/// GH-764 — message đang được người khác xử lý; phải quay lại sau, không được ACK.
/// </summary>
/// <remarks>
/// Ném ra thay vì bỏ qua là có chủ ý: bỏ qua sẽ ACK một message mà side effect có thể chưa bao
/// giờ chạy. Ném ra thì MassTransit gửi lại; lúc đó người kia hoặc đã xong (⇒ bỏ qua thật) hoặc
/// đã chết (⇒ chỗ giữ hết hạn, ta nhận lại).
/// </remarks>
public class InboxLeaseHeldException : Exception
{
    public InboxLeaseHeldException(Guid messageId, string consumerName)
        : base($"Message {messageId} đang được {consumerName} xử lý — sẽ thử lại sau.")
    {
        MessageId = messageId;
        ConsumerName = consumerName;
    }

    public Guid MessageId { get; }
    public string ConsumerName { get; }
}

public static class IdempotentConsumerExtensions
{
    /// <summary>
    /// Bọc logic consumer bằng Inbox idempotency. Action chỉ chạy XONG một lần cho mỗi
    /// (messageId, consumerName). Trả về true nếu action đã chạy, false nếu bỏ qua vì trùng.
    /// </summary>
    /// <remarks>
    /// GH-764 — bản cũ đánh dấu "đã xử lý" TRƯỚC khi gọi action và không bao giờ gỡ dấu khi
    /// action lỗi. Hậu quả: một lỗi tạm thời (nhà cung cấp email chập chờn, DB đích lỗi) biến
    /// thành mất message VĨNH VIỄN — MassTransit gửi lại, thấy dấu, bỏ qua, rồi ACK. Email/SMS/
    /// đồng bộ DB không bao giờ chạy và không ai biết.
    /// <para>
    /// Nay: giữ chỗ có hạn → chạy action → chốt nếu xong, nhả nếu lỗi. Lỗi vẫn ném ra ngoài để
    /// MassTransit giữ nguyên chính sách thử lại / đưa vào error queue.
    /// </para>
    /// </remarks>
    public static async Task<bool> ProcessOnceAsync<TMessage>(
        this ConsumeContext<TMessage> context,
        IInboxStore store,
        string consumerName,
        Func<Task> action)
        where TMessage : class
    {
        var messageId = context.Message switch
        {
            IntegrationEvent evt => evt.Id,
            _ => context.MessageId ?? Guid.NewGuid()
        };

        var claim = await store.TryBeginAsync(messageId, consumerName, context.CancellationToken);

        if (claim.Status == InboxClaimStatus.AlreadyCompleted)
        {
            AppMetrics.InboxSkippedDuplicate.WithLabels(consumerName).Inc();
            return false;
        }

        if (claim.Status == InboxClaimStatus.InProgressElsewhere)
            throw new InboxLeaseHeldException(messageId, consumerName);

        try
        {
            await action();
        }
        catch
        {
            // Nhả chỗ giữ để lần gửi lại chạy THẬT. Bọc riêng để lỗi lúc nhả không che mất lỗi
            // gốc — lỗi gốc mới là thứ quyết định chính sách thử lại của MassTransit.
            try
            {
                await store.ReleaseAsync(messageId, consumerName, claim.Token, context.CancellationToken);
            }
            catch
            {
                // Nhả hụt thì chỗ giữ tự hết hạn; không được nuốt mất lỗi gốc bên dưới.
            }
            throw;
        }

        await store.CompleteAsync(messageId, consumerName, claim.Token, context.CancellationToken);
        AppMetrics.InboxProcessed.WithLabels(consumerName).Inc();
        return true;
    }
}
