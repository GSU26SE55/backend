using AuditAggregatorService.Domain.Entities;
using SharedKernels.Interfaces;

namespace AuditAggregatorService.Application.Interfaces;

/// <summary>
/// UnitOfWork của AuditAggregatorService (Sprint audit) — theo chuẩn các service khác (Shared <see cref="IUnitOfWork"/>
/// + <see cref="IGenericRepository{T}"/>). Read-store chỉ có 1 entity <see cref="AuditAggregate"/>.
/// </summary>
public interface IAuditAggregatorUnitOfWork : IUnitOfWork
{
    IGenericRepository<AuditAggregate> AuditAggregates { get; }

    /// <summary>GH-728 — job replay audit từ source-of-truth.</summary>
    IGenericRepository<AuditReplayJob> AuditReplayJobs { get; }
}
