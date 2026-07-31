namespace SharedContracts.Interfaces;

public interface ICacheService
{
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default);
    Task SetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken cancellationToken = default);
    Task RemoveAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sprint 6.3 NOTI3-09 (#709) — đặt key CHỈ KHI chưa tồn tại, trong MỘT lệnh Redis
    /// (<c>SET key val NX EX ttl</c>). Trả <c>true</c> nếu lần này là lần đầu chiếm được key.
    ///
    /// Dùng cho dedup/khoá phân tán. Cặp <c>GetAsync</c> rồi <c>SetAsync</c> KHÔNG tương đương:
    /// hai lời gọi tách rời tạo cửa sổ tranh chấp — 2 message trùng đến gần như đồng thời có thể
    /// cùng đọc thấy "chưa có" và cùng được xử lý.
    /// </summary>
    /// <param name="key"></param>
    /// <param name="value">Giá trị lưu kèm (thường là timestamp, phục vụ chẩn đoán).</param>
    /// <param name="expiration"></param>
    /// <param name="cancellationToken"></param>
    Task<bool> TrySetIfNotExistsAsync(
        string key, string value, TimeSpan expiration, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sprint 6.3 NOTI3-06 (#706) — tăng bộ đếm một cách ATOMIC và trả về giá trị sau khi tăng
    /// (Redis <c>INCR</c>, kèm <c>EXPIRE</c> ở lần tạo đầu tiên).
    ///
    /// Dùng cho rate limit. Cặp <c>GetAsync</c> → +1 → <c>SetAsync</c> KHÔNG dùng được: hai request
    /// song song cùng đọc N rồi cùng ghi N+1 ⇒ đếm hụt và hạn mức bị vượt âm thầm.
    ///
    /// TTL chỉ được đặt khi key vừa được tạo, để cửa sổ không bị đẩy lùi mỗi lần tăng.
    /// </summary>
    /// <returns>Giá trị bộ đếm sau khi tăng.</returns>
    Task<long> IncrementAsync(string key, TimeSpan expiration, CancellationToken cancellationToken = default);
}
