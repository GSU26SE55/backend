using Microsoft.EntityFrameworkCore.Storage;
using SharedInfrastructure.Persistence.Repositories;
using SharedKernels.Interfaces;
using SmsService.Application.Interfaces.Repositories;
using SmsService.Domain.Entities;
using SmsService.Infrastructure.Persistence;

namespace SmsService.Infrastructure.Implements.Repositories;

/// <summary>
/// UoW impl cho SmsService — pattern khớp <c>AuthService.Infrastructure.Implements.Repositories.UnitOfWork</c>.
/// </summary>
public class SmsUnitOfWork : ISmsUnitOfWork
{
    private readonly SmsDbContext _context;
    private IDbContextTransaction? _currentTransaction;

    public SmsUnitOfWork(SmsDbContext context)
    {
        _context = context;
    }

    public IGenericRepository<SmsMessage> SmsMessages => new GenericRepository<SmsMessage>(_context);
    public IGenericRepository<SmsGatewayDevice> SmsGatewayDevices => new GenericRepository<SmsGatewayDevice>(_context);
    public IGenericRepository<SmsAuditLog> SmsAuditLogs => new GenericRepository<SmsAuditLog>(_context);
    public IGenericRepository<OutboxMessage> OutboxMessages => new GenericRepository<OutboxMessage>(_context);

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

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return await _context.SaveChangesAsync(cancellationToken);
    }

    public void Dispose() => _context.Dispose();
}
