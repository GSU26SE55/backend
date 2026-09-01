using System.Globalization;
using System.Text;
using BatteryService.Application.Import;
using ClosedXML.Excel;
using CsvHelper;

namespace BatteryService.UnitTests.Import;

/// <summary>Tách một workbook .xlsx ba sheet thành ba luồng CSV cho pipeline nhập liệu hiện có.</summary>
public class ImportWorkbookSplitterTests
{
    private readonly ImportWorkbookSplitter _splitter = new();

    private static byte[] SaveToBytes(XLWorkbook workbook)
    {
        using var buffer = new MemoryStream();
        workbook.SaveAs(buffer);
        return buffer.ToArray();
    }

    private static string? DecodeUtf8(byte[]? bytes) => bytes is null ? null : Encoding.UTF8.GetString(bytes).TrimStart('﻿');

    private static List<string[]> ReadCsvRows(byte[] csv)
    {
        using var reader = new StreamReader(new MemoryStream(csv), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        using var parser = new CsvParser(reader, CultureInfo.InvariantCulture);

        var rows = new List<string[]>();
        while (parser.Read())
            rows.Add(parser.Record ?? Array.Empty<string>());

        return rows;
    }

    [Fact]
    public void Split_ValidWorkbookWithAllThreeSheets_ReturnsThreeNonNullBuckets()
    {
        using var workbook = new XLWorkbook();
        var customers = workbook.Worksheets.Add(ImportWorkbookSplitter.CustomersSheetName);
        customers.Cell(1, 1).Value = "external_customer_code";
        customers.Cell(1, 2).Value = "full_name";
        customers.Cell(2, 1).Value = "KH-001";
        customers.Cell(2, 2).Value = "Cong ty A";

        var sites = workbook.Worksheets.Add(ImportWorkbookSplitter.SitesSheetName);
        sites.Cell(1, 1).Value = "external_site_code";
        sites.Cell(2, 1).Value = "ST-001";

        var assets = workbook.Worksheets.Add(ImportWorkbookSplitter.AssetsSheetName);
        assets.Cell(1, 1).Value = "external_asset_code";
        assets.Cell(2, 1).Value = "AS-001";

        var result = _splitter.Split(new MemoryStream(SaveToBytes(workbook)));

        result.CustomersCsv.Should().NotBeNull();
        result.SitesCsv.Should().NotBeNull();
        result.AssetsCsv.Should().NotBeNull();
        DecodeUtf8(result.CustomersCsv).Should().Contain("KH-001").And.Contain("Cong ty A");
    }

    [Fact]
    public void Split_SheetsRenamed_FallsBackToPosition()
    {
        // Người dùng lỡ đổi tên tab — vẫn phải nhận ra đúng loại nhờ thứ tự 0=khách,1=site,2=pin.
        using var workbook = new XLWorkbook();
        var first = workbook.Worksheets.Add("Sheet1");
        first.Cell(1, 1).Value = "external_customer_code";
        first.Cell(2, 1).Value = "KH-999";
        var second = workbook.Worksheets.Add("Sheet2");
        second.Cell(1, 1).Value = "external_site_code";
        second.Cell(2, 1).Value = "ST-999";
        var third = workbook.Worksheets.Add("Sheet3");
        third.Cell(1, 1).Value = "external_asset_code";
        third.Cell(2, 1).Value = "AS-999";

        var result = _splitter.Split(new MemoryStream(SaveToBytes(workbook)));

        DecodeUtf8(result.CustomersCsv).Should().Contain("KH-999");
        DecodeUtf8(result.SitesCsv).Should().Contain("ST-999");
        DecodeUtf8(result.AssetsCsv).Should().Contain("AS-999");
    }

    [Fact]
    public void Split_SheetWithOnlyHeaderAndCommentRows_IsTreatedAsAbsent()
    {
        // File mẫu tải về, người dùng không đụng vào sheet Site — chỉ còn dòng tiêu đề + hai dòng
        // '#' tham khảo. Phải coi như "không đính kèm loại này", KHÔNG được đẩy vào bộ đọc rồi báo
        // lỗi "no data rows" — lỗi đó sẽ đánh hỏng cả lô dù hai sheet còn lại có dữ liệu thật.
        using var workbook = new XLWorkbook();
        var sites = workbook.Worksheets.Add(ImportWorkbookSplitter.SitesSheetName);
        sites.Cell(1, 1).Value = "external_site_code";
        sites.Cell(1, 2).Value = "site_name";
        sites.Cell(2, 1).Value = "#REQUIRED";
        sites.Cell(2, 2).Value = "REQUIRED";
        sites.Cell(3, 1).Value = "#VIDU-SITE-001";
        sites.Cell(3, 2).Value = "Nha May Vi Du";

        var result = _splitter.Split(new MemoryStream(SaveToBytes(workbook)));

        result.SitesCsv.Should().BeNull();
    }

    [Fact]
    public void Split_SheetMissingEntirely_ReturnsNullForThatBucket()
    {
        using var workbook = new XLWorkbook();
        var customers = workbook.Worksheets.Add(ImportWorkbookSplitter.CustomersSheetName);
        customers.Cell(1, 1).Value = "external_customer_code";
        customers.Cell(2, 1).Value = "KH-001";

        var result = _splitter.Split(new MemoryStream(SaveToBytes(workbook)));

        result.CustomersCsv.Should().NotBeNull();
        result.SitesCsv.Should().BeNull();
        result.AssetsCsv.Should().BeNull();
    }

    [Fact]
    public void Split_OnlySiteSheetPresent_DoesNotMisreadItAsCustomers()
    {
        // Người dùng chủ ý nộp thiếu tab (upload từng phần theo quan hệ, xoá hẳn hai tab kia) — chỉ
        // còn "2-Sites" ở vị trí 0 trong workbook. Dò theo vị trí (dành cho trường hợp đổi tên tab)
        // không được phép nhầm sheet này thành Customers chỉ vì nó nằm ở vị trí 0.
        using var workbook = new XLWorkbook();
        var sites = workbook.Worksheets.Add(ImportWorkbookSplitter.SitesSheetName);
        sites.Cell(1, 1).Value = "external_site_code";
        sites.Cell(1, 2).Value = "external_customer_code";
        sites.Cell(2, 1).Value = "ST-001";
        sites.Cell(2, 2).Value = "KH-001";

        var result = _splitter.Split(new MemoryStream(SaveToBytes(workbook)));

        result.CustomersCsv.Should().BeNull();
        result.AssetsCsv.Should().BeNull();
        result.SitesCsv.Should().NotBeNull();
        DecodeUtf8(result.SitesCsv).Should().Contain("ST-001").And.Contain("KH-001");
    }

    [Fact]
    public void Split_EmptySheet_ReturnsNullBucket()
    {
        using var workbook = new XLWorkbook();
        workbook.Worksheets.Add(ImportWorkbookSplitter.CustomersSheetName);

        var result = _splitter.Split(new MemoryStream(SaveToBytes(workbook)));

        result.CustomersCsv.Should().BeNull();
    }

    [Fact]
    public void Split_NumericCell_RendersWithInvariantDotDecimal()
    {
        // Bất kể Excel của người dùng đặt vùng miền gì (kể cả hiển thị dấu phẩy thập phân), giá trị
        // số bên trong ô luôn là double — phải đọc ra dấu chấm để khớp bộ kiểm định.
        using var workbook = new XLWorkbook();
        var assets = workbook.Worksheets.Add(ImportWorkbookSplitter.AssetsSheetName);
        assets.Cell(1, 1).Value = "nominal_voltage";
        assets.Cell(2, 1).Value = 51.2;

        var result = _splitter.Split(new MemoryStream(SaveToBytes(workbook)));

        var rows = ReadCsvRows(result.AssetsCsv!);
        rows[1][0].Should().Be("51.2");
    }

    [Fact]
    public void Split_DateCell_RendersAsIsoDateRegardlessOfDisplayFormat()
    {
        // Ô ngày có thể hiển thị theo bất kỳ định dạng vùng miền nào (kể cả kiểu mập mờ dd/MM so với
        // MM/dd) — phải đọc theo giá trị DateTime thật của ô, không đọc theo chuỗi hiển thị.
        using var workbook = new XLWorkbook();
        var sites = workbook.Worksheets.Add(ImportWorkbookSplitter.SitesSheetName);
        sites.Cell(1, 1).Value = "install_date";
        var dateCell = sites.Cell(2, 1);
        dateCell.Value = new DateTime(2021, 3, 15);
        dateCell.Style.NumberFormat.Format = "MM/dd/yyyy";

        var result = _splitter.Split(new MemoryStream(SaveToBytes(workbook)));

        var rows = ReadCsvRows(result.SitesCsv!);
        rows[1][0].Should().Be("2021-03-15");
    }

    [Fact]
    public void Split_TextValueContainingComma_RoundTripsAsOneCsvField()
    {
        // Trước đây file mẫu ghép chuỗi CSV bằng tay nên giá trị có dấu phẩy bị vỡ cột. Dùng
        // CsvWriter thật để tách sheet loại hẳn lớp lỗi này cho dữ liệu người dùng gõ vào Excel.
        using var workbook = new XLWorkbook();
        var sites = workbook.Worksheets.Add(ImportWorkbookSplitter.SitesSheetName);
        sites.Cell(1, 1).Value = "external_site_code";
        sites.Cell(1, 2).Value = "address";
        sites.Cell(2, 1).Value = "ST-001";
        sites.Cell(2, 2).Value = "KCN Vi Du, Tinh ABC";

        var result = _splitter.Split(new MemoryStream(SaveToBytes(workbook)));

        var rows = ReadCsvRows(result.SitesCsv!);
        rows[1].Should().Equal("ST-001", "KCN Vi Du, Tinh ABC");
    }

    [Fact]
    public void Split_RowWhoseFirstCellStartsWithHash_IsExcludedFromRealDataCheck_ButOtherRealRowsStillCount()
    {
        using var workbook = new XLWorkbook();
        var customers = workbook.Worksheets.Add(ImportWorkbookSplitter.CustomersSheetName);
        customers.Cell(1, 1).Value = "external_customer_code";
        customers.Cell(2, 1).Value = "#VIDU-KH-001";
        customers.Cell(3, 1).Value = "KH-REAL";

        var result = _splitter.Split(new MemoryStream(SaveToBytes(workbook)));

        result.CustomersCsv.Should().NotBeNull();
        DecodeUtf8(result.CustomersCsv).Should().Contain("KH-REAL");
    }

    [Fact]
    public void Split_NotAnExcelFile_Throws()
    {
        var act = () => _splitter.Split(new MemoryStream("not an xlsx file"u8.ToArray()));

        act.Should().Throw<Exception>();
    }
}
