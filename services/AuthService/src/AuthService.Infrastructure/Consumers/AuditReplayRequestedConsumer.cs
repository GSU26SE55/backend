using AuthService.Application.Interfaces.Repositories;
using AuthService.Domain.Entities;
using Microsoft.Extensions.Logging;
using SharedContracts.Audit;
using SharedInfrastructure.Bus;

namespace AuthService.Infrastructure.Consumers;

/// <summary>
/// GH-728 — nhận <c>AuditReplayRequestedEvent</c> và phát lại audit của AuthService từ bảng
/// <c>AuditOutbox</c> (source-of-truth cho read-store của AuditAggregatorService).
///
/// <para>Toàn bộ logic nằm ở <see cref="AuditReplayRequestedConsumerBase{T}"/>; lớp này chỉ
/// khai báo service nào và bảng nào.</para>
/// </summary>
public class AuditReplayRequestedConsumer : AuditReplayRequestedConsumerBase<AuditOutbox>
{
    private readonly IAuthUnitOfWork _unitOfWork;

    public AuditReplayRequestedConsumer(
        IAuthUnitOfWork unitOfWork,
        ILogger<AuditReplayRequestedConsumer> logger)
    {
        _unitOfWork = unitOfWork;
        Logger = logger;
    }

    protected override string ServiceName => AuditServiceNames.Auth;

    protected override ILogger Logger { get; }

    protected override IQueryable<AuditOutbox> OutboxQuery => _unitOfWork.AuditOutboxes.GetAllAsync();
}
