using TicketService.Application.Interfaces.Repositories;
using TicketService.Domain.Entities;
using Microsoft.Extensions.Logging;
using SharedContracts.Audit;
using SharedInfrastructure.Bus;

namespace TicketService.Infrastructure.Consumers;

/// <summary>
/// GH-728 — nhận <c>AuditReplayRequestedEvent</c> và phát lại audit của TicketService từ bảng
/// <c>TicketAuditOutbox</c> (source-of-truth cho read-store của AuditAggregatorService).
///
/// <para>Toàn bộ logic nằm ở <see cref="AuditReplayRequestedConsumerBase{T}"/>; lớp này chỉ
/// khai báo service nào và bảng nào.</para>
/// </summary>
public class TicketAuditReplayRequestedConsumer : AuditReplayRequestedConsumerBase<TicketAuditOutbox>
{
    private readonly ITicketUnitOfWork _unitOfWork;

    public TicketAuditReplayRequestedConsumer(
        ITicketUnitOfWork unitOfWork,
        ILogger<TicketAuditReplayRequestedConsumer> logger)
    {
        _unitOfWork = unitOfWork;
        Logger = logger;
    }

    protected override string ServiceName => AuditServiceNames.Ticket;

    protected override ILogger Logger { get; }

    protected override IQueryable<TicketAuditOutbox> OutboxQuery => _unitOfWork.TicketAuditOutboxes.GetAllAsync();
}
