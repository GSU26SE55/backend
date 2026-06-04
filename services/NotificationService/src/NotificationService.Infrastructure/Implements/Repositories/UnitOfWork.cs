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
    public IGenericRepository<NotificationPreference> NotificationPreferences => new GenericRepository<NotificationPreference>(_context);
    public IGenericRepository<NotificationTemplate> NotificationTemplates => new GenericRepository<NotificationTemplate>(_context);

    public async Task BeginTransactionAsync()
    {
        if (_currentTransaction != null) return;
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
