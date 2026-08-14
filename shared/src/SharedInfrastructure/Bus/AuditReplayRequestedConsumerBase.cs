using System.Text.Json;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SharedContracts.Audit;
using SharedContracts.Events.Audit;
using SharedKernels.Domain;
using SharedKernels.Interfaces;

namespace SharedInfrastructure.Bus;

/// <summary>
/// GH-728 — khung xử lý yêu cầu replay audit, dùng chung cho 6 service có bảng audit outbox
/// (Auth, Battery, Ticket, Notification, Sms, FileStorage).
///
/// <para>Mỗi service chỉ phải trả lời hai câu: <b>tôi tên gì</b> (<see cref="ServiceName"/>)
/// và <b>bảng outbox của tôi ở đâu</b> (<see cref="OutboxQuery"/>). Toàn bộ phần dễ sai —
/// lọc đúng service, phân trang ổn định, trần an toàn, deserialize, bắt lỗi, luôn báo cáo
/// lại — nằm ở đây, viết một lần.</para>
///
/// <para><b>Vì sao đọc outbox chứ không đọc bảng audit-log:</b> xem
/// <see cref="IAuditOutboxMessage"/>. Tóm tắt: 6 bảng audit-log không đồng nhất, còn
/// <c>Payload</c> của outbox chính là <c>AuditCreatedEventV1</c> đã serialize.</para>
///
/// <para><b>Vì sao replay an toàn:</b> event phát lại giữ nguyên <c>EventId</c> gốc, mà
/// consumer của aggregator đã idempotent theo <c>EventId</c> (#AUDIT-15) ⇒ chạy nhiều lần
/// không sinh bản ghi trùng.</para>
/// </summary>
/// <typeparam name="TOutbox">Entity outbox của service, vd <c>BatteryAuditOutbox</c>.</typeparam>
public abstract class AuditReplayRequestedConsumerBase<TOutbox> : IConsumer<AuditReplayRequestedEvent>
    where TOutbox : AuditableEntity, IAuditOutboxMessage
{
    /// <summary>Số bản ghi đọc mỗi trang.</summary>
    protected virtual int PageSize => 500;

    /// <summary>
    /// Trần an toàn cho MỘT lần replay của MỘT service. Chạm trần thì dừng và báo
    /// <c>Truncated = true</c> — thà báo thiếu còn hơn bơm hàng triệu message vào RabbitMQ
    /// rồi làm nghẽn cả hệ thống.
    /// </summary>
    protected virtual int MaxRowsPerReplay => 50_000;

    /// <summary>Tên service, PHẢI khớp hằng trong <see cref="AuditServiceNames"/>.</summary>
    protected abstract string ServiceName { get; }

    protected abstract ILogger Logger { get; }

    /// <summary>
    /// <c>IQueryable</c> của bảng outbox service này (lấy từ UnitOfWork).
    /// Base tự thêm lọc thời gian, sắp xếp và phân trang.
    /// </summary>
    protected abstract IQueryable<TOutbox> OutboxQuery { get; }

    public async Task Consume(ConsumeContext<AuditReplayRequestedEvent> context)
    {
        var evt = context.Message;
        var ct = context.CancellationToken;

        // Publish là fanout: mọi service đều nhận. Không phải việc của mình thì im lặng bỏ
        // qua — KHÔNG báo cáo, để aggregator không đếm nhầm số service đã phản hồi.
        if (!AuditServiceNames.Matches(evt.ServiceName, ServiceName))
            return;

        var republished = 0;
        var truncated = false;
        var success = true;
        string? error = null;

        try
        {
            var query = OutboxQuery.AsNoTracking().Where(x => !x.IsDeleted);

            // Lọc theo CreatedAt (lúc ghi outbox). OccurredAt nằm trong JSON nên không lọc
            // được ở tầng SQL; hai mốc này chênh nhau không đáng kể vì outbox được ghi ngay
            // trong cùng transaction với hành động.
            if (evt.From.HasValue)
            {
                var from = evt.From.Value;
                query = query.Where(x => x.CreatedAt >= from);
            }

            if (evt.To.HasValue)
            {
                var to = evt.To.Value;
                query = query.Where(x => x.CreatedAt <= to);
            }

            // Thứ tự PHẢI ổn định: thêm tie-breaker theo Id. Nếu chỉ OrderBy(CreatedAt),
            // các bản ghi trùng mốc thời gian có thể bị Skip/Take bỏ sót hoặc lặp.
            query = query.OrderBy(x => x.CreatedAt).ThenBy(x => x.Id);

            var skip = 0;
            while (true)
            {
                var remaining = MaxRowsPerReplay - republished;
                if (remaining <= 0)
                {
                    truncated = true;
                    break;
                }

                var take = Math.Min(PageSize, remaining);
                var page = await query.Skip(skip).Take(take)
                    .Select(x => new { x.EventId, x.Payload })
                    .ToListAsync(ct);

                if (page.Count == 0)
                    break;

                foreach (var row in page)
                {
                    ct.ThrowIfCancellationRequested();

                    AuditCreatedEventV1? auditEvent;
                    try
                    {
                        auditEvent = JsonSerializer.Deserialize<AuditCreatedEventV1>(row.Payload);
                    }
                    catch (JsonException ex)
                    {
                        // Một dòng hỏng KHÔNG được làm hỏng cả lần replay — bỏ qua, ghi log,
                        // và đánh dấu truncated để job không bị báo "đủ" một cách sai.
                        truncated = true;
                        Logger.LogWarning(ex,
                            "Replay job {JobId} ở {Service}: bỏ qua outbox {EventId} vì payload không đọc được.",
                            evt.JobId, ServiceName, row.EventId);
                        continue;
                    }

                    if (auditEvent is null)
                    {
                        truncated = true;
                        Logger.LogWarning(
                            "Replay job {JobId} ở {Service}: outbox {EventId} deserialize ra null.",
                            evt.JobId, ServiceName, row.EventId);
                        continue;
                    }

                    await context.Publish(auditEvent, ct);
                    republished++;
                }

                // Trang chưa đầy ⇒ hết dữ liệu.
                if (page.Count < take)
                    break;

                skip += page.Count;
            }
        }
        catch (Exception ex)
        {
            success = false;
            error = ex.Message.Length > 1000 ? ex.Message[..1000] : ex.Message;
            Logger.LogError(ex,
                "Replay audit job {JobId} lỗi ở {Service} sau {Count} bản ghi.",
                evt.JobId, ServiceName, republished);
        }

        if (truncated)
        {
            Logger.LogWarning(
                "Replay audit job {JobId} ở {Service} KHÔNG replay trọn vẹn (trần {Max} hoặc payload hỏng).",
                evt.JobId, ServiceName, MaxRowsPerReplay);
        }

        // Luôn báo cáo, kể cả khi lỗi — nếu không, job ở aggregator treo vĩnh viễn.
        await context.Publish(
            new AuditReplayCompletedEvent(
                evt.JobId, ServiceName, republished, success, error, truncated, DateTime.UtcNow),
            ct);

        Logger.LogInformation(
            "Replay audit job {JobId} ở {Service}: {Count} bản ghi, success={Success}, truncated={Truncated}.",
            evt.JobId, ServiceName, republished, success, truncated);
    }
}
