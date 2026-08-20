using BatteryService.Application.Interfaces;
using BatteryService.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using BatteryTypeEntity = BatteryService.Domain.Entities.BatteryType;

namespace BatteryService.Application.Import;

/// <summary>
/// Hiện thực <see cref="IBatteryTypeResolver"/>: khớp theo tên, thiếu thì tạo mới nếu file đủ thông số.
/// </summary>
/// <remarks>
/// <para>
/// Khớp theo <c>Name</c> vì đó là cột đã có ràng buộc duy nhất trong cơ sở dữ liệu. So sánh không
/// phân biệt hoa thường — đối tác gõ "US3000C" hay "us3000c" đều phải trỏ về cùng một loại.
/// </para>
/// <para>
/// <b>Bẫy đã xử lý:</b> nhiều dòng pin trong cùng một lô thường dùng chung một loại pin mới. Loại
/// pin vừa tạo chưa được lưu xuống nên truy vấn không thấy nó, và nếu chỉ dựa vào truy vấn thì mỗi
/// dòng lại tạo thêm một loại pin trùng tên — tới lúc lưu sẽ vi phạm ràng buộc duy nhất và làm
/// hỏng cả lô. Bộ nhớ tạm bên dưới giữ những loại đã giải trong lượt này để tránh đúng chuyện đó.
/// </para>
/// </remarks>
public class BatteryTypeResolver : IBatteryTypeResolver
{
    private const int DefaultMaxCycleCount = 2000;

    private readonly IBatteryUnitOfWork _unitOfWork;

    /// <summary>Tên loại pin (đã chuẩn hoá) đã giải trong lượt này → định danh.</summary>
    private readonly Dictionary<string, Guid> _resolved = new(StringComparer.OrdinalIgnoreCase);

    public BatteryTypeResolver(IBatteryUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public void Reset() => _resolved.Clear();

    public async Task<BatteryTypeResolution> ResolveAsync(ValidatedAsset asset, CancellationToken cancellationToken)
    {
        var name = asset.BatteryTypeName.Trim();
        if (name.Length == 0)
            return BatteryTypeResolution.Failed("Battery type name is required.");

        if (_resolved.TryGetValue(name, out var cached))
            return BatteryTypeResolution.Existing(cached);

        var existing = await _unitOfWork.BatteryTypes
            .GetAllAsync()
            .Where(type => !type.IsDeleted && type.Name.ToLower() == name.ToLower())
            .Select(type => new { type.Id })
            .FirstOrDefaultAsync(cancellationToken);

        if (existing is not null)
        {
            _resolved[name] = existing.Id;
            return BatteryTypeResolution.Existing(existing.Id);
        }

        // Không có sẵn thì chỉ tạo khi file mang đủ thông số kỹ thuật. Tạo bằng giá trị đoán sẽ đẻ
        // ra một loại pin sai thông số mà về sau mọi ngưỡng cảnh báo dựa vào nó đều lệch.
        if (asset.NominalCapacityAh is null || asset.NominalVoltage is null)
        {
            return BatteryTypeResolution.Failed(
                $"Battery type \"{name}\" does not exist yet, and the row does not provide both nominal_capacity_ah and nominal_voltage needed to create it.");
        }

        var created = new BatteryTypeEntity
        {
            Id = Guid.NewGuid(),
            Name = name,
            Manufacturer = asset.Manufacturer,
            NominalCapacityAh = asset.NominalCapacityAh.Value,
            NominalVoltage = asset.NominalVoltage.Value,
            Chemistry = asset.Chemistry ?? BatteryChemistryEnum.Other,
            MaxCycleCount = DefaultMaxCycleCount,
            Description = "Created automatically from third-party import data."
        };

        await _unitOfWork.BatteryTypes.AddAsync(created);
        _resolved[name] = created.Id;

        return BatteryTypeResolution.Created(created.Id);
    }
}
