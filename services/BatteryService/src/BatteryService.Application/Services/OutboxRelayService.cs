using System.Text.Json;
using BatteryService.Application.Interfaces;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Events;
using SharedContracts.Events.Root;
using SharedContracts.Interfaces;

namespace BatteryService.Application.Services;

public class OutboxRelayService : IOutboxRelayService
{
    /// <summary>
    /// GH-725 — số lần thử tối đa. Vượt ngưỡng, row bị loại khỏi batch (dead-letter) để
    /// KHÔNG chiếm chỗ của row mới.
    /// </summary>
    /// <remarks>
    /// Trước đây truy vấn chỉ lọc <c>ProcessedAtUtc == null</c> rồi <c>OrderBy(OccurredAtUtc)</c>
    /// + <c>Take(batchSize)</c>. Một row hỏng vĩnh viễn (vd sai type) luôn là row cũ nhất nên
    /// chiếm suất trong batch mãi mãi — đủ số row hỏng là relay ngừng đẩy được event mới
    /// (starvation), dù mọi thứ khác vẫn bình thường.
    /// </remarks>
    public const int MaxRetryCount = 10;

    /// <summary>
    /// Marker gắn vào <c>LastError</c> khi row chạm trần retry, để ops tìm dead-letter bằng
    /// <c>ProcessedAtUtc IS NULL AND retry_count &gt;= 10</c>.
    /// </summary>
    public const string DeadLetterMarker = "[DEAD-LETTER]";

    /// <summary>
    /// Map type name (lưu trong cột <c>outbox_messages.type</c>) → CLR type cho deserialize.
    /// </summary>
    /// <remarks>
    /// GH-725 — TRƯỚC ĐÂY map này viết tay và liệt kê 7 type, trong khi service ghi ra nhiều
    /// hơn thế (AlertLinkedToTicket, AlertLinkToTicketRejected, EnvironmentalIncidentDetected,
    /// EnvironmentalIncidentResolved, BatteryAssetCreated, BatteryAssetTransferred). Những row
    /// đó bị đánh "Unknown event type" và không bao giờ rời outbox.
    ///
    /// Sửa bằng cách bỏ danh sách viết tay: dựng map bằng phản chiếu trên toàn bộ
    /// <see cref="IntegrationEvent"/> cụ thể trong assembly SharedContracts. Thêm event mới
    /// KHÔNG cần nhớ sửa file này nữa — đó mới là gốc của lỗi, chứ không phải 6 dòng bị thiếu.
    ///
    /// Cột <c>type</c> được ghi bằng <c>typeof(TEvent).Name</c> (xem
    /// <c>IntegrationEventOutboxWriter</c>) nên khoá của map đúng là tên đơn giản của type.
    /// </remarks>
    private static readonly IReadOnlyDictionary<string, Type> EventTypeMap = BuildEventTypeMap();

    private static Dictionary<string, Type> BuildEventTypeMap()
    {
        var concreteEvents = typeof(IntegrationEvent).Assembly
            .GetTypes()
            .Where(t => !t.IsAbstract && t.IsAssignableTo(typeof(IntegrationEvent)))
            .ToList();

        // Tên đơn giản trùng nhau ⇒ cột `type` trở nên nhập nhằng, chọn bừa sẽ publish sai
        // type. Dừng ngay lúc khởi tạo với thông báo rõ, hơn là sai âm thầm lúc chạy.
        var duplicates = concreteEvents
            .GroupBy(t => t.Name)
            .Where(g => g.Count() > 1)
            .Select(g => $"{g.Key}: {string.Join(", ", g.Select(t => t.FullName))}")
            .ToList();

        if (duplicates.Count > 0)
        {
            throw new InvalidOperationException(
                "Trùng tên IntegrationEvent trong SharedContracts — cột outbox `type` không còn " +
                $"phân biệt được: {string.Join(" | ", duplicates)}");
        }

        var map = concreteEvents.ToDictionary(t => t.Name, t => t, StringComparer.Ordinal);

        // Alias legacy: KHÔNG có type nào tên như vậy: đây là các row pre-Sprint-5B chưa relay,
        // payload theo schema cũ (BatteryAnomalyDetectedEvent).
        map["BatteryAnomalyEscalatedEvent"] = typeof(BatteryAnomalyDetectedEvent);

        return map;
    }

    private readonly IBatteryUnitOfWork _unitOfWork;
    private readonly IMessageProducerService _producer;

    public OutboxRelayService(IBatteryUnitOfWork unitOfWork, IMessageProducerService producer)
    {
        _unitOfWork = unitOfWork;
        _producer = producer;
    }

    public async Task<OutboxRelayResult> RelayBatchAsync(
        int batchSize = 100, CancellationToken cancellationToken = default)
    {
        // GH-725 — loại row đã chạm trần retry: chúng là row cũ nhất nên nếu không loại sẽ
        // chiếm hết suất trong batch và chặn vĩnh viễn event mới (starvation).
        // Row bị loại vẫn nằm trong bảng để ops tra: ProcessedAtUtc IS NULL AND retry_count >= 10.
        var pending = await _unitOfWork.OutboxMessages
            .GetAllAsync()
            .Where(m => m.ProcessedAtUtc == null && m.RetryCount < MaxRetryCount)
            .OrderBy(m => m.OccurredAtUtc)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        var result = new OutboxRelayResult();
        if (pending.Count == 0)
            return result;

        foreach (var msg in pending)
        {
            try
            {
                if (!EventTypeMap.TryGetValue(msg.Type, out var clrType))
                {
                    RecordFailure(msg, $"Unknown event type: {msg.Type}", result);
                    continue;
                }

                var evt = (IntegrationEvent?)JsonSerializer.Deserialize(msg.Payload, clrType);
                if (evt is null)
                {
                    RecordFailure(msg, "Deserialize returned null", result);
                    continue;
                }

                await _producer.PublishAsync(evt, cancellationToken);
                msg.ProcessedAtUtc = DateTime.UtcNow;
                _unitOfWork.OutboxMessages.UpdateAsync(msg);
                result.Published++;
            }
            catch (Exception ex)
            {
                RecordFailure(msg, ex.Message, result);
            }
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return result;
    }

    /// <summary>
    /// GH-725 — tăng retry, ghi lỗi, và đánh dấu dead-letter khi chạm trần
    /// <see cref="MaxRetryCount"/> để ops phân biệt "đang thử lại" với "đã bỏ".
    /// </summary>
    private void RecordFailure(Domain.Entities.OutboxMessage msg, string error, OutboxRelayResult result)
    {
        msg.RetryCount += 1;

        var trimmed = error.Length > 2000 ? error[..2000] : error;
        msg.LastError = msg.RetryCount >= MaxRetryCount
            ? $"{DeadLetterMarker} (retry {msg.RetryCount}/{MaxRetryCount}) {trimmed}"
            : trimmed;

        _unitOfWork.OutboxMessages.UpdateAsync(msg);
        result.Failed++;
    }
}
