using System.Globalization;
using System.Text;
using System.Text.Json;
using BatteryService.Domain.Enums;
using CsvHelper;
using CsvHelper.Configuration;

namespace BatteryService.Application.Import;

/// <summary>
/// Đọc file CSV của bên thứ ba.
/// </summary>
/// <remarks>
/// <para>
/// Bộ đọc cố tình <b>không</b> ép kiểu ở bước này: mọi ô đều đọc thành chuỗi rồi mới kiểm tra ở
/// tầng kiểm định. Nếu để thư viện ép kiểu, một ô ngày tháng sai định dạng sẽ ném ngoại lệ và làm
/// hỏng cả file, trong khi thứ người dùng cần là "dòng 47 sai ngày" chứ không phải "file hỏng".
/// </para>
/// <para>
/// Tên cột đọc theo lối rộng rãi: bỏ qua hoa thường, bỏ qua khoảng trắng thừa, chấp nhận cả gạch
/// dưới lẫn gạch nối. Đối tác xuất file từ đủ loại công cụ và không ai trong số đó thống nhất.
/// </para>
/// </remarks>
public class CsvImportFileParser : IImportFileParser
{
    private static readonly string[] CustomerColumns =
        ["external_customer_code", "full_name", "email", "phone"];

    private static readonly string[] SiteColumns =
    [
        "external_site_code", "external_customer_code", "site_name", "address",
        "latitude", "longitude", "install_date", "contact_person_name", "contact_person_phone"
    ];

    private static readonly string[] AssetColumns =
    [
        "external_asset_code", "external_site_code", "serial_number", "battery_type_name",
        "manufacturer", "nominal_capacity_ah", "nominal_voltage", "chemistry",
        "install_date", "warranty_end_date", "location", "notes"
    ];


    public IReadOnlyList<string> TemplateColumns(ImportEntityTypeEnum entityType) => entityType switch
    {
        ImportEntityTypeEnum.Customer => CustomerColumns,
        ImportEntityTypeEnum.Site => SiteColumns,
        ImportEntityTypeEnum.BatteryAsset => AssetColumns,
        _ => Array.Empty<string>()
    };

    public IReadOnlyList<string> RequiredColumns(ImportEntityTypeEnum entityType) => entityType switch
    {
        ImportEntityTypeEnum.Customer => ["external_customer_code", "full_name", "email"],
        ImportEntityTypeEnum.Site => ["external_site_code", "external_customer_code", "site_name"],
        ImportEntityTypeEnum.BatteryAsset =>
            ["external_asset_code", "external_site_code", "serial_number", "battery_type_name"],
        _ => Array.Empty<string>()
    };

    public ImportParseResult<ImportCustomerRow> ParseCustomers(Stream csv, int maxRows) =>
        Parse(csv, maxRows, ImportEntityTypeEnum.Customer, ImportRowPayload.ToCustomer);

    public ImportParseResult<ImportSiteRow> ParseSites(Stream csv, int maxRows) =>
        Parse(csv, maxRows, ImportEntityTypeEnum.Site, ImportRowPayload.ToSite);

    public ImportParseResult<ImportAssetRow> ParseAssets(Stream csv, int maxRows) =>
        Parse(csv, maxRows, ImportEntityTypeEnum.BatteryAsset, ImportRowPayload.ToAsset);


    private ImportParseResult<T> Parse<T>(
        Stream csv, int maxRows, ImportEntityTypeEnum entityType,
        Func<Dictionary<string, string>, T> map)
    {
        var configuration = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            // Tự kiểm tra tiêu đề để báo đúng cột nào thiếu, thay vì để thư viện ném ngoại lệ chung.
            HeaderValidated = null,
            MissingFieldFound = null,
            TrimOptions = TrimOptions.Trim,
            IgnoreBlankLines = true,
            BadDataFound = null,
            // File mẫu tải về (GetImportTemplateQueryHandler) chèn dòng chú thích/ví dụ bắt đầu bằng
            // '#' ở giữa và cuối file. Bật cờ này để những dòng đó bị bỏ qua nếu đối tác lỡ nộp
            // nguyên file mẫu chưa xoá phần chú thích, thay vì bị đọc thành một dòng dữ liệu hỏng.
            AllowComments = true,
            Comment = '#'
        };

        var rows = new List<ParsedRow<T>>();

        try
        {
            // detectEncodingFromByteOrderMarks xử lý luôn dấu BOM mà Excel hay chèn vào đầu file;
            // không xử lý thì tên cột đầu tiên mang theo ký tự vô hình và không bao giờ khớp.
            using var reader = new StreamReader(csv, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            using var parser = new CsvReader(reader, configuration);

            if (!parser.Read() || !parser.ReadHeader())
                return ImportParseResult<T>.Fatal("The file is empty or has no header row.");

            var header = parser.HeaderRecord ?? Array.Empty<string>();
            var normalizedHeader = header.Select(NormalizeHeader).ToArray();

            var missing = RequiredColumns(entityType)
                .Where(required => !normalizedHeader.Contains(required, StringComparer.Ordinal))
                .ToList();

            if (missing.Count > 0)
            {
                return ImportParseResult<T>.Fatal(
                    $"The file is missing required columns: {string.Join(", ", missing)}.");
            }

            while (parser.Read())
            {
                // Lấy số dòng thật trong file (parser.Parser.Row), không phải "rows.Count + 2":
                // file mẫu tải về và file đối tác đều có thể chứa dòng trống hoặc dòng chú thích
                // '#' bị bỏ qua ở trên — đếm theo rows.Count sẽ báo sai dòng cho người dùng đi sửa.
                var rowNumber = parser.Parser.Row;

                if (rows.Count >= maxRows)
                {
                    return ImportParseResult<T>.Fatal(
                        $"The file has more than {maxRows} data rows. Split it into smaller files.");
                }

                var cells = new Dictionary<string, string>(StringComparer.Ordinal);
                for (var i = 0; i < normalizedHeader.Length; i++)
                {
                    var name = normalizedHeader[i];
                    if (name.Length == 0 || cells.ContainsKey(name))
                        continue;

                    cells[name] = parser.TryGetField<string>(i, out var value) ? value?.Trim() ?? string.Empty : string.Empty;
                }

                rows.Add(new ParsedRow<T>(rowNumber, JsonSerializer.Serialize(cells), map(cells)));
            }
        }
        catch (CsvHelperException ex)
        {
            return ImportParseResult<T>.Fatal($"The file could not be read as CSV: {ex.Message}");
        }

        return rows.Count == 0
            ? ImportParseResult<T>.Fatal("The file has a header row but no data rows.")
            : new ImportParseResult<T>(rows, null);
    }

    /// <summary>Đưa tên cột về dạng chữ thường, gạch dưới — chấp nhận mọi biến thể đối tác gửi tới.</summary>
    private static string NormalizeHeader(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

        var builder = new StringBuilder(raw.Length);
        foreach (var ch in raw.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch))
                builder.Append(ch);
            else if (builder.Length > 0 && builder[^1] != '_')
                builder.Append('_');
        }

        while (builder.Length > 0 && builder[^1] == '_')
            builder.Length--;

        return builder.ToString();
    }

}
