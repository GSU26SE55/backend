using NotificationService.Domain.Entities;
using SharedKernels.Interfaces;

namespace NotificationService.Application.Interfaces.Repositories;

public interface INotificationUnitOfWork : IUnitOfWork
{
    IGenericRepository<Notification> Notifications { get; }
    IGenericRepository<DeviceToken> DeviceTokens { get; }
    IGenericRepository<NotificationAuditLog> NotificationAuditLogs { get; }       // Sprint audit #AUDIT-34
    IGenericRepository<NotificationAuditOutbox> NotificationAuditOutboxes { get; } // Sprint audit #AUDIT-34
    IGenericRepository<NotificationPreference> NotificationPreferences { get; }
    IGenericRepository<NotificationTemplate> NotificationTemplates { get; }
    IGenericRepository<AccountReadModel> Accounts { get; }
}
