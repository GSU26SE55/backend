namespace SharedContracts.Common.Requests;

/// <summary>
/// Helper cho server-side sort. Chuẩn hoá hướng sort (<c>SortDir</c>).
/// <para>
/// Việc map <c>SortBy</c> (string từ client) → property entity PHẢI làm bằng
/// switch-case whitelist trong từng handler — KHÔNG dùng dynamic LINQ / reflection
/// theo string thô (tránh injection + sort theo cột không index).
/// </para>
/// </summary>
public static class SortHelper
{
    /// <summary>
    /// true nếu sort giảm dần. Mặc định desc; chỉ trả false khi <paramref name="sortDir"/>
    /// bằng "asc" (không phân biệt hoa/thường). Mọi giá trị lạ → desc.
    /// </summary>
    public static bool IsDescending(string? sortDir)
        => !string.Equals(sortDir?.Trim(), "asc", StringComparison.OrdinalIgnoreCase);
}
