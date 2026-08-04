using Microsoft.EntityFrameworkCore.Storage;
using NotificationService.Application.Interfaces.Repositories;
using NotificationService.Domain.Entities;
using NotificationService.Infrastructure.Persistence;
using SharedInfrastructure.Persistence.Repositories;
using SharedKernels.Interfaces;

namespace NotificationService.Infrastructure.Implements.Repositories;

public class UnitOfWork : INotificationUnitOfWork
{
    private readonly ApplicationDbContext _context;
    private IDbContextTransaction? _currentTransaction;

    public UnitOfWork(ApplicationDbContext context)
    {
        _context = context;
    }

    public IGenericRepository<Notification> Notifications => new GenericRepository<Notification>(_context);
    public IGenericRepository<DeviceToken> DeviceTokens => new GenericRepository<DeviceToken>(_context);
    public IGenericRepository<NotificationAuditLog> NotificationAuditLogs => new GenericRepository<NotificationAuditLog>(_context);       // #AUDIT-34
    public IGenericRepository<NotificationAuditOutbox> NotificationAuditOutboxes => new GenericRepository<NotificationAuditOutbox>(_context); // #AUDIT-34
    public IGenericRepository<NotificationPreference> NotificationPreferences => new GenericRepository<NotificationPreference>(_context);
    public IGenericRepository<NotificationTemplate> NotificationTemplates => new GenericRepository<NotificationTemplate>(_context);
    public IGenericRepository<AccountReadModel> Accounts => new GenericRepository<AccountReadModel>(_context);
    public IGenericRepository<PushReceipt> PushReceipts => new GenericRepository<PushReceipt>(_context); // Sprint 6.3 NOTI3-02 (#702)
    public IGenericRepository<NotificationCategoryPreference> NotificationCategoryPreferences => new GenericRepository<NotificationCategoryPreference>(_context); // Sprint 6.3 NOTI3-04 (#704)
    public IGenericRepository<NotificationGroup> NotificationGroups => new GenericRepository<NotificationGroup>(_context);                   // Sprint 6.4 NOTI4-01
    public IGenericRepository<NotificationGroupMember> NotificationGroupMembers => new GenericRepository<NotificationGroupMember>(_context); // Sprint 6.4 NOTI4-01
    public IGenericRepository<NotificationBatch> NotificationBatches => new GenericRepository<NotificationBatch>(_context);                  // Sprint 6.4 NOTI4-06
    public IGenericRepository<NotificationBatchTarget> NotificationBatchTargets => new GenericRepository<NotificationBatchTarget>(_context); // Sprint 6.4 NOTI4-06

    public async Task BeginTransactionAsync()
    {
        if (_currentTransaction != null)
            return;
        _currentTransaction = await _context.Database.BeginTransactionAsync();
    }

    public async Task CommitTransactionAsync()
    {
        try
        {
            await _context.SaveChangesAsync();
            if (_currentTransaction != null)
                await _currentTransaction.CommitAsync();
        }
        catch
        {
            await RollbackTransactionAsync();
            throw;
        }
        finally
        {
            if (_currentTransaction != null)
            {
                await _currentTransaction.DisposeAsync();
                _currentTransaction = null;
            }
        }
    }

    public async Task RollbackTransactionAsync()
    {
        try
        {
            if (_currentTransaction != null)
                await _currentTransaction.RollbackAsync();
        }
        finally
        {
            if (_currentTransaction != null)
            {
                await _currentTransaction.DisposeAsync();
                _currentTransaction = null;
            }
        }
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        => _context.SaveChangesAsync(cancellationToken);

    public void Dispose() => _context.Dispose();
}
