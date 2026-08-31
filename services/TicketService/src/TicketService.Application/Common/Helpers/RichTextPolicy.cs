using System.Text.RegularExpressions;
using SharedContracts.Common.Responses;

namespace TicketService.Application.Common.Helpers;

/// <summary>
/// "Có nội dung" cho các field rich-text (blog post, KB article).
///
/// <para>BE trước đây chỉ kiểm <c>IsNullOrWhiteSpace</c> trên chuỗi HTML thô, nên
/// <c>"&lt;hr&gt;"</c> hay <c>"&lt;p&gt;&amp;nbsp;&lt;/p&gt;"</c> được coi là hợp lệ — trong khi FE
/// strip tag rồi mới kiểm nên coi chúng là rỗng. Hệ quả: bài lưu qua API bằng những chuỗi đó
/// thì chính editor không mở sửa lại được, đúng cái bẫy mà BlogSlugPolicy sinh ra để đóng.</para>
///
/// <para>Điều kiện khớp với FE: có text sau khi bỏ tag, HOẶC có media nhúng.</para>
/// </summary>
public static class RichTextPolicy
{
    private static readonly Regex TagRegex = new("<[^>]*>", RegexOptions.Compiled);

    private static readonly Regex EmbeddedMediaRegex = new(
        @"<(img|video|iframe|figure|embed)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>True khi HTML có chữ thật hoặc media nhúng.</summary>
    public static bool HasContent(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return false;

        if (EmbeddedMediaRegex.IsMatch(html))
            return true;

        var text = TagRegex.Replace(html, " ")
            .Replace("&nbsp;", " ")
            .Replace("&amp;", "&")
            .Replace("&lt;", "<")
            .Replace("&gt;", ">");

        return !string.IsNullOrWhiteSpace(text);
    }

    public static void AddContentErrors(ICollection<Errors> errors, string? html, string field, string label = "Content")
    {
        if (!HasContent(html))
            errors.Add(new Errors { Field = field, Detail = $"{label} is required." });
    }
}
