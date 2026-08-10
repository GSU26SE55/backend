using SharedContracts.Events.Root;

namespace SharedContracts.Events.Audit;

/// <summary>
/// GH-728 — AuditAggregatorService yêu cầu các service nguồn nạp lại audit từ
/// source-of-truth (<c>{service}_audit_logs</c>) vào read-store.
///
/// <para>Publish (fanout): mọi service có bảng audit đều nhận; consumer tự lọc bằng
/// <see cref="SharedContracts.Audit.AuditServiceNames.Matches"/>.</para>
///
/// <para><b>Vì sao replay an toàn:</b> service nguồn re-publish lại đúng
/// <see cref="AuditCreatedEventV1"/> cũ, giữ nguyên <c>EventId</c>. Consumer của aggregator
/// đã idempotent theo <c>EventId</c> (#AUDIT-15) nên chạy lại nhiều lần không sinh bản ghi
/// trùng — đó là lý do không cần cơ chế dedupe riêng cho replay.</para>
/// </summary>
/// <param name="JobId">Id job replay bền vững do aggregator tạo. Dùng để đối chiếu phản hồi.</param>
/// <param name="ServiceName">Service cần replay; <c>null</c>/rỗng = tất cả.</param>
/// <param name="From">Mốc đầu (UTC, theo <c>OccurredAt</c>); <c>null</c> = không giới hạn.</param>
/// <param name="To">Mốc cuối (UTC, theo <c>OccurredAt</c>); <c>null</c> = không giới hạn.</param>
/// <param name="RequestedAt">UTC — lúc job được tạo.</param>
public record AuditReplayRequestedEvent(
    Guid JobId,
    string? ServiceName,
    DateTime? From,
    DateTime? To,
    DateTime RequestedAt
) : IntegrationEvent;

/// <summary>
/// GH-728 — một service nguồn báo đã replay xong phần của mình.
/// AuditAggregatorService cộng dồn để cập nhật tiến độ job.
/// </summary>
/// <param name="JobId">Id job replay tương ứng.</param>
/// <param name="ServiceName">Service báo cáo (tên chuẩn trong <c>AuditServiceNames</c>).</param>
/// <param name="RepublishedCount">Số bản ghi audit đã re-publish.</param>
/// <param name="IsSuccess">False nếu service gặp lỗi giữa chừng.</param>
/// <param name="Error">Thông báo lỗi khi <paramref name="IsSuccess"/> = false.</param>
/// <param name="Truncated">
/// True nếu chạm trần an toàn và KHÔNG replay hết khoảng yêu cầu — quan trọng để job không
/// bị báo "Completed" trong khi dữ liệu còn thiếu.
/// </param>
/// <param name="CompletedAt">UTC — lúc service hoàn tất phần của mình.</param>
public record AuditReplayCompletedEvent(
    Guid JobId,
    string ServiceName,
    int RepublishedCount,
    bool IsSuccess,
    string? Error,
    bool Truncated,
    DateTime CompletedAt
) : IntegrationEvent;
