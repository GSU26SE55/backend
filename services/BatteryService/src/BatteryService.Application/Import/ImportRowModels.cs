namespace BatteryService.Application.Import;

/// <summary>
/// Bốn hình dạng dòng mà file nhập của bên thứ ba có thể mang.
/// </summary>
/// <remarks>
/// Các sheet nối với nhau bằng <b>mã của bên thứ ba</b>, không bằng định danh nội bộ. Lúc đối tác
/// xuất file họ chưa biết định danh bên mình, nên bắt họ điền định danh đó là bắt họ nhập hai lần.
/// </remarks>
public sealed class ImportCustomerRow
{
    public string ExternalCustomerCode { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }
}

public sealed class ImportSiteRow
{
    public string ExternalSiteCode { get; set; } = string.Empty;
    public string ExternalCustomerCode { get; set; } = string.Empty;
    public string SiteName { get; set; } = string.Empty;
    public string? Address { get; set; }
    public string? Latitude { get; set; }
    public string? Longitude { get; set; }
    public string? InstallDate { get; set; }
    public string? ContactPersonName { get; set; }
    public string? ContactPersonPhone { get; set; }
}

public sealed class ImportAssetRow
{
    public string ExternalAssetCode { get; set; } = string.Empty;
    public string ExternalSiteCode { get; set; } = string.Empty;
    public string SerialNumber { get; set; } = string.Empty;
    public string BatteryTypeName { get; set; } = string.Empty;
    public string? Manufacturer { get; set; }

    /// <summary>Dung lượng danh định tính bằng ampe giờ.</summary>
    public string? NominalCapacityAh { get; set; }

    public string? NominalVoltage { get; set; }
    public string? Chemistry { get; set; }
    public string? InstallDate { get; set; }
    public string? WarrantyEndDate { get; set; }
    public string? Location { get; set; }
    public string? Notes { get; set; }
}


/// <summary>Một dòng đã đọc được từ file, kèm số dòng gốc và nội dung gốc.</summary>
/// <param name="RowNumber">Số dòng trong file, tính cả dòng tiêu đề — để người dùng dò ngược.</param>
/// <param name="RawJson">Nội dung gốc dạng JSON, chưa qua chuẩn hoá.</param>
/// <param name="Value">Đối tượng đã ánh xạ.</param>
public sealed record ParsedRow<T>(int RowNumber, string RawJson, T Value);

/// <summary>Kết quả đọc một file.</summary>
/// <param name="Rows">Các dòng đọc được.</param>
/// <param name="FatalError">Lỗi ở mức file khiến không dòng nào dùng được; null nếu đọc được.</param>
public sealed record ImportParseResult<T>(IReadOnlyList<ParsedRow<T>> Rows, string? FatalError)
{
    public bool IsFatal => FatalError is not null;

    public static ImportParseResult<T> Fatal(string message) => new(Array.Empty<ParsedRow<T>>(), message);
}
