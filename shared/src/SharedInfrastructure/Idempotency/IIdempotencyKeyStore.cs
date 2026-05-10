namespace SharedInfrastructure.Idempotency;

public interface IIdempotencyKeyStore
{
    /// <summary>
    /// Reserve key. Trả true nếu reserve thành công (lần đầu), false nếu key đã tồn tại (đang xử lý hoặc đã có response cached).
    /// </summary>
    Task<bool> TryReserveAsync(string key, TimeSpan ttl, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lưu response đã capture vào cache để replay cho request lặp.
    /// </summary>
    Task SaveResponseAsync(string key, int statusCode, string body, TimeSpan ttl, CancellationToken cancellationToken = default);

    /// <summary>
    /// Đọc response cached. null = chưa có (đang reserve nhưng chưa save response).
    /// </summary>
    Task<CachedIdempotencyResponse?> TryGetResponseAsync(string key, CancellationToken cancellationToken = default);
}

public record CachedIdempotencyResponse(int StatusCode, string Body);
