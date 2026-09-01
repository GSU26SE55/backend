namespace BatteryService.Application.Services;

/// <summary>
/// <see cref="IOutboxSignal"/> bằng <see cref="SemaphoreSlim"/> sức chứa 1.
/// </summary>
/// <remarks>
/// Sức chứa 1 là có chủ ý: một lượt ingest có thể sinh nhiều alert và gọi <see cref="Notify"/>
/// nhiều lần, nhưng relay chỉ cần thức dậy MỘT lần rồi quét cả lô. Semaphore không đếm quá 1 nên
/// n lần gọi gộp thành một lượt đánh thức — không dồn n vòng quét rỗng phía sau.
///
/// <para><c>Release()</c> ném <see cref="SemaphoreFullException"/> khi đã đầy; bắt và bỏ qua vì
/// "đã có tín hiệu chờ sẵn" đúng là kết quả mong muốn.</para>
/// </remarks>
public sealed class OutboxSignal : IOutboxSignal
{
    private readonly SemaphoreSlim _semaphore = new(0, 1);

    public void Notify()
    {
        try
        { _semaphore.Release(); }
        catch (SemaphoreFullException) { /* đã có tín hiệu chờ — không cần thêm */ }
    }

    public Task WaitAsync(CancellationToken ct) => _semaphore.WaitAsync(ct);
}
