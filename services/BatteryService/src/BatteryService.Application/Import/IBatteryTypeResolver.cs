namespace BatteryService.Application.Import;

/// <summary>Tìm hoặc tạo loại pin tương ứng với một dòng pin trong file nhập.</summary>
public interface IBatteryTypeResolver
{
    /// <summary>
    /// Trả về định danh loại pin dùng cho dòng này.
    /// </summary>
    /// <remarks>
    /// Loại pin mới (nếu có) chỉ được đưa vào ngữ cảnh dữ liệu, <b>không</b> lưu xuống — người gọi
    /// quyết định thời điểm lưu để loại pin và pin cùng nằm trong một giao dịch.
    /// </remarks>
    Task<BatteryTypeResolution> ResolveAsync(ValidatedAsset asset, CancellationToken cancellationToken);

    /// <summary>Xoá bộ nhớ tạm giữa hai lô. Bắt buộc gọi khi tái dùng một thể hiện cho nhiều lô.</summary>
    void Reset();
}

/// <param name="BatteryTypeId">Định danh loại pin dùng được; rỗng khi không giải được.</param>
/// <param name="WasCreated">True khi loại pin vừa được tạo trong lượt này.</param>
/// <param name="FailureReason">Lý do không giải được; null khi thành công.</param>
public readonly record struct BatteryTypeResolution(Guid BatteryTypeId, bool WasCreated, string? FailureReason)
{
    public bool IsSuccess => FailureReason is null && BatteryTypeId != Guid.Empty;

    public static BatteryTypeResolution Failed(string reason) => new(Guid.Empty, false, reason);

    public static BatteryTypeResolution Existing(Guid id) => new(id, false, null);

    public static BatteryTypeResolution Created(Guid id) => new(id, true, null);
}
