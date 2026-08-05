using AuditAggregatorService.Application.Interfaces;
using AuditAggregatorService.Domain.Entities;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SharedContracts.Events.Audit;
using SharedInfrastructure.Metrics;

namespace AuditAggregatorService.Application.Consumers;

/// <summary>
/// Consume <see cref="AuditCreatedEventV1"/> từ exchange <c>audit.events</c> → INSERT idempotent vào read-store
/// <c>audit_aggregate</c> (Sprint audit <c>#AUDIT-15</c>).
///
/// <para>Idempotency: check <c>AnyAsync(event_id)</c> trước; nếu chưa có thì AddAsync + SaveChanges, catch
/// <see cref="DbUpdateException"/> cho race (composite unique event_id+occurred_at). 1000 duplicate event → chỉ 1 row
/// (test <c>#AUDIT-19</c>). Geo IP enrichment thêm ở <c>#AUDIT-16</c>.</para>
/// </summary>
public class AuditCreatedConsumer : IConsumer<AuditCreatedEventV1>
{
    private readonly IAuditAggregatorUnitOfWork _unitOfWork;
    private readonly IGeoIpResolver _geoIp;
    private readonly ILogger<AuditCreatedConsumer> _logger;

    public AuditCreatedConsumer(IAuditAggregatorUnitOfWork unitOfWork, IGeoIpResolver geoIp, ILogger<AuditCreatedConsumer> logger)
    {
        _unitOfWork = unitOfWork;
        _geoIp = geoIp;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<AuditCreatedEventV1> context)
    {
        var evt = context.Message;
        var ct = context.CancellationToken;

        // Idempotency check trước (rẻ hơn so với bắt exception).
        if (await _unitOfWork.AuditAggregates.AnyAsync(x => x.EventId == evt.EventId))
        {
            _logger.LogDebug("Audit event {EventId} ({Service}/{Action}) đã tồn tại — bỏ qua (idempotent).",
                evt.EventId, evt.ServiceName, evt.ActionCode);
            return;
        }

        var aggregate = AuditAggregate.FromEvent(
            evt.EventId, evt.ServiceName, evt.ActionCode, evt.ActionCategory, evt.Severity,
            evt.TargetType, evt.TargetId, evt.TargetDisplay,
            evt.ActorAccountId, evt.ActorRole, evt.ActorDisplay, evt.ActorIp, evt.ActorUserAgent,
            evt.IsSuccess, evt.ErrorCode, evt.Reason, evt.MetadataJson,
            evt.CorrelationId, evt.CausationId, evt.OccurredAt, evt.RecordedAt);

        // #AUDIT-16 — geo enrichment (optional, fallback null).
        var geo = _geoIp.Lookup(evt.ActorIp);
        if (geo is not null)
        {
            aggregate.GeoCountry = geo.CountryCode;
            aggregate.GeoCity = geo.City;
        }

        await _unitOfWork.AuditAggregates.AddAsync(aggregate);

        try
        {
            await _unitOfWork.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (DuplicateAuditDetection.IsDuplicateEventInsert(ex))
        {
            // Race: consumer khác đã insert cùng event_id (composite unique). Idempotent → coi như đã có, không tính metric.
            _logger.LogDebug("Audit event {EventId} bị insert đồng thời — bỏ qua (idempotent race).", evt.EventId);
            return;
        }
        catch (DbUpdateException ex)
        {
            // GH-775 — MỌI lỗi DB khác phải nổi lên. Bản cũ bắt trọn DbUpdateException nên chuỗi
            // quá dài (22001), jsonb không hợp lệ (22P02) hay lỗi kết nối cũng bị ghi log là
            // "trùng lặp" rồi ACK: bản ghi kiểm toán mất hẳn, không retry, không DLQ. Với hệ thống
            // kiểm toán thì đó là kiểu mất mát tệ nhất — mất trong im lặng và tự nhận là bình thường.
            _logger.LogError(ex,
                "Audit event {EventId} ({Service}/{Action}) lưu thất bại — KHÔNG phải trùng lặp. "
                + "Ném lại để MassTransit thử lại / đưa vào error queue.",
                evt.EventId, evt.ServiceName, evt.ActionCode);
            throw;
        }

        // #AUDIT-44 — metric: counter events_total + histogram consumer_lag (now - occurred_at).
        AppMetrics.AuditEventsTotal.WithLabels(evt.ServiceName, evt.ActionCode, evt.Severity).Inc();
        var lagSeconds = (DateTime.UtcNow - evt.OccurredAt).TotalSeconds;
        if (lagSeconds >= 0)
            AppMetrics.AuditConsumerLagSeconds.Observe(lagSeconds);
    }
}
