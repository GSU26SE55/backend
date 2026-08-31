using System.Text.RegularExpressions;

namespace TicketService.Application.Common.Helpers;

/// <summary>
/// Định dạng slug của blog post — slug đi thẳng vào URL nên chỉ nhận chữ thường, số và
/// dấu gạch nối đơn.
///
/// <para>Trước đây chỉ FE kiểm tra định dạng này còn BE cho qua, nên gọi thẳng API là lưu
/// được slug có dấu cách/chữ hoa, và bài đó về sau không sửa được từ editor vì slug lấy
/// ra không qua nổi validate của FE.</para>
/// </summary>
public static class BlogSlugPolicy
{
    public const string FormatMessage =
        "Slug may only contain lowercase letters, digits and single hyphens.";

    private static readonly Regex SlugRegex = new(
        @"^[a-z0-9]+(?:-[a-z0-9]+)*$",
        RegexOptions.Compiled);

    public static bool IsValid(string? slug)
        => !string.IsNullOrWhiteSpace(slug) && SlugRegex.IsMatch(slug);
}
