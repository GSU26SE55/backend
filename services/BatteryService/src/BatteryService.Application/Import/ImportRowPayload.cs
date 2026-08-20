using System.Text.Json;

namespace BatteryService.Application.Import;

/// <summary>
/// Ánh xạ các ô của một dòng (đã chuẩn hoá tên cột) sang mô hình dòng.
/// </summary>
/// <remarks>
/// Dùng chung cho cả bộ đọc file lẫn pha ghi thật. Pha ghi đọc lại dòng gốc đã lưu, nên nếu hai
/// nơi ánh xạ khác nhau thì thứ được kiểm định và thứ được ghi xuống có thể lệch nhau — loại lỗi
/// không lộ ra ở bước kiểm định và chỉ phát hiện được khi so dữ liệu bằng mắt.
/// </remarks>
public static class ImportRowPayload
{
    public static ImportCustomerRow ToCustomer(IReadOnlyDictionary<string, string> cells) => new()
    {
        ExternalCustomerCode = Get(cells, "external_customer_code"),
        FullName = Get(cells, "full_name"),
        Email = Get(cells, "email"),
        Phone = GetOrNull(cells, "phone")
    };

    public static ImportSiteRow ToSite(IReadOnlyDictionary<string, string> cells) => new()
    {
        ExternalSiteCode = Get(cells, "external_site_code"),
        ExternalCustomerCode = Get(cells, "external_customer_code"),
        SiteName = Get(cells, "site_name"),
        Address = GetOrNull(cells, "address"),
        Latitude = GetOrNull(cells, "latitude"),
        Longitude = GetOrNull(cells, "longitude"),
        InstallDate = GetOrNull(cells, "install_date"),
        ContactPersonName = GetOrNull(cells, "contact_person_name"),
        ContactPersonPhone = GetOrNull(cells, "contact_person_phone")
    };

    public static ImportAssetRow ToAsset(IReadOnlyDictionary<string, string> cells) => new()
    {
        ExternalAssetCode = Get(cells, "external_asset_code"),
        ExternalSiteCode = Get(cells, "external_site_code"),
        SerialNumber = Get(cells, "serial_number"),
        BatteryTypeName = Get(cells, "battery_type_name"),
        Manufacturer = GetOrNull(cells, "manufacturer"),
        NominalCapacityAh = GetOrNull(cells, "nominal_capacity_ah"),
        NominalVoltage = GetOrNull(cells, "nominal_voltage"),
        Chemistry = GetOrNull(cells, "chemistry"),
        InstallDate = GetOrNull(cells, "install_date"),
        WarrantyEndDate = GetOrNull(cells, "warranty_end_date"),
        Location = GetOrNull(cells, "location"),
        Notes = GetOrNull(cells, "notes")
    };


    /// <summary>Đọc lại các ô từ chuỗi JSON đã lưu ở dòng import.</summary>
    public static IReadOnlyDictionary<string, string> FromRawJson(string? rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
            return new Dictionary<string, string>(StringComparer.Ordinal);

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(rawJson)
                   ?? new Dictionary<string, string>(StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }

    private static string Get(IReadOnlyDictionary<string, string> cells, string name) =>
        cells.TryGetValue(name, out var value) ? value : string.Empty;

    private static string? GetOrNull(IReadOnlyDictionary<string, string> cells, string name)
    {
        var value = Get(cells, name);
        return value.Length == 0 ? null : value;
    }
}
