using FileStorageService.Application.Interfaces;
using FileStorageService.Domain.Entities;
using FileStorageService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Storage;
using SharedInfrastructure.Persistence.Repositories;
using SharedKernels.Interfaces;

namespace FileStorageService.Infrastructure.Implements.Repositories;

public class UnitOfWork : IFileStorageUnitOfWork
{
    private readonly ApplicationDbContext _context;
    private IDbContextTransaction? _currentTransaction;

    public UnitOfWork(ApplicationDbContext context)
    {
        _context = context;
    }

    public IGenericRepository<UploadedFile> UploadedFiles => new GenericRepository<UploadedFile>(_context);
    public IGenericRepository<FileAuditLog> FileAuditLogs => new GenericRepository<FileAuditLog>(_context);       // #AUDIT-29
    public IGenericRepository<FileAuditOutbox> FileAuditOutboxes => new GenericRepository<FileAuditOutbox>(_context); // #AUDIT-29

    public async Task BeginTransactionAsync()
    {
        if (_currentTransaction is not null)
            return;

        _currentTransaction = await _context.Database.BeginTransactionAsync();
    }

    public async Task CommitTransactionAsync()
    {
        try
        {
            await _context.SaveChangesAsync();

            if (_currentTransaction is not null)
                await _currentTransaction.CommitAsync();
        }
        catch
        {
            await RollbackTransactionAsync();
            throw;
        }
        finally
        {
            if (_currentTransaction is not null)
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
            if (_currentTransaction is not null)
                await _currentTransaction.RollbackAsync();
        }
        finally
        {
            if (_currentTransaction is not null)
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

    public void Dispose()
    {
        _context.Dispose();
    }
}
