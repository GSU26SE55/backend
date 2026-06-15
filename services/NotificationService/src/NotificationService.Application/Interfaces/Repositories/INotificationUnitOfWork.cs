using NotificationService.Domain.Entities;
using SharedKernels.Interfaces;

namespace NotificationService.Application.Interfaces.Repositories;

public interface INotificationUnitOfWork : IUnitOfWork
{
    IGenericRepository<Notification> Notifications { get; }
    IGenericRepository<DeviceToken> DeviceTokens { get; }
    IGenericRepository<NotificationPreference> NotificationPreferences { get; }
    IGenericRepository<NotificationTemplate> NotificationTemplates { get; }
}
