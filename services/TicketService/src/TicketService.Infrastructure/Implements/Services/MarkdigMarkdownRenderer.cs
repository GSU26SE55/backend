using System.Text.RegularExpressions;
using Ganss.Xss;
using Markdig;
using TicketService.Application.Interfaces.Services;

namespace TicketService.Infrastructure.Implements.Services;

public class MarkdigMarkdownRenderer : IMarkdownRenderer
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAutoLinks()
        .Build();

    private static readonly string[] AllowedTagNames =
    {
        "p", "strong", "em", "code", "pre", "ul", "ol", "li", "a", "blockquote", "br", "img"
    };

    private static readonly Regex ImgTagRegex = new(@"<img\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex SrcAttrRegex = new(@"src\s*=\s*[""']([^""']*)[""']", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex GuidSegmentRegex = new(
        @"^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$",
        RegexOptions.Compiled);

    public string RenderToHtml(string markdownBody, IEnumerable<Guid> ticketAttachmentIds)
    {
        if (string.IsNullOrWhiteSpace(markdownBody))
            return string.Empty;

        var rawHtml = Markdown.ToHtml(markdownBody, Pipeline);
        var sanitized = Sanitize(rawHtml);

        var allowedIds = ticketAttachmentIds
            .Select(id => id.ToString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return StripDisallowedImages(sanitized, allowedIds);
    }

    private static string Sanitize(string html)
    {
        var sanitizer = new HtmlSanitizer();

        sanitizer.AllowedTags.Clear();
        foreach (var tag in AllowedTagNames)
            sanitizer.AllowedTags.Add(tag);

        sanitizer.AllowedAttributes.Clear();
        sanitizer.AllowedAttributes.Add("href");
        sanitizer.AllowedAttributes.Add("src");
        sanitizer.AllowedAttributes.Add("alt");

        sanitizer.AllowedSchemes.Clear();
        sanitizer.AllowedSchemes.Add("http");
        sanitizer.AllowedSchemes.Add("https");

        return sanitizer.Sanitize(html);
    }

    private static string StripDisallowedImages(string html, HashSet<string> allowedIds)
    {
        return ImgTagRegex.Replace(html, match =>
        {
            var srcMatch = SrcAttrRegex.Match(match.Value);
            if (!srcMatch.Success)
                return string.Empty;

            var src = srcMatch.Groups[1].Value.Trim();
            return IsAllowedAttachmentSrc(src, allowedIds) ? match.Value : string.Empty;
        });
    }

    // Chỉ chấp nhận relative path nội bộ có 1 segment khớp EXACT với attachment ID hợp lệ —
    // chặn absolute URL / protocol-relative URL (domain ngoài) và GUID giả nhúng trong query string.
    private static bool IsAllowedAttachmentSrc(string src, HashSet<string> allowedIds)
    {
        if (string.IsNullOrEmpty(src) || src.StartsWith("//", StringComparison.Ordinal))
            return false;

        if (Uri.TryCreate(src, UriKind.Absolute, out _))
            return false;

        var path = src.Split('?', '#')[0];
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);

        return segments.Any(segment => GuidSegmentRegex.IsMatch(segment) && allowedIds.Contains(segment));
    }
}
