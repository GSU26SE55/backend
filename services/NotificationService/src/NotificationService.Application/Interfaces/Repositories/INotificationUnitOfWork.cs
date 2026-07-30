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

    /// <summary>Sprint 6.3 NOTI3-02 (#702) — biên nhận push để đối soát với Expo.</summary>
    IGenericRepository<PushReceipt> PushReceipts { get; }

    /// <summary>Sprint 6.3 NOTI3-04 (#704) — tuỳ chọn theo nhóm × kênh.</summary>
    IGenericRepository<NotificationCategoryPreference> NotificationCategoryPreferences { get; }
}
