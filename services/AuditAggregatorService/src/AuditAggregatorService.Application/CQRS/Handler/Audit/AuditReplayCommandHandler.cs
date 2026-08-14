using AuditAggregatorService.Application.CQRS.Command.Audit;
using AuditAggregatorService.Application.Interfaces;
using AuditAggregatorService.Domain.Entities;
using MassTransit;
using MediatR;
using Microsoft.Extensions.Logging;
using SharedContracts.Audit;
using SharedContracts.Common.Responses;
using SharedContracts.Events.Audit;
using SharedInfrastructure.Services;

namespace AuditAggregatorService.Application.CQRS.Handler.Audit;

/// <summary>
/// GH-728 — nhận yêu cầu replay audit từ source-of-truth.
///
/// <para><b>Trước đây</b> handler này là stub: trả 202 mà không validate, không lưu gì,
/// không publish gì. Nghĩa là 202 ("đã tiếp nhận, sẽ xử lý") là lời hứa suông — không có
/// cách nào biết ai đã yêu cầu, có chạy không, chạy tới đâu.</para>
///
/// <para><b>Bây giờ</b>: validate → <b>lưu job bền vững</b> → publish
/// <see cref="AuditReplayRequestedEvent"/> → mới trả 202 kèm <c>jobId</c>. Ghi DB hỏng thì
/// trả lỗi chứ không trả 202.</para>
///
/// <para>Mỗi service nguồn tự đọc bảng audit của mình rồi re-publish
/// <see cref="AuditCreatedEventV1"/> giữ nguyên <c>EventId</c>; consumer của aggregator đã
/// idempotent theo <c>EventId</c> (#AUDIT-15) nên chạy lại nhiều lần không sinh bản ghi trùng.</para>
/// </summary>
public class AuditReplayCommandHandler : IRequestHandler<AuditReplayCommand, CommonResponse<object>>
{
    private readonly IAuditAggregatorUnitOfWork _unitOfWork;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<AuditReplayCommandHandler> _logger;

    public AuditReplayCommandHandler(
        IAuditAggregatorUnitOfWork unitOfWork,
        IPublishEndpoint publishEndpoint,
        ICurrentUserService currentUserService,
        ILogger<AuditReplayCommandHandler> logger)
    {
        _unitOfWork = unitOfWork;
        _publishEndpoint = publishEndpoint;
        _currentUserService = currentUserService;
        _logger = logger;
    }

    public async Task<CommonResponse<object>> Handle(AuditReplayCommand request, CancellationToken ct)
    {
        var errors = new List<Errors>();

        // ── Validate ──
        if (!string.IsNullOrWhiteSpace(request.Service) && !AuditServiceNames.IsKnown(request.Service))
        {
            errors.Add(new Errors
            {
                Field = nameof(request.Service),
                Detail = $"Invalid service. Allowed values: {string.Join(", ", AuditServiceNames.All)} (or leave blank for all)."
            });
        }

        var from = NormalizeUtc(request.From);
        var to = NormalizeUtc(request.To);

        if (from.HasValue && to.HasValue && from > to)
        {
            errors.Add(new Errors { Field = nameof(request.From), Detail = "From must be less than or equal to To." });
        }

        // Mốc trong tương lai gần như luôn là gõ nhầm; replay khoảng đó chắc chắn rỗng.
        var now = DateTime.UtcNow;
        if (from > now)
            errors.Add(new Errors { Field = nameof(request.From), Detail = "From must not be in the future." });

        if (errors.Count > 0)
        {
            return new CommonResponse<object>
            {
                IsSuccess = false,
                StatusCode = 400,
                Message = "Invalid replay request.",
                ListErrors = errors
            };
        }

        // ── Lưu job TRƯỚC khi hứa hẹn gì ──
        var normalizedService = string.IsNullOrWhiteSpace(request.Service)
            ? null
            : AuditServiceNames.All.First(s => string.Equals(s, request.Service.Trim(), StringComparison.OrdinalIgnoreCase));

        Guid.TryParse(_currentUserService.UserId, out var actorId);

        var job = new AuditReplayJob
        {
            Id = Guid.NewGuid(),
            ServiceName = normalizedService,
            FromUtc = from,
            ToUtc = to,
            Status = AuditReplayJobStatus.Requested,
            ExpectedResponders = AuditServiceNames.ExpectedResponders(normalizedService),
            RespondedCount = 0,
            RepublishedCount = 0,
            RequestedByAccountId = actorId == Guid.Empty ? null : actorId,
            RequestedAtUtc = now,
        };

        await _unitOfWork.AuditReplayJobs.AddAsync(job);
        await _unitOfWork.SaveChangesAsync(ct);

        // ── Chỉ publish SAU khi job đã nằm trong DB ──
        // Ngược lại, service nguồn có thể báo "xong" cho một job chưa tồn tại.
        await _publishEndpoint.Publish(
            new AuditReplayRequestedEvent(job.Id, normalizedService, from, to, now), ct);

        _logger.LogInformation(
            "Audit replay job {JobId} đã tạo: service={Service} from={From} to={To} expected={Expected}.",
            job.Id, normalizedService ?? "all", from, to, job.ExpectedResponders);

        return new CommonResponse<object>
        {
            IsSuccess = true,
            StatusCode = 202,
            Message = $"Replay job created for service='{normalizedService ?? "all"}'. "
                      + "Track progress at GET /api/admin/audit/replay/{jobId}.",
            Data = new
            {
                jobId = job.Id,
                service = normalizedService,
                from,
                to,
                expectedResponders = job.ExpectedResponders,
                status = job.Status.ToString()
            }
        };
    }

    /// <summary>
    /// Ép về UTC. Client hay gửi mốc không kèm offset; coi là UTC thay vì giờ máy chủ —
    /// nếu không, khoảng replay lệch đúng bằng timezone của container.
    /// </summary>
    private static DateTime? NormalizeUtc(DateTime? value)
    {
        if (value is null)
            return null;

        return value.Value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.Value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value.Value, DateTimeKind.Utc)
        };
    }
}
