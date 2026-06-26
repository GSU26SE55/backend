using Microsoft.EntityFrameworkCore.Storage;
using SharedInfrastructure.Persistence.Repositories;
using SharedKernels.Interfaces;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Domain.Entities;
using TicketService.Infrastructure.Persistence;

namespace TicketService.Infrastructure.Implements.Repositories;

public class UnitOfWork : ITicketUnitOfWork
{
    private readonly TicketDbContext _context;
    private IDbContextTransaction? _currentTransaction;

    public UnitOfWork(TicketDbContext context)
    {
        _context = context;
    }

    public IGenericRepository<Ticket> Tickets => new GenericRepository<Ticket>(_context);
    public IGenericRepository<TicketAuditLog> TicketAuditLogs => new GenericRepository<TicketAuditLog>(_context);       // #AUDIT-24
    public IGenericRepository<TicketAuditOutbox> TicketAuditOutboxes => new GenericRepository<TicketAuditOutbox>(_context); // #AUDIT-25
    public IGenericRepository<TicketActivity> TicketActivities => new GenericRepository<TicketActivity>(_context);
    public IGenericRepository<TicketComment> TicketComments => new GenericRepository<TicketComment>(_context);
    public IGenericRepository<TicketAttachment> TicketAttachments => new GenericRepository<TicketAttachment>(_context);
    public IGenericRepository<SlaTimer> SlaTimers => new GenericRepository<SlaTimer>(_context);
    public IGenericRepository<SlaPauseEvent> SlaPauseEvents => new GenericRepository<SlaPauseEvent>(_context);
    public IGenericRepository<MaintenanceLog> MaintenanceLogs => new GenericRepository<MaintenanceLog>(_context);
    public IGenericRepository<OutboxMessage> OutboxMessages => new GenericRepository<OutboxMessage>(_context);
    public IGenericRepository<CustomerAccount> CustomerAccounts => new GenericRepository<CustomerAccount>(_context);
    public IGenericRepository<StaffAccount> StaffAccounts => new GenericRepository<StaffAccount>(_context);
    public IGenericRepository<KnowledgeBaseArticle> KnowledgeBaseArticles => new GenericRepository<KnowledgeBaseArticle>(_context);
    public IGenericRepository<KbArticleVersion> KbArticleVersions => new GenericRepository<KbArticleVersion>(_context);
    public IGenericRepository<TicketKbReference> TicketKbReferences => new GenericRepository<TicketKbReference>(_context);

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
            {
                await _currentTransaction.CommitAsync();
            }
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

    public void Dispose()
    {
        _context.Dispose();
    }

    public async Task RollbackTransactionAsync()
    {
        try
        {
            if (_currentTransaction != null)
            {
                await _currentTransaction.RollbackAsync();
            }
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
}
