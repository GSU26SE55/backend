namespace SharedInfrastructure.Leasing;

/// <summary>
/// GH-793 — quyền chạy độc quyền một công việc nền, có chủ sở hữu rõ ràng.
/// </summary>
/// <remarks>
/// <para>
/// Khuôn cũ rải khắp các background service là <c>GET</c> rồi <c>SET</c>: đọc thấy khoá trống thì
/// ghi tên mình vào. Hai replica cùng đọc thấy trống trong cùng một khoảnh khắc thì cả hai đều tự
/// coi là chủ, và cùng gửi một thông báo.
/// </para>
/// <para>
/// Ba phép ở đây đều là một lệnh nguyên tử phía Redis, và đều đối chiếu <b>token chủ sở hữu</b>:
/// không ai gia hạn hay nhả được quyền của người khác. Thiếu đối chiếu đó, một instance đã mất quyền
/// (vì treo quá lâu) khi tỉnh lại sẽ nhả mất quyền của chủ mới.
/// </para>
/// </remarks>
public interface IDistributedLease
{
    /// <summary>
    /// Giành quyền, hoặc gia hạn nếu <paramref name="owner"/> đang là chủ.
    /// </summary>
    /// <returns><c>true</c> nếu sau lời gọi này <paramref name="owner"/> là chủ.</returns>
    Task<bool> TryAcquireAsync(string key, string owner, TimeSpan ttl, CancellationToken ct = default);

    /// <summary>
    /// Gia hạn quyền ĐANG giữ. Trả <c>false</c> nếu đã mất quyền vào tay người khác.
    /// </summary>
    /// <remarks>
    /// Cần cho những lượt chạy dài: một batch gọi hàng trăm lần ra ngoài có thể vượt quá thời hạn,
    /// lúc đó instance khác giành quyền và hai bên cùng làm một việc. Trả <c>false</c> là tín hiệu
    /// để dừng lượt đang chạy giữa chừng.
    /// </remarks>
    Task<bool> TryRenewAsync(string key, string owner, TimeSpan ttl, CancellationToken ct = default);

    /// <summary>Nhả quyền — chỉ khi <paramref name="owner"/> thật sự đang là chủ.</summary>
    Task ReleaseAsync(string key, string owner, CancellationToken ct = default);
}
