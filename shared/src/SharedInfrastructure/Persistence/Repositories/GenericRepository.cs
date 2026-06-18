using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using SharedKernels.Interfaces;

namespace SharedInfrastructure.Persistence.Repositories;

public class GenericRepository<T> : IGenericRepository<T> where T : class
{
    private readonly DbContext _context;
    private readonly DbSet<T> _dbSet;
    public GenericRepository(DbContext context)
    {
        _context = context;
        _dbSet = _context.Set<T>();
    }

    public async Task<T?> GetByIdAsync(object id)
    {
        return await _dbSet.FindAsync(id);
    }

    public IQueryable<T> GetAllAsync()
    {
        return _dbSet.AsQueryable();
    }

    /// <summary>#AUTH-35: opt-in tracking. tracking=false dùng AsNoTracking() cho read-only query.</summary>
    public IQueryable<T> GetAllAsync(bool tracking)
    {
        return tracking ? _dbSet.AsQueryable() : _dbSet.AsNoTracking();
    }

    public IQueryable<T> FindAsync(Expression<Func<T, bool>> predicate)
    {
        return _dbSet.Where(predicate);
    }

    public async Task AddAsync(T entity)
    {
        await _dbSet.AddAsync(entity);
    }
    public void UpdateAsync(T entity)
    {
        _dbSet.Update(entity);
    }

    public void DeleteAsync(T entity)
    {
        if (_context.Entry(entity).State == EntityState.Detached)
            _dbSet.Attach(entity);
        _dbSet.Remove(entity);
    }

    public async Task<bool> AnyAsync(Expression<Func<T, bool>> predicate)
    {
        return await _dbSet.AnyAsync(predicate);
    }
}
