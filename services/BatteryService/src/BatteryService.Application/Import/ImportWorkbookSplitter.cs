using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using CsvHelper;

namespace BatteryService.Application.Import;

/// <summary>Ba bucket CSV tách ra từ một workbook — đúng hình dạng mà pipeline CSV hiện có đang dùng.</summary>
public sealed record ImportWorkbookSplitResult(byte[]? CustomersCsv, byte[]? SitesCsv, byte[]? AssetsCsv);

/// <summary>
/// Tách một file Excel (.xlsx) nhiều sheet thành ba luồng CSV, để phần còn lại của hệ thống — vốn
/// đọc/kiểm định/ghi theo CSV — không cần biết gì về Excel.
/// </summary>
public interface IImportWorkbookSplitter
{
    /// <summary>
    /// Đọc <paramref name="xlsx"/> và trả về ba bucket CSV tương ứng ba sheet đã biết trước
    /// (<see cref="ImportWorkbookSplitter.CustomersSheetName"/> và tương tự). Ném ngoại lệ nếu
    /// file không đọc được như một workbook Excel hợp lệ — gọi nơi biên (controller) bắt lấy để trả
    /// lỗi rõ ràng, vì đây là hỏng ở mức file, không phải hỏng ở mức một sheet/dòng.
    /// </summary>
    ImportWorkbookSplitResult Split(Stream xlsx);
}

/// <summary>Hiện thực <see cref="IImportWorkbookSplitter"/> bằng ClosedXML.</summary>
public sealed class ImportWorkbookSplitter : IImportWorkbookSplitter
{
    /// <summary>
    /// Tên sheet chuẩn, đúng thứ tự bắt buộc khi ghi thật (khách hàng → site → pin). Đánh số ngay
    /// trong tên để thứ tự đó hiện rõ trên tab Excel, không chỉ nằm trong tài liệu.
    /// </summary>
    public const string CustomersSheetName = "1-Customers";
    public const string SitesSheetName = "2-Sites";
    public const string AssetsSheetName = "3-Assets";

    public ImportWorkbookSplitResult Split(Stream xlsx)
    {
        using var workbook = new XLWorkbook(xlsx);

        // Dò theo vị trí chỉ an toàn khi cả ba sheet đều có mặt (người dùng chỉ lỡ đổi tên tab,
        // không xoá tab nào) — người dùng chủ ý nộp thiếu tab (upload từng phần) thường xoá hẳn
        // tab không dùng tới, nên workbook sẽ có ít hơn 3 sheet. Dò theo vị trí trong trường hợp đó
        // sẽ đẩy dữ liệu của sheet còn lại vào nhầm bucket (vd. workbook chỉ còn "2-Sites" ở vị trí
        // 0 sẽ bị đọc nhầm thành Customers).
        var allowPositionFallback = workbook.Worksheets.Count == 3;

        return new ImportWorkbookSplitResult(
            SplitSheet(workbook, CustomersSheetName, 0, allowPositionFallback),
            SplitSheet(workbook, SitesSheetName, 1, allowPositionFallback),
            SplitSheet(workbook, AssetsSheetName, 2, allowPositionFallback));
    }

    /// <summary>
    /// Tra sheet theo đúng tên trước; nếu người dùng lỡ đổi tên tab NHƯNG vẫn giữ đủ ba tab, dò
    /// tiếp theo vị trí (0=khách, 1=site, 2=pin) — đúng thứ tự các sheet luôn được tạo ra trong file
    /// mẫu tải về.
    /// </summary>
    private static byte[]? SplitSheet(
        XLWorkbook workbook, string sheetName, int fallbackPosition, bool allowPositionFallback)
    {
        var sheet = workbook.Worksheets.FirstOrDefault(ws =>
            string.Equals(ws.Name.Trim(), sheetName, StringComparison.OrdinalIgnoreCase));

        if (sheet is null && allowPositionFallback && fallbackPosition < workbook.Worksheets.Count)
            sheet = workbook.Worksheets.ElementAt(fallbackPosition);

        if (sheet is null)
            return null;

        var rows = sheet.RowsUsed().ToList();
        if (rows.Count == 0)
            return null;

        var headerRow = rows[0];
        var lastColumn = headerRow.LastCellUsed()?.Address.ColumnNumber ?? 0;
        if (lastColumn == 0)
            return null;

        var dataRows = rows.Skip(1).ToList();

        // Dòng "REQUIRED/optional" và dòng ví dụ trong file mẫu đều bắt đầu bằng '#' ở ô đầu tiên —
        // cùng quy ước comment mà CsvImportFileParser đã bật (AllowComments). Nếu người dùng chưa
        // điền gì thật ngoài hai dòng đó, sheet coi như KHÔNG có dữ liệu — giống hệt việc bỏ trống,
        // không đính kèm file cho loại đó (thay vì tạo lô rồi báo lỗi "no data rows").
        var hasRealData = dataRows.Any(row => RowHasRealData(row, lastColumn));
        if (!hasRealData)
            return null;

        return ToCsvBytes(headerRow, dataRows, lastColumn);
    }

    private static bool RowHasRealData(IXLRow row, int lastColumn)
    {
        var firstCell = CellToText(row.Cell(1));
        if (firstCell.StartsWith('#'))
            return false;

        for (var column = 1; column <= lastColumn; column++)
        {
            if (CellToText(row.Cell(column)).Length > 0)
                return true;
        }

        return false;
    }

    private static byte[] ToCsvBytes(IXLRow headerRow, List<IXLRow> dataRows, int lastColumn)
    {
        using var buffer = new MemoryStream();
        using (var streamWriter = new StreamWriter(buffer, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), leaveOpen: true))
        using (var csvWriter = new CsvWriter(streamWriter, CultureInfo.InvariantCulture))
        {
            WriteRow(csvWriter, headerRow, lastColumn);
            foreach (var row in dataRows)
                WriteRow(csvWriter, row, lastColumn);
        }

        return buffer.ToArray();
    }

    private static void WriteRow(CsvWriter csvWriter, IXLRow row, int lastColumn)
    {
        for (var column = 1; column <= lastColumn; column++)
            csvWriter.WriteField(CellToText(row.Cell(column)));

        csvWriter.NextRecord();
    }

    /// <summary>
    /// Đọc ô theo đúng kiểu dữ liệu Excel thay vì <c>GetString()</c> thô: một ô số luôn ra dấu chấm
    /// thập phân bất kể máy Excel của người dùng đặt vùng miền gì (kể cả Excel tiếng Việt hiển thị
    /// dấu phẩy) — đúng cái mà bộ kiểm định đòi hỏi. Một ô ngày luôn ra <c>yyyy-MM-dd</c> — không lệ
    /// thuộc định dạng hiển thị của ô, tránh mọi nhập nhằng kiểu MM/dd so với dd/MM.
    /// </summary>
    private static string CellToText(IXLCell cell)
    {
        return cell.DataType switch
        {
            XLDataType.Blank => string.Empty,
            XLDataType.Number => cell.GetDouble().ToString(CultureInfo.InvariantCulture),
            XLDataType.DateTime => cell.GetDateTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            XLDataType.Boolean => cell.GetBoolean() ? "true" : "false",
            _ => cell.GetString().Trim(),
        };
    }
}
