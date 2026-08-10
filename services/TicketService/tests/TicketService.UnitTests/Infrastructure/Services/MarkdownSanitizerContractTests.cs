using System.Text.RegularExpressions;
using FluentAssertions;
using TicketService.Infrastructure.Implements.Services;

namespace TicketService.UnitTests.Infrastructure.Services;

/// <summary>
/// Ghim HỢP ĐỒNG của bộ khử HTML, đặt ra khi nâng <c>HtmlSanitizer 9.0.892 → 9.1.982</c>
/// (kéo theo <c>AngleSharp 0.17.1 → 1.7.0</c> — vá GHSA-pgww-w46g-26qg).
///
/// <para>Đổi bộ phân tích HTML nằm ngay dưới ranh giới chống XSS là thay đổi rủi ro nhất
/// trong một lần nâng dependency: hai bộ phân tích có thể "hiểu" cùng một chuỗi méo theo hai
/// cách khác nhau (parser differential / mXSS), và hệ quả không lộ ra ở test so chuỗi thông
/// thường.</para>
///
/// <para><b>Cách tiếp cận:</b> thay vì liệt kê chuỗi cấm, các test dưới đây kiểm <b>mọi thẻ và
/// mọi thuộc tính còn sót lại</b> trong kết quả có nằm trong allowlist không. Hợp đồng đó độc
/// lập với phiên bản parser, nên còn đúng ở những lần nâng sau.</para>
/// </summary>
public class MarkdownSanitizerContractTests
{
    private readonly MarkdigMarkdownRenderer _renderer = new();

    /// <summary>Phải khớp <c>MarkdigMarkdownRenderer.AllowedTagNames</c>.</summary>
    private static readonly HashSet<string> AllowedTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "p", "strong", "em", "code", "pre", "ul", "ol", "li", "a", "blockquote", "br", "img"
    };

    private static readonly HashSet<string> AllowedAttributes = new(StringComparer.OrdinalIgnoreCase)
    {
        "href", "src", "alt"
    };

    /// <summary>
    /// Payload nhắm vào KHÁC BIỆT GIỮA CÁC BỘ PHÂN TÍCH, không phải XSS sách vở:
    /// thẻ méo, viết hoa lẫn lộn, entity mã hoá, nội dung "ngoại lai" (svg/math/template),
    /// breakout khỏi comment/noscript — những chỗ hai phiên bản AngleSharp dễ hiểu khác nhau.
    /// </summary>
    public static TheoryData<string> ParserEdgeCasePayloads() => new()
    {
        "<ScRiPt>alert(1)</ScRiPt>",
        "<img/src=x onerror=alert(1)>",
        "<img src=x onerror=alert(1)//",
        "<a href=\"  javascript:alert(1)\">x</a>",
        "<a href=\"java&#x09;script:alert(1)\">x</a>",
        "<a href=\"&#106;avascript:alert(1)\">x</a>",
        "<a href=\"JaVaScRiPt:alert(1)\">x</a>",
        "<!--><script>alert(1)</script>-->",
        "<noscript><p title=\"</noscript><img src=x onerror=alert(1)>\">",
        "<template><script>alert(1)</script></template>",
        "<math><mtext><table><mglyph><style><img src=x onerror=alert(1)>",
        "<svg><animate onbegin=alert(1) attributeName=x dur=1s>",
        "<style>@import 'javascript:alert(1)';</style>",
        "<form><button formaction=javascript:alert(1)>x</button></form>",
        "<object data=\"javascript:alert(1)\"></object>",
        "<embed src=\"javascript:alert(1)\">",
        "<base href=\"javascript:alert(1)//\">",
        "<meta http-equiv=\"refresh\" content=\"0;url=javascript:alert(1)\">",
        "<a href=\"data:text/html;base64,PHNjcmlwdD5hbGVydCgxKTwvc2NyaXB0Pg==\">x</a>",
        "<p onmouseover=alert(1)>hover</p>",
        "<p ONMOUSEOVER=alert(1)>hover</p>",
        "<li><a href='javascript:alert(1)'>x</a></li>",
    };

    private static readonly Regex TagRegex =
        new(@"<\s*/?\s*([a-zA-Z][a-zA-Z0-9]*)", RegexOptions.Compiled);

    // Thuộc tính trong thẻ mở: tên = giá trị (có/không dấu nháy) hoặc tên trần.
    private static readonly Regex OpenTagRegex =
        new(@"<\s*([a-zA-Z][a-zA-Z0-9]*)((?:[^>""']|""[^""]*""|'[^']*')*)>", RegexOptions.Compiled);
    private static readonly Regex AttrNameRegex =
        new(@"(?:^|\s)([a-zA-Z_:][-a-zA-Z0-9_:.]*)\s*(?==|\s|$)", RegexOptions.Compiled);

    [Theory]
    [MemberData(nameof(ParserEdgeCasePayloads))]
    public void EveryRemainingTag_IsInAllowlist(string payload)
    {
        var html = _renderer.RenderToHtml(payload, Array.Empty<Guid>());

        var tags = TagRegex.Matches(html).Select(m => m.Groups[1].Value).Distinct().ToList();

        // Dùng NotContain thay OnlyContain: sanitizer xoá sạch (tập rỗng) là kết quả ĐÚNG,
        // nhưng OnlyContain coi tập rỗng là fail. Hai cách chặt như nhau với tập không rỗng.
        tags.Should().NotContain(t => !AllowedTags.Contains(t),
            $"mọi thẻ còn lại phải nằm trong allowlist. Kết quả: {html}");
    }

    [Theory]
    [MemberData(nameof(ParserEdgeCasePayloads))]
    public void EveryRemainingAttribute_IsInAllowlist(string payload)
    {
        var html = _renderer.RenderToHtml(payload, Array.Empty<Guid>());

        var attrs = OpenTagRegex.Matches(html)
            .SelectMany(m => AttrNameRegex.Matches(m.Groups[2].Value).Select(a => a.Groups[1].Value))
            .Distinct()
            .ToList();

        // Bắt được mọi handler sự kiện (onerror/onload/onmouseover/…) mà không phải liệt kê tay.
        attrs.Should().NotContain(a => !AllowedAttributes.Contains(a),
            $"mọi thuộc tính còn lại phải nằm trong allowlist. Kết quả: {html}");
    }

    [Theory]
    [MemberData(nameof(ParserEdgeCasePayloads))]
    public void NoExecutableScheme_Survives(string payload)
    {
        var html = _renderer.RenderToHtml(payload, Array.Empty<Guid>());

        // Bỏ khoảng trắng + tab/newline vì đó chính là cách né bộ lọc so chuỗi ngây thơ.
        var squeezed = Regex.Replace(html, @"[\s\u0000-\u0020]+", string.Empty);

        squeezed.Should().NotContainEquivalentOf("javascript:");
        squeezed.Should().NotContainEquivalentOf("vbscript:");
        squeezed.Should().NotContainEquivalentOf("data:text/html");
    }

    // ───────── Tự kiểm: bộ trích xuất phải THẬT SỰ hoạt động ─────────
    // Nếu 3 regex trên hỏng và luôn trả rỗng thì 66 ca Theory ở trên sẽ pass VÔ NGHĨA.
    // Ba test dưới đây đóng lỗ hổng đó.

    private static List<string> ExtractTags(string html) =>
        TagRegex.Matches(html).Select(m => m.Groups[1].Value).Distinct().ToList();

    private static List<string> ExtractAttributes(string html) =>
        OpenTagRegex.Matches(html)
            .SelectMany(m => AttrNameRegex.Matches(m.Groups[2].Value).Select(a => a.Groups[1].Value))
            .Distinct()
            .ToList();

    [Fact]
    public void Extractor_FindsTags_OnRealSanitizedOutput()
    {
        var html = _renderer.RenderToHtml("**đậm** và [link](https://example.com)", Array.Empty<Guid>());

        ExtractTags(html).Should().Contain("p").And.Contain("a").And.Contain("strong");
    }

    [Fact]
    public void Extractor_FindsAttributes_OnRealSanitizedOutput()
    {
        var html = _renderer.RenderToHtml("[link](https://example.com)", Array.Empty<Guid>());

        ExtractAttributes(html).Should().Contain("href");
    }

    [Fact]
    public void Extractor_WouldFlagDangerousMarkup_IfSanitizerEverRegressed()
    {
        // Đưa thẳng HTML bẩn vào bộ trích xuất (KHÔNG qua sanitizer): nếu một ngày sanitizer
        // để lọt, các assertion ở trên PHẢI đỏ. Đây là bằng chứng chúng có răng.
        const string dirty = "<script src=\"x\"></script><img src=y onerror=\"alert(1)\">";

        ExtractTags(dirty).Should().Contain(t => !AllowedTags.Contains(t),
            "phải phát hiện được thẻ ngoài allowlist");
        ExtractAttributes(dirty).Should().Contain(a => !AllowedAttributes.Contains(a),
            "phải phát hiện được thuộc tính ngoài allowlist (onerror)");
    }

    [Fact]
    public void PlainMarkdown_StillRendersAfterParserUpgrade()
    {
        // Chốt ngược lại: siết quá tay tới mức nội dung hợp lệ cũng mất thì cũng là hỏng.
        var html = _renderer.RenderToHtml(
            "**đậm** _nghiêng_ `mã`\n\n- một\n- hai\n\n> trích dẫn", Array.Empty<Guid>());

        html.Should().Contain("<strong>").And.Contain("<em>").And.Contain("<code>");
        html.Should().Contain("<ul>").And.Contain("<li>");
        html.Should().Contain("<blockquote>");
    }

    [Fact]
    public void SafeExternalLink_IsPreserved()
    {
        var html = _renderer.RenderToHtml("[trang chủ](https://example.com/a?b=1)", Array.Empty<Guid>());

        html.Should().Contain("href=\"https://example.com/a?b=1\"");
    }
}
