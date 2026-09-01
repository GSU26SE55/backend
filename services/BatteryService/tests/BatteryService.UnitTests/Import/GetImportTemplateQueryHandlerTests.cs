using System.Text;
using BatteryService.Application.CQRS.Handler.Import;
using BatteryService.Application.CQRS.Query.Import;
using BatteryService.Application.Import;
using BatteryService.Domain.Enums;
using ClosedXML.Excel;

namespace BatteryService.UnitTests.Import;

/// <summary>Workbook mẫu .xlsx tải về từ <c>GET /api/imports/templates</c> — ba sheet, một file.</summary>
public class GetImportTemplateQueryHandlerTests
{
    private readonly CsvImportFileParser _csvParser = new();
    private readonly ImportWorkbookSplitter _splitter = new();
    private readonly GetImportTemplateQueryHandler _handler;

    public GetImportTemplateQueryHandlerTests()
    {
        _handler = new GetImportTemplateQueryHandler(_csvParser);
    }

    private async Task<XLWorkbook> DownloadAsync()
    {
        var response = await _handler.Handle(new GetImportTemplateQuery(), default);
        response.IsSuccess.Should().BeTrue();
        response.Data.Should().NotBeNull();
        response.Data!.ContentType.Should().Be("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        response.Data.FileName.Should().Be("import-template.xlsx");

        return new XLWorkbook(new MemoryStream(response.Data.Content));
    }

    [Fact]
    public async Task Handle_ReturnsExactlyThreeSheets_InTheCorrectOrder()
    {
        using var workbook = await DownloadAsync();

        workbook.Worksheets.Count.Should().Be(3);
        workbook.Worksheets.Select(ws => ws.Name).Should().Equal(
            ImportWorkbookSplitter.CustomersSheetName,
            ImportWorkbookSplitter.SitesSheetName,
            ImportWorkbookSplitter.AssetsSheetName);
    }

    [Theory]
    [InlineData(ImportWorkbookSplitter.CustomersSheetName, ImportEntityTypeEnum.Customer)]
    [InlineData(ImportWorkbookSplitter.SitesSheetName, ImportEntityTypeEnum.Site)]
    [InlineData(ImportWorkbookSplitter.AssetsSheetName, ImportEntityTypeEnum.BatteryAsset)]
    public async Task Handle_SheetHeaderRow_MatchesTemplateColumns(string sheetName, ImportEntityTypeEnum entityType)
    {
        using var workbook = await DownloadAsync();
        var sheet = workbook.Worksheet(sheetName);
        var expected = _csvParser.TemplateColumns(entityType);

        for (var i = 0; i < expected.Count; i++)
            sheet.Cell(1, i + 1).GetString().Should().Be(expected[i]);
    }

    [Theory]
    [InlineData(ImportWorkbookSplitter.CustomersSheetName)]
    [InlineData(ImportWorkbookSplitter.SitesSheetName)]
    [InlineData(ImportWorkbookSplitter.AssetsSheetName)]
    public async Task Handle_ReferenceRows_BothStartWithHashInFirstCell(string sheetName)
    {
        using var workbook = await DownloadAsync();
        var sheet = workbook.Worksheet(sheetName);

        sheet.Cell(2, 1).GetString().Should().StartWith("#");
        sheet.Cell(3, 1).GetString().Should().StartWith("#");
    }

    [Fact]
    public async Task Handle_ReUploadedAsIs_NoSheetProducesRealData()
    {
        // Nếu người dùng nộp thẳng file mẫu chưa sửa gì, cả ba bucket phải rỗng — nghĩa là lô bị
        // chặn ở bước validate ("provide at least one file") thay vì tạo ra khách/site/pin giả.
        using var workbook = await DownloadAsync();
        using var buffer = new MemoryStream();
        workbook.SaveAs(buffer);
        buffer.Position = 0;

        var result = _splitter.Split(buffer);

        result.CustomersCsv.Should().BeNull();
        result.SitesCsv.Should().BeNull();
        result.AssetsCsv.Should().BeNull();
    }

    /// <summary>
    /// Bóc dòng ví dụ (dòng 3) của một sheet ra thành workbook riêng chỉ một dòng dữ liệu — mô
    /// phỏng người dùng xoá dấu '#' ở đầu và xoá dòng REQUIRED/optional, giữ lại đúng ví dụ.
    /// </summary>
    private static byte[] IsolateExampleRow(XLWorkbook template, string sheetName)
    {
        var sheet = template.Worksheet(sheetName);
        var columnCount = sheet.Row(1).LastCellUsed()!.Address.ColumnNumber;

        using var isolated = new XLWorkbook();
        var isolatedSheet = isolated.Worksheets.Add(sheetName);
        for (var column = 1; column <= columnCount; column++)
        {
            isolatedSheet.Cell(1, column).Value = sheet.Cell(1, column).GetString();
            var exampleValue = sheet.Cell(3, column).GetString();
            isolatedSheet.Cell(2, column).Value = column == 1 ? exampleValue.TrimStart('#') : exampleValue;
        }

        using var buffer = new MemoryStream();
        isolated.SaveAs(buffer);
        return buffer.ToArray();
    }

    [Fact]
    public async Task Handle_CustomerExampleRow_IsRealValidDataOnceUncommented()
    {
        using var workbook = await DownloadAsync();
        var split = _splitter.Split(new MemoryStream(IsolateExampleRow(workbook, ImportWorkbookSplitter.CustomersSheetName)));

        split.CustomersCsv.Should().NotBeNull();
        var parsed = _csvParser.ParseCustomers(new MemoryStream(split.CustomersCsv!), 5000);
        parsed.IsFatal.Should().BeFalse();
        parsed.Rows.Should().ContainSingle();
    }

    [Fact]
    public async Task Handle_SiteExampleRow_IsRealValidDataOnceUncommented()
    {
        using var workbook = await DownloadAsync();
        var split = _splitter.Split(new MemoryStream(IsolateExampleRow(workbook, ImportWorkbookSplitter.SitesSheetName)));

        split.SitesCsv.Should().NotBeNull();
        var parsed = _csvParser.ParseSites(new MemoryStream(split.SitesCsv!), 5000);
        parsed.IsFatal.Should().BeFalse();
        parsed.Rows.Should().ContainSingle();
    }

    [Fact]
    public async Task Handle_AssetExampleRow_IsRealValidDataOnceUncommented()
    {
        using var workbook = await DownloadAsync();
        var split = _splitter.Split(new MemoryStream(IsolateExampleRow(workbook, ImportWorkbookSplitter.AssetsSheetName)));

        split.AssetsCsv.Should().NotBeNull();
        var parsed = _csvParser.ParseAssets(new MemoryStream(split.AssetsCsv!), 5000);
        parsed.IsFatal.Should().BeFalse();
        parsed.Rows.Should().ContainSingle();
    }
}
