using System.Text;
using BatteryService.Application.Import;
using BatteryService.Domain.Enums;

namespace BatteryService.UnitTests.Import;

/// <summary>I1 — bộ đọc file CSV của bên thứ ba.</summary>
public class ImportParserTests
{
    private readonly CsvImportFileParser _parser = new();

    private static Stream Csv(string content, bool withBom = false)
    {
        var bytes = withBom
            ? Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(content)).ToArray()
            : Encoding.UTF8.GetBytes(content);
        return new MemoryStream(bytes);
    }

    [Fact]
    public void ParseCustomers_ValidFile_ReturnsRowsWithOriginalLineNumbers()
    {
        var result = _parser.ParseCustomers(Csv(
            "external_customer_code,full_name,email,phone\n" +
            "KH-001,Cong ty A,a@example.com,0901234567\n" +
            "KH-002,Cong ty B,b@example.com,\n"), 5000);

        result.IsFatal.Should().BeFalse();
        result.Rows.Should().HaveCount(2);
        // Dòng tiêu đề là dòng 1 nên dòng dữ liệu đầu tiên phải là dòng 2.
        result.Rows[0].RowNumber.Should().Be(2);
        result.Rows[1].RowNumber.Should().Be(3);
        result.Rows[0].Value.Email.Should().Be("a@example.com");
        result.Rows[1].Value.Phone.Should().BeNull();
    }

    [Fact]
    public void ParseCustomers_MissingRequiredColumn_IsFatalAndNamesTheColumn()
    {
        var result = _parser.ParseCustomers(Csv("external_customer_code,full_name\nKH-001,Cong ty A\n"), 5000);

        result.IsFatal.Should().BeTrue();
        result.FatalError.Should().Contain("email");
    }

    [Fact]
    public void ParseCustomers_FileWithBom_StillMatchesFirstColumn()
    {
        var result = _parser.ParseCustomers(Csv(
            "external_customer_code,full_name,email\nKH-001,Cong ty A,a@example.com\n", withBom: true), 5000);

        result.IsFatal.Should().BeFalse();
        result.Rows.Should().ContainSingle();
        result.Rows[0].Value.ExternalCustomerCode.Should().Be("KH-001");
    }

    [Fact]
    public void ParseCustomers_HeaderVariants_AreNormalized()
    {
        // Đối tác xuất file từ nhiều công cụ khác nhau: hoa thường, khoảng trắng, gạch nối đều có.
        var result = _parser.ParseCustomers(Csv(
            "External Customer Code, Full-Name ,EMAIL\nKH-001,Cong ty A,a@example.com\n"), 5000);

        result.IsFatal.Should().BeFalse();
        result.Rows[0].Value.ExternalCustomerCode.Should().Be("KH-001");
        result.Rows[0].Value.FullName.Should().Be("Cong ty A");
        result.Rows[0].Value.Email.Should().Be("a@example.com");
    }

    [Fact]
    public void ParseCustomers_ExtraColumns_AreIgnored()
    {
        var result = _parser.ParseCustomers(Csv(
            "external_customer_code,full_name,email,internal_note\nKH-001,Cong ty A,a@example.com,anything\n"), 5000);

        result.IsFatal.Should().BeFalse();
        result.Rows.Should().ContainSingle();
    }

    [Fact]
    public void ParseCustomers_HeaderOnly_IsFatal()
    {
        var result = _parser.ParseCustomers(Csv("external_customer_code,full_name,email\n"), 5000);

        result.IsFatal.Should().BeTrue();
        result.FatalError.Should().Contain("no data rows");
    }

    [Fact]
    public void ParseCustomers_TooManyRows_IsFatalInsteadOfSilentTruncation()
    {
        var builder = new StringBuilder("external_customer_code,full_name,email\n");
        for (var i = 0; i < 5; i++)
            builder.Append($"KH-{i:D3},Name {i},user{i}@example.com\n");

        var result = _parser.ParseCustomers(Csv(builder.ToString()), maxRows: 3);

        result.IsFatal.Should().BeTrue();
        result.FatalError.Should().Contain("3");
    }

    [Fact]
    public void RawJson_KeepsOriginalCellsBeforeNormalization()
    {
        var result = _parser.ParseAssets(Csv(
            "external_asset_code,external_site_code,serial_number,battery_type_name\n" +
            "AS-1,ST-1,pyl/us3000c 88a21,US3000C\n"), 5000);

        result.Rows[0].RawJson.Should().Contain("pyl/us3000c 88a21");
    }

    [Fact]
    public void TemplateColumns_CoverEveryRequiredColumn()
    {
        foreach (var entityType in Enum.GetValues<ImportEntityTypeEnum>())
        {
            var template = _parser.TemplateColumns(entityType);
            var required = _parser.RequiredColumns(entityType);

            required.Should().NotBeEmpty($"{entityType} must declare required columns");
            required.Should().BeSubsetOf(template,
                $"every required column of {entityType} must appear in the downloadable template");
        }
    }

    [Fact]
    public void ParseCustomers_LineStartingWithHash_IsIgnoredAsComment()
    {
        // File mẫu tải về chèn dòng chú thích/ví dụ bắt đầu bằng '#' giữa tiêu đề và dữ liệu thật.
        // Nếu người dùng quên xoá trước khi nộp, những dòng đó không được biến thành dữ liệu.
        var result = _parser.ParseCustomers(Csv(
            "external_customer_code,full_name,email,phone\n" +
            "#REQUIRED,REQUIRED,REQUIRED,optional\n" +
            "#VIDU-KH-001,Cong Ty Vi Du,vidu@example.com,0900000000\n" +
            "KH-001,Cong ty That,that@example.com,0901234567\n"), 5000);

        result.IsFatal.Should().BeFalse();
        result.Rows.Should().ContainSingle();
        result.Rows[0].Value.ExternalCustomerCode.Should().Be("KH-001");
        // Dòng dữ liệu thật là dòng 4 trong file — không bị lệch bởi các dòng chú thích đứng trước.
        result.Rows[0].RowNumber.Should().Be(4);
    }

    [Fact]
    public void ParseCustomers_OnlyCommentLinesAfterHeader_IsFatalNoDataRows()
    {
        var result = _parser.ParseCustomers(Csv(
            "external_customer_code,full_name,email,phone\n" +
            "#REQUIRED,REQUIRED,REQUIRED,optional\n" +
            "#VIDU-KH-001,Cong Ty Vi Du,vidu@example.com,0900000000\n" +
            "\n" +
            "# ===== HUONG DAN =====\n"), 5000);

        result.IsFatal.Should().BeTrue();
        result.FatalError.Should().Contain("no data rows");
    }
}
