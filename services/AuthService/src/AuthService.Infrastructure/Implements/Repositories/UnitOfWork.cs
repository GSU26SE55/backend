using AuthService.Application.Interfaces.Repositories;
using AuthService.Domain.Entities;
using AuthService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Storage;
using SharedInfrastructure.Persistence.Repositories;
using SharedKernels.Interfaces;

namespace AuthService.Infrastructure.Implements.Repositories;

public class UnitOfWork : IAuthUnitOfWork
{
    private readonly ApplicationDbContext _context;
    private IDbContextTransaction? _currentTransaction;
    public UnitOfWork(ApplicationDbContext context)
    {
        _context = context;
    }

    public IGenericRepository<Account> Accounts => new GenericRepository<Account>(_context);
    public IGenericRepository<Role> Roles => new GenericRepository<Role>(_context);
    public IGenericRepository<RefreshToken> RefreshTokens => new GenericRepository<RefreshToken>(_context);
    public IGenericRepository<AccountRole> AccountRoles => new GenericRepository<AccountRole>(_context);
    public IGenericRepository<AuditLog> AuditLogs => new GenericRepository<AuditLog>(_context);
    public IGenericRepository<LoginAttempt> LoginAttempts => new GenericRepository<LoginAttempt>(_context);
    public IGenericRepository<Permission> Permissions => new GenericRepository<Permission>(_context);
    public IGenericRepository<RolePermission> RolePermissions => new GenericRepository<RolePermission>(_context);

    public async Task BeginTransactionAsync()
    {
        if (_currentTransaction != null)
        {
            return; // Đã có transaction đang chạy thì không tạo mới
        }

        _currentTransaction = await _context.Database.BeginTransactionAsync();
    }

    public async Task CommitTransactionAsync()
    {
        try
        {
            // Luôn SaveChanges trước khi Commit
            await _context.SaveChangesAsync();

            if (_currentTransaction != null)
            {
                await _currentTransaction.CommitAsync();
            }
        }
        catch
        {
            await RollbackTransactionAsync();
            throw; // Ném lỗi ra để Middleware xử lý
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
