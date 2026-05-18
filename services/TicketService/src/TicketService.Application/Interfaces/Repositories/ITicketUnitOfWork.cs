using SharedKernels.Interfaces;
using TicketService.Domain.Entities;

namespace TicketService.Application.Interfaces.Repositories;

public interface ITicketUnitOfWork : IUnitOfWork
{
    IGenericRepository<Ticket> Tickets { get; }
    IGenericRepository<TicketActivity> TicketActivities { get; }
    IGenericRepository<TicketComment> TicketComments { get; }
    IGenericRepository<TicketAttachment> TicketAttachments { get; }
    IGenericRepository<SlaTimer> SlaTimers { get; }
    IGenericRepository<SlaPauseEvent> SlaPauseEvents { get; }
    IGenericRepository<MaintenanceLog> MaintenanceLogs { get; }
    IGenericRepository<OutboxMessage> OutboxMessages { get; }
    IGenericRepository<CustomerAccount> CustomerAccounts { get; }
    IGenericRepository<StaffAccount> StaffAccounts { get; }
    IGenericRepository<KnowledgeBaseArticle> KnowledgeBaseArticles { get; }
    IGenericRepository<TicketKbReference> TicketKbReferences { get; }
}
