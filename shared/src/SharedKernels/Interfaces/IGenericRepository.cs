using System.Linq.Expressions;

namespace SharedKernels.Interfaces;

public interface IGenericRepository<T> where T : class
{
    Task<T?> GetByIdAsync(object id);

    /// <summary>
    /// Trả về <see cref="IQueryable{T}"/> với EF tracking BẬT (giữ behavior cũ — không break callers hiện tại).
    /// Read-only query nên dùng <see cref="GetAllAsync(bool)"/> với tracking=false (#AUTH-35).
    /// </summary>
    IQueryable<T> GetAllAsync();

    /// <summary>
    /// #AUTH-35: opt-in/out tracking. <paramref name="tracking"/>=false → <c>AsNoTracking()</c> cho
    /// query read-only (list/search dashboard). Khi handler cần Update entity sau lookup, dùng overload
    /// không tham số hoặc truyền <paramref name="tracking"/>=true.
    /// </summary>
    IQueryable<T> GetAllAsync(bool tracking);

    IQueryable<T> FindAsync(Expression<Func<T, bool>> predicate);
    Task AddAsync(T entity);
    void UpdateAsync(T entity);
    void DeleteAsync(T entity);

    Task<bool> AnyAsync(Expression<Func<T, bool>> predicate);

    /// <summary>
    /// #AUTH-34: Reload entity từ database (overwrite tracker với current values từ DB).
    /// Dùng sau khi <see cref="System.Data.Common.DbException"/> hoặc <c>DbUpdateConcurrencyException</c>
    /// để retry-safe — sau Reload, entity reflect state DB hiện tại + RowVersion token mới.
    /// No-op nếu entity chưa được track (Detached).
    /// </summary>
    Task ReloadAsync(T entity, CancellationToken cancellationToken = default);
}
