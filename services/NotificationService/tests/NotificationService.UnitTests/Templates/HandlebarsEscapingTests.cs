using System.Reflection;
using NotificationService.Application.Templates;

namespace NotificationService.UnitTests.Templates;

/// <summary>
/// Sprint 6.3 NOTI3-16 (#716) — chống stored XSS qua template email (R-45).
///
/// Nội dung chèn vào template đến từ dữ liệu người dùng nhập: tên khách hàng, tiêu đề ticket,
/// ghi chú xử lý. Trước sprint này renderer bật <c>NoEscape = true</c>, nghĩa là một tiêu đề ticket
/// chứa <c>&lt;script&gt;</c> sẽ được chèn thẳng vào email HTML gửi cho người khác.
/// </summary>
public class HandlebarsEscapingTests
{
    private readonly HandlebarsTemplateRenderer _renderer = new();

    [Fact]
    public void RenderInline_EscapesScriptTag()
    {
        var html = _renderer.RenderInline("<p>{{Title}}</p>", new { Title = "<script>alert(1)</script>" });

        html.Should().NotContain("<script>");
        html.Should().Contain("&lt;script&gt;");
    }

    [Fact]
    public void RenderInline_EscapesAttributeBreakout()
    {
        // Kịch bản thoát khỏi thuộc tính: nếu không escape dấu nháy, chuỗi này gắn được onerror.
        var html = _renderer.RenderInline(
            """<img src="{{Url}}" />""",
            new { Url = "x\" onerror=\"alert(1)" });

        html.Should().NotContain("onerror=\"alert(1)\"");
        html.Should().Contain("&quot;");
    }

    [Fact]
    public void RenderInline_EscapesAmpersand()
    {
        _renderer.RenderInline("{{Name}}", new { Name = "Pin A & Pin B" })
            .Should().Be("Pin A &amp; Pin B");
    }

    /// <summary>
    /// Đây là lý do gốc khiến <c>NoEscape</c> từng được bật, và nó là mối lo CÓ THẬT: encoder mặc
    /// định của Handlebars.NET mã hoá cả ký tự ngoài ASCII ("Nguyễn" → <c>Nguy&amp;#7877;n</c>),
    /// hỏng hoàn toàn nội dung push/SMS. Cách chữa đúng không phải tắt escape mà là dùng
    /// <c>HtmlOnlyTextEncoder</c> — test này chốt cả hai yêu cầu cùng lúc.
    /// </summary>
    [Theory]
    [InlineData("Nguyễn Văn Bảo")]
    [InlineData("Sự cố nhiệt độ vượt ngưỡng tại trạm Đà Nẵng")]
    [InlineData("Pin đã xuống cấp — cần thay thế")]
    [InlineData("Ưu tiên P1: Khẩn cấp")]
    public void RenderInline_KeepsVietnameseDiacriticsIntact(string value)
    {
        _renderer.RenderInline("{{Value}}", new { Value = value }).Should().Be(value);
    }

    /// <summary>Ba ngoặc là lối thoát CÓ Ý THỨC khi thật sự cần HTML thô.</summary>
    [Fact]
    public void RenderInline_TripleBrace_StillEmitsRawHtml()
    {
        _renderer.RenderInline("{{{Html}}}", new { Html = "<b>đậm</b>" })
            .Should().Be("<b>đậm</b>");
    }

    [Fact]
    public void RenderInline_EmptySource_ReturnsEmpty()
    {
        _renderer.RenderInline("", new { }).Should().BeEmpty();
    }

    /// <summary>
    /// Rà 16 template embedded: chỗ nào dùng <c>{{{ba-ngoặc}}}</c> là chỗ cố ý cho phép HTML thô.
    /// Hiện KHÔNG template nào cần, vì tất cả chỉ chèn dữ liệu thuần (tên, mã, ngày giờ, ghi chú).
    /// Test này đỏ khi có người thêm ba-ngoặc mới ⇒ buộc phải cân nhắc có ý thức thay vì lọt âm thầm.
    /// </summary>
    [Fact]
    public void EmbeddedTemplates_DoNotUseRawHtmlPlaceholders()
    {
        var assembly = typeof(HandlebarsTemplateRenderer).Assembly;
        var names = assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith("NotificationService.Application.Templates.", StringComparison.Ordinal)
                        && n.EndsWith(".html", StringComparison.Ordinal))
            .ToList();

        names.Should().NotBeEmpty("phải tìm thấy template embedded — sai thì test này vô nghĩa");

        var offenders = new List<string>();

        foreach (var name in names)
        {
            using var stream = assembly.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream);
            var content = reader.ReadToEnd();

            if (content.Contains("{{{", StringComparison.Ordinal))
                offenders.Add(name);
        }

        offenders.Should().BeEmpty(
            "template dùng {{{{{{...}}}}}} là bỏ qua HTML-escape — chỉ chấp nhận khi thật sự cần HTML thô "
            + "và đã rà nguồn dữ liệu");
    }

    /// <summary>Mọi template embedded phải compile được — template hỏng chỉ lộ ra lúc gửi thật.</summary>
    [Fact]
    public void EveryEmbeddedTemplate_CompilesAndRenders()
    {
        var assembly = typeof(HandlebarsTemplateRenderer).Assembly;
        var prefix = "NotificationService.Application.Templates.";

        var templateNames = assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(prefix, StringComparison.Ordinal) && n.EndsWith(".html", StringComparison.Ordinal))
            .Select(n => n[prefix.Length..^".html".Length])
            .ToList();

        foreach (var templateName in templateNames)
        {
            var act = () => _renderer.Render(templateName, new { });
            act.Should().NotThrow($"template '{templateName}' phải compile được");
        }
    }
}
