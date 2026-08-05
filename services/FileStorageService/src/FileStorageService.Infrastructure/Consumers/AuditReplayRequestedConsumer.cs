using FileStorageService.Application.Interfaces;
using FileStorageService.Domain.Entities;
using Microsoft.Extensions.Logging;
using SharedContracts.Audit;
using SharedInfrastructure.Bus;

namespace FileStorageService.Infrastructure.Consumers;

/// <summary>
/// GH-728 — nhận <c>AuditReplayRequestedEvent</c> và phát lại audit của FileStorageService từ bảng
/// <c>FileAuditOutbox</c> (source-of-truth cho read-store của AuditAggregatorService).
///
/// <para>Toàn bộ logic nằm ở <see cref="AuditReplayRequestedConsumerBase{T}"/>; lớp này chỉ
/// khai báo service nào và bảng nào.</para>
/// </summary>
public class AuditReplayRequestedConsumer : AuditReplayRequestedConsumerBase<FileAuditOutbox>
{
    private readonly IFileStorageUnitOfWork _unitOfWork;

    public AuditReplayRequestedConsumer(
        IFileStorageUnitOfWork unitOfWork,
        ILogger<AuditReplayRequestedConsumer> logger)
    {
        _unitOfWork = unitOfWork;
        Logger = logger;
    }

    protected override string ServiceName => AuditServiceNames.FileStorage;

    protected override ILogger Logger { get; }

    protected override IQueryable<FileAuditOutbox> OutboxQuery => _unitOfWork.FileAuditOutboxes.GetAllAsync();
}
