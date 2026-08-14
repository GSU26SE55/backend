using ClosedXML.Excel;
using FluentAssertions;
using SharedInfrastructure.Services;
using Xunit;

namespace SharedInfrastructure.UnitTests.Services;

/// <summary>
/// GH-731 — regression cho luồng export Excel sau khi ghim
/// <c>System.IO.Packaging 6.0.0 → 8.0.1</c> (vá GHSA-f32c-w444-8ppv + GHSA-qj66-m88j-hmgj).
///
/// <para><b>Vì sao cần bộ này:</b> test cũ chỉ kiểm "file không rỗng + đúng content-type" —
/// một workbook hỏng hoàn toàn vẫn qua được. Mà
/// <c>System.IO.Packaging</c> chính là thứ dựng container OPC/ZIP của .xlsx, nên rủi ro của
/// lần nâng này nằm đúng ở chỗ "file sinh ra có mở lại được không". Các test dưới đây
/// <b>mở lại workbook và đọc từng ô</b>, thay vì tin vào kích thước file.</para>
/// </summary>
public class ReportFileExporterXlsxRoundTripTests
{
    private static readonly DateTime SampleDate = new(2026, 8, 4, 10, 30, 0, DateTimeKind.Utc);

    private static ReportTable SampleTable(string name = "bao-cao") => new(
        name,
        new[] { "Chuỗi", "Số nguyên", "Số thực", "Ngày", "Đúng/Sai", "Rỗng" },
        new IReadOnlyList<object?>[]
        {
            new object?[] { "Pin số 1", 42, 3.5m, SampleDate, true, null },
            new object?[] { "Trạm Hòa Bình", -7, 0.125d, SampleDate.AddDays(-1), false, null },
        });

    private static XLWorkbook OpenXlsx(ReportFile file)
    {
        var ms = new MemoryStream(file.Content);
        return new XLWorkbook(ms);
    }

    [Fact]
    public void Xlsx_IsValidOpcPackage_AndReopens()
    {
        var file = ReportFileExporter.Export(SampleTable(), "xlsx");

        // OPC = container ZIP ⇒ 2 byte đầu phải là "PK". Bắt được ca packaging hỏng ngay từ
        // vỏ file, trước cả khi ClosedXML kịp phàn nàn.
        file.Content.Should().HaveCountGreaterThan(2);
        file.Content[0].Should().Be((byte)'P');
        file.Content[1].Should().Be((byte)'K');

        using var wb = OpenXlsx(file);
        wb.Worksheets.Should().HaveCount(1);
    }

    [Fact]
    public void Xlsx_RoundTrip_PreservesHeadersAndCellValues()
    {
        var table = SampleTable();
        var file = ReportFileExporter.Export(table, "xlsx");

        using var wb = OpenXlsx(file);
        var ws = wb.Worksheets.First();

        for (var c = 0; c < table.Headers.Count; c++)
            ws.Cell(1, c + 1).GetString().Should().Be(table.Headers[c]);

        // Hàng 1 dữ liệu (dòng 2 trong sheet).
        ws.Cell(2, 1).GetString().Should().Be("Pin số 1");
        ws.Cell(2, 2).GetValue<int>().Should().Be(42);
        ws.Cell(2, 3).GetValue<decimal>().Should().Be(3.5m);
        ws.Cell(2, 4).GetDateTime().Should().Be(SampleDate);
        ws.Cell(2, 5).GetBoolean().Should().BeTrue();
        ws.Cell(2, 6).IsEmpty().Should().BeTrue("null phải thành ô trống, không phải chuỗi 'null'");

        ws.Cell(3, 1).GetString().Should().Be("Trạm Hòa Bình", "tiếng Việt có dấu phải nguyên vẹn");
        ws.Cell(3, 2).GetValue<int>().Should().Be(-7);
        ws.Cell(3, 5).GetBoolean().Should().BeFalse();
    }

    [Fact]
    public void Xlsx_NumbersStayNumeric_NotText()
    {
        // Nếu số bị ghi thành chuỗi thì Excel không tính tổng được — lỗi âm thầm mà
        // test "file không rỗng" không bao giờ thấy.
        var file = ReportFileExporter.Export(SampleTable(), "xlsx");

        using var wb = OpenXlsx(file);
        var ws = wb.Worksheets.First();

        ws.Cell(2, 2).DataType.Should().Be(XLDataType.Number);
        ws.Cell(2, 3).DataType.Should().Be(XLDataType.Number);
        ws.Cell(2, 5).DataType.Should().Be(XLDataType.Boolean);
    }

    [Fact]
    public void Xlsx_HeaderRow_IsBold()
    {
        var file = ReportFileExporter.Export(SampleTable(), "xlsx");

        using var wb = OpenXlsx(file);
        wb.Worksheets.First().Row(1).Style.Font.Bold.Should().BeTrue();
    }

    [Fact]
    public void Xlsx_LongName_IsTruncatedTo31Chars_ExcelLimit()
    {
        var longName = new string('t', 50);

        var file = ReportFileExporter.Export(SampleTable(longName), "xlsx");

        using var wb = OpenXlsx(file);
        wb.Worksheets.First().Name.Should().HaveLength(31, "Excel giới hạn tên sheet 31 ký tự");
        // Tên file KHÔNG bị cắt — chỉ tên sheet bị.
        file.FileName.Should().Be($"{longName}.xlsx");
    }

    [Fact]
    public void Xlsx_EmptyTable_StillProducesOpenableWorkbook()
    {
        var empty = new ReportTable("rong", new[] { "A", "B" }, Array.Empty<IReadOnlyList<object?>>());

        var file = ReportFileExporter.Export(empty, "xlsx");

        using var wb = OpenXlsx(file);
        var ws = wb.Worksheets.First();
        ws.Cell(1, 1).GetString().Should().Be("A");
        ws.Cell(2, 1).IsEmpty().Should().BeTrue();
    }

    [Fact]
    public void Csv_Path_IsUnaffectedByPackagingUpgrade()
    {
        // CSV không đụng System.IO.Packaging; giữ test này để phân biệt "hỏng do packaging"
        // với "hỏng do exporter" nếu sau này có sự cố.
        var file = ReportFileExporter.Export(SampleTable(), "csv");

        file.ContentType.Should().Be(ReportFileExporter.CsvContentType);
        file.Content.Should().StartWith(System.Text.Encoding.UTF8.GetPreamble());
    }
}
