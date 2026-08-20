using BatteryService.Domain.Enums;

namespace BatteryService.Application.Import;

/// <summary>Đọc file CSV do bên thứ ba cung cấp thành các dòng có cấu trúc.</summary>
public interface IImportFileParser
{
    /// <summary>Cột bắt buộc của từng loại file — dùng cả khi đọc lẫn khi sinh file mẫu.</summary>
    IReadOnlyList<string> RequiredColumns(ImportEntityTypeEnum entityType);

    /// <summary>Toàn bộ cột của file mẫu, theo đúng thứ tự.</summary>
    IReadOnlyList<string> TemplateColumns(ImportEntityTypeEnum entityType);

    ImportParseResult<ImportCustomerRow> ParseCustomers(Stream csv, int maxRows);

    ImportParseResult<ImportSiteRow> ParseSites(Stream csv, int maxRows);

    ImportParseResult<ImportAssetRow> ParseAssets(Stream csv, int maxRows);
}
