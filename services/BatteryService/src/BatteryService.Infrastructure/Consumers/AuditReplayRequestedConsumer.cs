using BatteryService.Application.Interfaces;
using BatteryService.Domain.Entities;
using Microsoft.Extensions.Logging;
using SharedContracts.Audit;
using SharedInfrastructure.Bus;

namespace BatteryService.Infrastructure.Consumers;

/// <summary>
/// GH-728 — nhận <c>AuditReplayRequestedEvent</c> và phát lại audit của BatteryService từ bảng
/// <c>BatteryAuditOutbox</c> (source-of-truth cho read-store của AuditAggregatorService).
///
/// <para>Toàn bộ logic nằm ở <see cref="AuditReplayRequestedConsumerBase{T}"/>; lớp này chỉ
/// khai báo service nào và bảng nào.</para>
/// </summary>
public class AuditReplayRequestedConsumer : AuditReplayRequestedConsumerBase<BatteryAuditOutbox>
{
    private readonly IBatteryUnitOfWork _unitOfWork;

    public AuditReplayRequestedConsumer(
        IBatteryUnitOfWork unitOfWork,
        ILogger<AuditReplayRequestedConsumer> logger)
    {
        _unitOfWork = unitOfWork;
        Logger = logger;
    }

    protected override string ServiceName => AuditServiceNames.Battery;

    protected override ILogger Logger { get; }

    protected override IQueryable<BatteryAuditOutbox> OutboxQuery => _unitOfWork.BatteryAuditOutboxes.GetAllAsync();
}
