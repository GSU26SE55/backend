using TicketService.Infrastructure.Implements.Services;

namespace TicketService.UnitTests.Infrastructure.Services;

public class MarkdownRendererTests
{
    private readonly MarkdigMarkdownRenderer _renderer = new();

    public static IEnumerable<object[]> XssPayloads => new List<object[]>
    {
        new object[] { "<script>alert(1)</script>" },
        new object[] { "<img src=x onerror=alert(1)>" },
        new object[] { "[click](javascript:alert(1))" },
        new object[] { "<svg onload=alert(1)></svg>" },
        new object[] { "<img src=\"data:text/html;base64,PHNjcmlwdD5hbGVydCgxKTwvc2NyaXB0Pg==\">" },
        new object[] { "<iframe src=\"javascript:alert(1)\"></iframe>" },
        new object[] { "<body onload=alert(1)>" },
        new object[] { "<div onclick=\"alert(1)\">click</div>" },
        new object[] { "[click](vbscript:msgbox(1))" },
        new object[] { "<img src=\"javascript:alert(1)\">" }
    };

    [Theory]
    [MemberData(nameof(XssPayloads))]
    public void RenderToHtml_XssPayload_IsSanitized(string payload)
    {
        var html = _renderer.RenderToHtml(payload, Array.Empty<Guid>());

        html.Should().NotContain("alert(1)");
        html.Should().NotContain("onerror");
        html.Should().NotContain("onload");
        html.Should().NotContain("onclick");
        html.Should().NotContain("<script");
        html.Should().NotContain("<iframe");
        html.Should().NotContain("<svg");
        html.Should().NotContain("javascript:");
        html.Should().NotContain("vbscript:");
    }

    [Fact]
    public void RenderToHtml_AllowedTags_ArePreserved()
    {
        var html = _renderer.RenderToHtml("**bold** and *em* and `code`", Array.Empty<Guid>());

        html.Should().Contain("<strong>bold</strong>");
        html.Should().Contain("<em>em</em>");
        html.Should().Contain("<code>code</code>");
    }

    [Fact]
    public void RenderToHtml_AutoLinksUrl()
    {
        var html = _renderer.RenderToHtml("See it at https://example.com", Array.Empty<Guid>());

        html.Should().Contain("<a href=\"https://example.com\"");
    }

    [Fact]
    public void RenderToHtml_ImgWithAllowedAttachmentId_IsKept()
    {
        var attachmentId = Guid.NewGuid();
        var markdown = $"![alt](/files/{attachmentId})";

        var html = _renderer.RenderToHtml(markdown, new[] { attachmentId });

        html.Should().Contain("<img");
        html.Should().Contain(attachmentId.ToString());
    }

    [Fact]
    public void RenderToHtml_ImgWithDisallowedSrc_IsStripped()
    {
        var allowedId = Guid.NewGuid();
        var foreignId = Guid.NewGuid();
        var markdown = $"![alt](/files/{foreignId})";

        var html = _renderer.RenderToHtml(markdown, new[] { allowedId });

        html.Should().NotContain("<img");
    }

    [Fact]
    public void RenderToHtml_ImgWithAllowedIdInExternalDomainQueryString_IsStripped()
    {
        var allowedId = Guid.NewGuid();
        var markdown = $"![x](https://evil.com/track.gif?ref={allowedId})";

        var html = _renderer.RenderToHtml(markdown, new[] { allowedId });

        html.Should().NotContain("<img");
        html.Should().NotContain("evil.com");
    }

    [Fact]
    public void RenderToHtml_ImgWithProtocolRelativeExternalSrc_IsStripped()
    {
        var allowedId = Guid.NewGuid();
        var markdown = $"![x](//evil.com/{allowedId})";

        var html = _renderer.RenderToHtml(markdown, new[] { allowedId });

        html.Should().NotContain("<img");
        html.Should().NotContain("evil.com");
    }

    [Fact]
    public void RenderToHtml_ImgWithAllowedIdAsQueryParamOnRelativePath_IsStripped()
    {
        var allowedId = Guid.NewGuid();
        var markdown = $"![x](/files/unrelated.gif?ref={allowedId})";

        var html = _renderer.RenderToHtml(markdown, new[] { allowedId });

        html.Should().NotContain("<img");
    }

    [Fact]
    public void RenderToHtml_EmptyBody_ReturnsEmptyString()
    {
        var html = _renderer.RenderToHtml("", Array.Empty<Guid>());

        html.Should().BeEmpty();
    }
}
