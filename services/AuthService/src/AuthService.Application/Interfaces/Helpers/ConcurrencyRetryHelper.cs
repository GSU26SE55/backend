using Microsoft.EntityFrameworkCore;

namespace AuthService.Application.Interfaces.Helpers;

/// <summary>
/// #AUTH-34: Retry helper cho <see cref="DbUpdateConcurrencyException"/>.
///
/// Pattern dùng: handler bọc business logic + SaveChanges trong delegate, helper retry tối đa N lần.
/// Trước mỗi retry, caller phải reload entity mới (helper truyền retry attempt để caller biết refresh).
///
/// Áp dụng cho update Account cùng lúc bởi 2 admin (xmin token mismatch → 1 fail).
/// Read-only query không cần retry. Insert không cần retry. Delete soft-delete dùng retry tương tự.
/// </summary>
public static class ConcurrencyRetryHelper
{
    private const int DefaultMaxAttempts = 3;

    /// <summary>
    /// Chạy <paramref name="operation"/>; nếu throw <see cref="DbUpdateConcurrencyException"/> và còn quota,
    /// gọi <paramref name="reload"/> rồi retry. <paramref name="operation"/> trả về kết quả của caller.
    /// </summary>
    public static async Task<T> ExecuteAsync<T>(
        Func<int, CancellationToken, Task<T>> operation,
        Func<CancellationToken, Task>? reload = null,
        int maxAttempts = DefaultMaxAttempts,
        CancellationToken cancellationToken = default)
    {
        if (maxAttempts < 1)
            maxAttempts = 1;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                return await operation(attempt, cancellationToken);
            }
            catch (DbUpdateConcurrencyException) when (attempt < maxAttempts)
            {
                if (reload != null)
                    await reload(cancellationToken);
            }
        }

        // Không thể tới đây — last attempt throw exception sẽ propagate ra ngoài.
        throw new InvalidOperationException("ConcurrencyRetryHelper exited unexpected branch.");
    }
}
