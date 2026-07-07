using SharedKernels.Interfaces;
using TicketService.Domain.Entities;

namespace TicketService.Application.Interfaces.Repositories;

public interface ITicketUnitOfWork : IUnitOfWork
{
    IGenericRepository<Ticket> Tickets { get; }
    IGenericRepository<TicketAuditLog> TicketAuditLogs { get; }       // Sprint audit #AUDIT-24
    IGenericRepository<TicketAuditOutbox> TicketAuditOutboxes { get; } // Sprint audit #AUDIT-25
    IGenericRepository<TicketActivity> TicketActivities { get; }
    IGenericRepository<TicketChat> TicketChats { get; }
    IGenericRepository<TicketChatEdit> TicketChatEdits { get; }
    IGenericRepository<TicketAttachment> TicketAttachments { get; }
    IGenericRepository<SlaTimer> SlaTimers { get; }
    IGenericRepository<SlaPauseEvent> SlaPauseEvents { get; }
    IGenericRepository<MaintenanceLog> MaintenanceLogs { get; }
    IGenericRepository<OutboxMessage> OutboxMessages { get; }
    IGenericRepository<CustomerAccount> CustomerAccounts { get; }
    IGenericRepository<StaffAccount> StaffAccounts { get; }
    IGenericRepository<KnowledgeBaseArticle> KnowledgeBaseArticles { get; }
    IGenericRepository<KbArticleVersion> KbArticleVersions { get; }
    IGenericRepository<TicketKbReference> TicketKbReferences { get; }
    IGenericRepository<TicketParticipant> TicketParticipants { get; }
    IGenericRepository<TicketChatMention> TicketChatMentions { get; }
    IGenericRepository<TicketChatReaction> TicketChatReactions { get; }
    IGenericRepository<TicketChatRead> TicketChatReads { get; }
    IGenericRepository<TicketChatHide> TicketChatHides { get; }
    IGenericRepository<ChatTemplate> ChatTemplates { get; }
    IGenericRepository<ChatAiSuggestion> ChatAiSuggestions { get; }
    IGenericRepository<TicketChatTranslation> TicketChatTranslations { get; }
    IGenericRepository<TicketChatTranslationUser> ChatTranslationUsers { get; }
}
