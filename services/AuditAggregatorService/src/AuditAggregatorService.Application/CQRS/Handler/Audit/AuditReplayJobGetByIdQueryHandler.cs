using AuditAggregatorService.Application.CQRS.Query.Audit;
using AuditAggregatorService.Application.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Audit;
using SharedContracts.Common.Responses;

namespace AuditAggregatorService.Application.CQRS.Handler.Audit;

/// <summary>
/// GH-728 — trả tiến độ job replay.
/// </summary>
public class AuditReplayJobGetByIdQueryHandler
    : IRequestHandler<AuditReplayJobGetByIdQuery, CommonResponse<AuditReplayJobDto>>
{
    private readonly IAuditAggregatorUnitOfWork _unitOfWork;

    public AuditReplayJobGetByIdQueryHandler(IAuditAggregatorUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<CommonResponse<AuditReplayJobDto>> Handle(
        AuditReplayJobGetByIdQuery request, CancellationToken ct)
    {
        var job = await _unitOfWork.AuditReplayJobs
            .GetAllAsync()
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == request.JobId && !x.IsDeleted, ct);

        if (job is null)
        {
            return new CommonResponse<AuditReplayJobDto>
            {
                IsSuccess = false,
                StatusCode = 404,
                Message = "Không tìm thấy job replay."
            };
        }

        var responded = string.IsNullOrWhiteSpace(job.RespondedServices)
            ? new List<string>()
            : job.RespondedServices
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();

        // Đang chờ ai — câu hỏi đầu tiên khi job treo.
        var expected = job.ServiceName is null
            ? AuditServiceNames.All
            : new[] { job.ServiceName };

        var pending = expected
            .Where(s => !responded.Contains(s, StringComparer.OrdinalIgnoreCase))
            .ToList();

        return new CommonResponse<AuditReplayJobDto>
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = new AuditReplayJobDto
            {
                Id = job.Id.ToString(),
                Service = job.ServiceName,
                From = job.FromUtc,
                To = job.ToUtc,
                Status = job.Status.ToString(),
                ExpectedResponders = job.ExpectedResponders,
                RespondedCount = job.RespondedCount,
                RepublishedCount = job.RepublishedCount,
                Truncated = job.Truncated,
                RespondedServices = responded,
                PendingServices = pending,
                Error = job.Error,
                RequestedByAccountId = job.RequestedByAccountId?.ToString(),
                RequestedAt = job.RequestedAtUtc,
                CompletedAt = job.CompletedAtUtc
            }
        };
    }
}
