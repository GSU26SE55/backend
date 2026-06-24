using System.Text;
using FluentAssertions;
using SharedInfrastructure.Services;

namespace BatteryService.UnitTests.Application;

/// <summary>Sprint 7 #114 — ReportFileExporter (CSV thủ công + XLSX ClosedXML).</summary>
public class ReportFileExporterTests
{
    private static ReportTable Sample() => new(
        "demo",
        new[] { "Name", "Count" },
        new List<IReadOnlyList<object?>>
        {
            new object?[] { "alpha", 3 },
            new object?[] { "has,comma", 5 }
        });

    [Theory]
    [InlineData("csv")]
    [InlineData("xlsx")]
    [InlineData("CSV")]
    [InlineData("XLSX")]
    public void IsExportFormat_True_ForCsvXlsx(string format)
        => ReportFileExporter.IsExportFormat(format).Should().BeTrue();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("json")]
    [InlineData("pdf")]
    public void IsExportFormat_False_Otherwise(string? format)
        => ReportFileExporter.IsExportFormat(format).Should().BeFalse();

    [Fact]
    public void Csv_Has_Header_Rows_Bom_And_Escapes_Comma()
    {
        var file = ReportFileExporter.Export(Sample(), "csv");

        file.ContentType.Should().Be(ReportFileExporter.CsvContentType);
        file.FileName.Should().Be("demo.csv");

        // UTF-8 BOM ở đầu.
        file.Content.Take(3).Should().Equal(Encoding.UTF8.GetPreamble());

        var text = Encoding.UTF8.GetString(file.Content);
        text.Should().Contain("Name,Count");
        text.Should().Contain("alpha,3");
        text.Should().Contain("\"has,comma\",5");   // field có dấu phẩy → quote
    }

    [Fact]
    public void Xlsx_Produces_NonEmpty_File_With_Correct_ContentType()
    {
        var file = ReportFileExporter.Export(Sample(), "xlsx");

        file.ContentType.Should().Be(ReportFileExporter.XlsxContentType);
        file.FileName.Should().Be("demo.xlsx");
        file.Content.Length.Should().BeGreaterThan(0);
        // XLSX là zip → bắt đầu bằng "PK".
        file.Content.Take(2).Should().Equal(new byte[] { 0x50, 0x4B });
    }
}
