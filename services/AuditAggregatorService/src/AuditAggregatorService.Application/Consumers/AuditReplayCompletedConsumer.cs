using AuditAggregatorService.Application.Interfaces;
using AuditAggregatorService.Domain.Entities;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SharedContracts.Events.Audit;

namespace AuditAggregatorService.Application.Consumers;

/// <summary>
/// GH-728 — nhận báo cáo "đã replay xong" từ từng service nguồn và cộng dồn vào job.
///
/// <para><b>Đồng thời:</b> khi replay tất cả, 6 service báo về gần như cùng lúc và cùng ghi
/// một dòng job. Dòng job dùng <c>xmin</c> làm concurrency token; ở đây bắt
/// <see cref="DbUpdateConcurrencyException"/> rồi đọc lại + thử lại, nếu không số đếm của
/// service báo sau sẽ đè mất service báo trước và job treo vĩnh viễn ở InProgress.</para>
///
/// <para><b>Idempotent:</b> MassTransit có thể giao lại message (redelivery). Mỗi service chỉ
/// được tính MỘT lần — kiểm bằng danh sách <c>RespondedServices</c> chứ không phải chỉ tăng
/// biến đếm.</para>
/// </summary>
public class AuditReplayCompletedConsumer : IConsumer<AuditReplayCompletedEvent>
{
    private const int MaxConcurrencyRetries = 5;

    private readonly IAuditAggregatorUnitOfWork _unitOfWork;
    private readonly ILogger<AuditReplayCompletedConsumer> _logger;

    public AuditReplayCompletedConsumer(
        IAuditAggregatorUnitOfWork unitOfWork,
        ILogger<AuditReplayCompletedConsumer> logger)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<AuditReplayCompletedEvent> context)
    {
        var evt = context.Message;
        var ct = context.CancellationToken;

        for (var attempt = 1; attempt <= MaxConcurrencyRetries; attempt++)
        {
            var job = await _unitOfWork.AuditReplayJobs
                .GetAllAsync()
                .FirstOrDefaultAsync(x => x.Id == evt.JobId && !x.IsDeleted, ct);

            if (job is null)
            {
                // Không ném: ném sẽ khiến MassTransit retry mãi một job không tồn tại.
                _logger.LogWarning(
                    "Nhận AuditReplayCompleted cho job {JobId} không tồn tại (service={Service}). Bỏ qua.",
                    evt.JobId, evt.ServiceName);
                return;
            }

            var responded = SplitServices(job.RespondedServices);
            if (responded.Contains(evt.ServiceName, StringComparer.OrdinalIgnoreCase))
            {
                _logger.LogDebug(
                    "Service {Service} đã báo cáo cho job {JobId} trước đó — bỏ qua (idempotent).",
                    evt.ServiceName, evt.JobId);
                return;
            }

            responded.Add(evt.ServiceName);
            job.RespondedServices = string.Join(",", responded);
            job.RespondedCount = responded.Count;
            job.RepublishedCount += evt.RepublishedCount;
            job.Truncated |= evt.Truncated;

            if (!evt.IsSuccess)
            {
                var line = $"{evt.ServiceName}: {evt.Error ?? "unknown error"}";
                job.Error = string.IsNullOrEmpty(job.Error) ? line : $"{job.Error} | {line}";
            }

            if (job.RespondedCount >= job.ExpectedResponders)
            {
                // "Completed" chỉ khi KHÔNG có lỗi và KHÔNG bị cắt ngắn — nếu không, người
                // vận hành sẽ tin là đã phục hồi đủ trong khi dữ liệu vẫn thiếu.
                job.Status = job.Error is null && !job.Truncated
                    ? AuditReplayJobStatus.Completed
                    : AuditReplayJobStatus.CompletedWithErrors;
                job.CompletedAtUtc = DateTime.UtcNow;
            }
            else
            {
                job.Status = AuditReplayJobStatus.InProgress;
            }

            _unitOfWork.AuditReplayJobs.UpdateAsync(job);

            try
            {
                await _unitOfWork.SaveChangesAsync(ct);

                _logger.LogInformation(
                    "Replay job {JobId}: {Service} báo xong ({Count} bản ghi). Tiến độ {Responded}/{Expected}, trạng thái {Status}.",
                    job.Id, evt.ServiceName, evt.RepublishedCount, job.RespondedCount, job.ExpectedResponders, job.Status);
                return;
            }
            catch (DbUpdateConcurrencyException) when (attempt < MaxConcurrencyRetries)
            {
                // Service khác vừa ghi trước. Đọc lại và cộng dồn lên bản mới nhất.
                _logger.LogDebug(
                    "Xung đột đồng thời khi cập nhật job {JobId} (lần {Attempt}) — thử lại.",
                    evt.JobId, attempt);
            }
        }

        // Hết lượt thử: ném để MassTransit retry sau, KHÔNG nuốt (sẽ mất tiến độ vĩnh viễn).
        throw new InvalidOperationException(
            $"Could not update replay job {evt.JobId} progress after {MaxConcurrencyRetries} attempts due to concurrency conflicts.");
    }

    private static List<string> SplitServices(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? new List<string>()
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
}
