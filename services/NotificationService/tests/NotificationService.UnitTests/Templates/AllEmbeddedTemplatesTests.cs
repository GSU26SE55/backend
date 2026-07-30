using System.Reflection;
using NotificationService.Application.Templates;

namespace NotificationService.UnitTests.Templates;

/// <summary>
/// Quét TOÀN BỘ template embedded thay vì liệt kê tay từng cái — file mới thêm về sau tự động
/// được kiểm tra. Bắt được 3 loại lỗi hay xảy ra khi sửa/đổi tên template:
/// quên đưa vào <c>EmbeddedResource</c> csproj, sai cú pháp Handlebars, và regression layout
/// (thiếu viewport / còn <c>min-width</c> khiến email hiển thị bé tí trên mobile).
/// </summary>
public class AllEmbeddedTemplatesTests
{
    private const string Prefix = "NotificationService.Application.Templates.";
    private const string Suffix = ".html";

    private static readonly Assembly TemplateAssembly = typeof(HandlebarsTemplateRenderer).Assembly;

    public static IEnumerable<object[]> AllTemplateNames() =>
        TemplateAssembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(Prefix, StringComparison.Ordinal) && n.EndsWith(Suffix, StringComparison.Ordinal))
            .Select(n => new object[] { n[Prefix.Length..^Suffix.Length] })
            .OrderBy(a => (string)a[0]);

    private static string ReadRaw(string templateName)
    {
        using var stream = TemplateAssembly.GetManifestResourceStream($"{Prefix}{templateName}{Suffix}")!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    [Fact]
    public void AllTemplateFiles_AreEmbeddedAsHtml()
    {
        var names = AllTemplateNames().Select(a => (string)a[0]).ToList();

        names.Should().HaveCount(16, "16 template .html phải nằm trong assembly — thiếu nghĩa là "
                                     + "glob EmbeddedResource trong csproj không khớp");
        names.Should().Contain("environmental-incident-detected");
        names.Should().Contain("battery-alert-escalation-pending");
        names.Should().Contain("alert-ticket-saga-failed");
    }

    [Fact]
    public void NoTemplateIsStillEmbeddedAsHbs()
    {
        TemplateAssembly.GetManifestResourceNames()
            .Where(n => n.EndsWith(".hbs", StringComparison.OrdinalIgnoreCase))
            .Should().BeEmpty("toàn bộ .hbs đã đổi sang .html — còn sót nghĩa là csproj nhúng cả hai");
    }

    /// <summary>Compile được = cú pháp Handlebars hợp lệ (thẻ mở/đóng {{#if}} cân nhau…).</summary>
    [Theory]
    [MemberData(nameof(AllTemplateNames))]
    public void Template_CompilesAndRenders(string templateName)
    {
        var renderer = new HandlebarsTemplateRenderer();

        var act = () => renderer.Render(templateName, new { });

        act.Should().NotThrow($"template '{templateName}' phải compile được với data rỗng");
    }

    /// <summary>
    /// Chặn tái diễn lỗi layout đã sửa: <c>min-width:1000px</c> ép mobile client thu nhỏ cả email
    /// (chữ bé li ti), <c>width:70%</c> + <c>margin:50px auto</c> tạo viền trắng rất dày.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllTemplateNames))]
    public void Template_HasResponsiveEmailLayout(string templateName)
    {
        var html = ReadRaw(templateName);

        html.Should().Contain("name=\"viewport\"",
            $"'{templateName}' thiếu meta viewport → client mobile tự zoom-out, chữ bé");
        html.Should().Contain("max-width:600px",
            $"'{templateName}' phải giới hạn khung nội dung 600px");
        html.Should().NotContain("min-width:1000px",
            $"'{templateName}' còn min-width:1000px → mobile phải scale nhỏ toàn bộ email");
        html.Should().NotContain("width:70%",
            $"'{templateName}' còn width:70% → chừa gutter trắng dày hai bên");
        html.Should().NotContain("margin:50px auto",
            $"'{templateName}' còn margin ngoài 50px → viền quanh email quá dày");
    }

    [Theory]
    [MemberData(nameof(AllTemplateNames))]
    public void Template_IsWellFormedHtmlDocument(string templateName)
    {
        var html = ReadRaw(templateName);

        html.Should().StartWith("<!DOCTYPE html>");
        html.Should().Contain("<html");
        html.TrimEnd().Should().EndWith("</html>");
        html.Should().Contain("charset=UTF-8");

        // Thẻ table phải cân — email client rất dễ vỡ layout nếu lệch.
        CountOccurrences(html, "<table").Should().Be(CountOccurrences(html, "</table>"),
            $"'{templateName}' số thẻ <table> và </table> phải bằng nhau");
        CountOccurrences(html, "<tr").Should().Be(CountOccurrences(html, "</tr>"));
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }
        return count;
    }
}
