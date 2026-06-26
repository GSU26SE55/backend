using System.Globalization;
using System.Text;
using ClosedXML.Excel;

namespace SharedInfrastructure.Services;

/// <summary>
/// Sprint 7 #114 (§5.2/§5.3) — export report ra CSV hoặc XLSX.
///
/// Dùng chung cho mọi report endpoint: handler build <see cref="ReportTable"/> (headers + rows),
/// controller gọi <see cref="Export"/> với <c>format</c> từ query (<c>csv</c>/<c>xlsx</c>) → trả file download.
/// CSV viết tay (không cần dependency); XLSX dùng ClosedXML (no Excel install).
/// </summary>
public static class ReportFileExporter
{
    public const string CsvContentType = "text/csv";
    public const string XlsxContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    /// <summary>True nếu <paramref name="format"/> là csv hoặc xlsx (không phân biệt hoa thường).</summary>
    public static bool IsExportFormat(string? format) =>
        string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase)
        || string.Equals(format, "xlsx", StringComparison.OrdinalIgnoreCase);

    public static ReportFile Export(ReportTable table, string? format)
    {
        var isXlsx = string.Equals(format, "xlsx", StringComparison.OrdinalIgnoreCase);
        return isXlsx ? ToXlsx(table) : ToCsv(table);
    }

    private static ReportFile ToCsv(ReportTable table)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", table.Headers.Select(EscapeCsv)));
        foreach (var row in table.Rows)
        {
            sb.AppendLine(string.Join(",", row.Select(cell => EscapeCsv(Format(cell)))));
        }

        // UTF-8 BOM để Excel mở tiếng Việt không lỗi font.
        var preamble = Encoding.UTF8.GetPreamble();
        var body = Encoding.UTF8.GetBytes(sb.ToString());
        var content = new byte[preamble.Length + body.Length];
        Buffer.BlockCopy(preamble, 0, content, 0, preamble.Length);
        Buffer.BlockCopy(body, 0, content, preamble.Length, body.Length);

        return new ReportFile(content, CsvContentType, $"{table.Name}.csv");
    }

    private static ReportFile ToXlsx(ReportTable table)
    {
        using var workbook = new XLWorkbook();
        // Sheet name tối đa 31 ký tự.
        var sheetName = table.Name.Length > 31 ? table.Name[..31] : table.Name;
        var ws = workbook.Worksheets.Add(sheetName);

        for (var c = 0; c < table.Headers.Count; c++)
        {
            ws.Cell(1, c + 1).Value = table.Headers[c];
        }
        ws.Row(1).Style.Font.Bold = true;

        for (var r = 0; r < table.Rows.Count; r++)
        {
            var row = table.Rows[r];
            for (var c = 0; c < row.Count; c++)
            {
                ws.Cell(r + 2, c + 1).Value = XLCellValueFor(row[c]);
            }
        }

        ws.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        workbook.SaveAs(ms);
        return new ReportFile(ms.ToArray(), XlsxContentType, $"{table.Name}.xlsx");
    }

    private static XLCellValue XLCellValueFor(object? value) => value switch
    {
        null => Blank.Value,
        bool b => b,
        DateTime dt => dt,
        decimal dec => dec,
        double d => d,
        int i => i,
        long l => l,
        _ => value.ToString()
    };

    private static string Format(object? cell) => cell switch
    {
        null => string.Empty,
        DateTime dt => dt.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
        decimal dec => dec.ToString(CultureInfo.InvariantCulture),
        double d => d.ToString(CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => cell.ToString() ?? string.Empty
    };

    private static string EscapeCsv(string field)
    {
        if (field.Contains(',') || field.Contains('"') || field.Contains('\n') || field.Contains('\r'))
        {
            return $"\"{field.Replace("\"", "\"\"")}\"";
        }
        return field;
    }
}
