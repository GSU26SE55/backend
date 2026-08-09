using Microsoft.Extensions.Logging;
using SharedContracts.Audit;
using SharedInfrastructure.Bus;
using SmsService.Application.Interfaces.Repositories;
using SmsService.Domain.Entities;

namespace SmsService.Infrastructure.Consumers;

/// <summary>
/// GH-728 — nhận <c>AuditReplayRequestedEvent</c> và phát lại audit của SmsService từ bảng
/// <c>SmsAuditOutbox</c> (source-of-truth cho read-store của AuditAggregatorService).
///
/// <para>Toàn bộ logic nằm ở <see cref="AuditReplayRequestedConsumerBase{T}"/>; lớp này chỉ
/// khai báo service nào và bảng nào.</para>
/// </summary>
public class SmsAuditReplayRequestedConsumer : AuditReplayRequestedConsumerBase<SmsAuditOutbox>
{
    private readonly ISmsUnitOfWork _unitOfWork;

    public SmsAuditReplayRequestedConsumer(
        ISmsUnitOfWork unitOfWork,
        ILogger<SmsAuditReplayRequestedConsumer> logger)
    {
        _unitOfWork = unitOfWork;
        Logger = logger;
    }

    protected override string ServiceName => AuditServiceNames.Sms;

    protected override ILogger Logger { get; }

    protected override IQueryable<SmsAuditOutbox> OutboxQuery => _unitOfWork.SmsAuditOutboxes.GetAllAsync();
}
