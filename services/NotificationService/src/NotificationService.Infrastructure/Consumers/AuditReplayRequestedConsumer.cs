using NotificationService.Application.Interfaces.Repositories;
using NotificationService.Domain.Entities;
using Microsoft.Extensions.Logging;
using SharedContracts.Audit;
using SharedInfrastructure.Bus;

namespace NotificationService.Infrastructure.Consumers;

/// <summary>
/// GH-728 — nhận <c>AuditReplayRequestedEvent</c> và phát lại audit của NotificationService từ bảng
/// <c>NotificationAuditOutbox</c> (source-of-truth cho read-store của AuditAggregatorService).
///
/// <para>Toàn bộ logic nằm ở <see cref="AuditReplayRequestedConsumerBase{T}"/>; lớp này chỉ
/// khai báo service nào và bảng nào.</para>
/// </summary>
public class AuditReplayRequestedConsumer : AuditReplayRequestedConsumerBase<NotificationAuditOutbox>
{
    private readonly INotificationUnitOfWork _unitOfWork;

    public AuditReplayRequestedConsumer(
        INotificationUnitOfWork unitOfWork,
        ILogger<AuditReplayRequestedConsumer> logger)
    {
        _unitOfWork = unitOfWork;
        Logger = logger;
    }

    protected override string ServiceName => AuditServiceNames.Notification;

    protected override ILogger Logger { get; }

    protected override IQueryable<NotificationAuditOutbox> OutboxQuery => _unitOfWork.NotificationAuditOutboxes.GetAllAsync();
}
