using BatteryService.Domain.Enums;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.Import;

/// <summary>Kết quả kiểm định một dòng: giá trị đã chuẩn hoá, hoặc danh sách lỗi.</summary>
/// <remarks>
/// Lỗi dùng lại kiểu <see cref="Errors"/> của dự án để phần hiển thị và phần xuất file lỗi không
/// phải dịch qua một hình dạng thứ hai.
/// </remarks>
public sealed record ImportValidation<T>(T? Value, IReadOnlyList<Errors> Errors)
{
    public bool IsValid => Errors.Count == 0 && Value is not null;

    public static ImportValidation<T> Ok(T value) => new(value, Array.Empty<Errors>());

    public static ImportValidation<T> Invalid(IReadOnlyList<Errors> errors) => new(default, errors);
}

/// <param name="ExternalCode">Mã khách hàng bên đối tác, đã chuẩn hoá.</param>
/// <param name="ExternalCodeRaw">Mã gốc như trong file.</param>
public sealed record ValidatedCustomer(
    string ExternalCode,
    string ExternalCodeRaw,
    string FullName,
    string Email,
    string? Phone);

public sealed record ValidatedSite(
    string ExternalCode,
    string ExternalCodeRaw,
    string ExternalCustomerCode,
    string Name,
    string? Address,
    decimal? Latitude,
    decimal? Longitude,
    DateTime InstallDate,
    string? ContactPersonName,
    string? ContactPersonPhone);

public sealed record ValidatedAsset(
    string ExternalCode,
    string ExternalCodeRaw,
    string ExternalSiteCode,
    string SerialNumber,
    string SerialNumberRaw,
    string BatteryTypeName,
    string? Manufacturer,
    decimal? NominalCapacityAh,
    decimal? NominalVoltage,
    BatteryChemistryEnum? Chemistry,
    DateTime InstallDate,
    DateTime? WarrantyEndDate,
    string? Location,
    string? Notes);
