using System.Text.RegularExpressions;
using NotificationService.Domain.Enums;

namespace NotificationService.Application.Templates;

/// <summary>
/// Kiểm **tên biến** của template trước khi lưu — bổ sung cho <see cref="TemplateSyntaxGuard"/> vốn
/// chỉ kiểm cú pháp.
///
/// <para><b>Vì sao cú pháp thôi là chưa đủ.</b> <c>{{ticketCode}}</c> đúng cú pháp hoàn hảo. Nó chỉ
/// sai ở chỗ consumer ghi khoá tên <c>code</c>. Handlebars gặp biến lạ thì trả chuỗi rỗng chứ không
/// ném, nên template kiểu này lưu được, gửi được, và người nhận đọc phải "Ticket mới " — cụt đuôi.
/// Không log, không metric, không test nào bắt được. Toàn bộ bộ template của dự án từng dính đúng
/// lỗi này vì tác giả soạn theo một hợp đồng payload tưởng tượng.</para>
///
/// <para><b>Vì sao chặn chứ không cảnh báo.</b> Lưu xong là template có hiệu lực ngay cho mọi thông
/// báo của cặp (type × channel) đó. Một cảnh báo bị bỏ qua sẽ thành hàng trăm tin nhắn cụt trước khi
/// có ai để ý. Đây là điểm cuối cùng còn kịp chặn.</para>
/// </summary>
public static class TemplateVariableGuard
{
    /// <summary>
    /// Bắt mọi cụm <c>{{...}}</c> và <c>{{{...}}}</c>. Nội dung bên trong được bóc tách tiếp ở
    /// <see cref="ExtractVariables"/> — regex chỉ cắt cụm, không cố hiểu ngữ nghĩa.
    /// </summary>
    private static readonly Regex MustachePattern =
        new(@"\{\{\{?([^{}]*)\}?\}\}", RegexOptions.Compiled);

    /// <summary>
    /// Từ khoá Handlebars, không phải biến của người soạn nên không đem đi đối chiếu danh mục.
    /// </summary>
    private static readonly HashSet<string> Reserved =
        new(StringComparer.OrdinalIgnoreCase) { "this", "else", "true", "false", "null" };

    /// <summary>
    /// Trả về thông báo lỗi nếu có biến không nằm trong danh mục của <paramref name="type"/>;
    /// <c>null</c> nếu mọi biến đều hợp lệ.
    /// </summary>
    public static string? FindUnknownVariables(
        NotificationTypeEnum type, string titleTemplate, string bodyTemplate)
    {
        var allowed = NotificationTemplateVariables.AllowedFor(type);

        var unknown = ExtractVariables(titleTemplate)
            .Concat(ExtractVariables(bodyTemplate))
            .Where(v => !allowed.Contains(v))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(v => v, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (unknown.Count == 0)
            return null;

        var details = unknown.Select(v =>
        {
            var suggestion = Suggest(v, allowed);
            return suggestion is null ? $"{{{{{v}}}}}" : $"{{{{{v}}}}} (did you mean {{{{{suggestion}}}}}?)";
        });

        var allowedList = string.Join(", ", allowed.OrderBy(a => a, StringComparer.OrdinalIgnoreCase));

        return $"Template uses variables that don't exist in this notification type's data: "
             + $"{string.Join(", ", details)}. These variables will render as empty, so recipients will see a truncated message. "
             + $"Valid variables: {allowedList}.";
    }

    /// <summary>
    /// Bóc tên biến khỏi một chuỗi template.
    ///
    /// <para>Cố ý <b>bỏ qua</b> những gì không phải biến của người soạn: chú thích <c>{{!...}}</c>,
    /// thẻ đóng <c>{{/if}}</c>, partial <c>{{&gt;...}}</c>. Với block <c>{{#if code}}</c> thì <c>#if</c>
    /// là helper, <c>code</c> mới là biến. Với lời gọi nhiều token, token đầu là helper.</para>
    ///
    /// <para>Đường dẫn lồng <c>{{a.b}}</c> chỉ lấy gốc <c>a</c> — model là từ điển phẳng nên chỉ gốc
    /// mới có nghĩa để đối chiếu.</para>
    /// </summary>
    public static IEnumerable<string> ExtractVariables(string? template)
    {
        if (string.IsNullOrWhiteSpace(template))
            yield break;

        foreach (Match match in MustachePattern.Matches(template))
        {
            var inner = match.Groups[1].Value.Trim();

            if (inner.Length == 0)
                continue;

            // Chú thích, thẻ đóng, partial — không chứa biến cần kiểm.
            if (inner[0] is '!' or '/' or '>' or '@')
                continue;

            // Mở block: {{#if x}} / {{^unless x}} — bỏ token helper ở đầu.
            var isBlock = inner[0] is '#' or '^';
            if (isBlock)
                inner = inner[1..].TrimStart();

            var tokens = inner.Split(
                [' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries);

            if (tokens.Length == 0)
                continue;

            // Một token đứng một mình là biến — kể cả trong block, vì {{#items}} là dạng section
            // lấy thẳng biến. Từ hai token trở lên thì token đầu là tên helper ({{#if code}},
            // {{formatDate createdAt}}), phần còn lại mới là biến.
            var candidates = tokens.Length == 1 ? tokens : tokens[1..];

            foreach (var raw in candidates)
            {
                var name = raw.Trim();

                // Hằng chuỗi/số trong lời gọi helper — không phải biến.
                if (name.Length == 0 || name[0] is '"' or '\'' or '(' || char.IsDigit(name[0]))
                    continue;

                // Đường dẫn lồng: chỉ gốc mới tra được trong model phẳng.
                var dot = name.IndexOfAny(['.', '/']);
                if (dot == 0)
                    continue;
                if (dot > 0)
                    name = name[..dot];

                if (name.Length == 0 || Reserved.Contains(name))
                    continue;

                yield return name;
            }
        }
    }

    /// <summary>
    /// Gợi ý biến đúng cho một tên sai. Ưu tiên quan hệ chứa nhau (<c>serialNumber</c> ↔
    /// <c>assetSerialNumber</c>, <c>threshold</c> ↔ <c>thresholdValue</c>, <c>ticketCode</c> ↔
    /// <c>code</c>) vì đó là dạng nhầm phổ biến nhất; sau đó tới khoảng cách sửa đổi để bắt lỗi gõ
    /// thừa/thiếu ký tự.
    /// </summary>
    private static string? Suggest(string unknown, IReadOnlySet<string> allowed)
    {
        var contains = allowed
            .Where(a => a.Contains(unknown, StringComparison.OrdinalIgnoreCase)
                     || unknown.Contains(a, StringComparison.OrdinalIgnoreCase))
            .OrderBy(a => Math.Abs(a.Length - unknown.Length))
            .FirstOrDefault();

        if (contains is not null)
            return contains;

        // Ngưỡng 1/3 độ dài: đủ rộng để bắt "ticketCodeeeeeee" → "ticketCode" nếu có trong danh mục,
        // đủ hẹp để không gợi ý bừa một biến chẳng liên quan.
        var threshold = Math.Max(2, unknown.Length / 3);

        return allowed
            .Select(a => (Name: a, Distance: EditDistance(unknown, a)))
            .Where(x => x.Distance <= threshold)
            .OrderBy(x => x.Distance)
            .Select(x => x.Name)
            .FirstOrDefault();
    }

    /// <summary>Khoảng cách Levenshtein, không phân biệt hoa thường.</summary>
    private static int EditDistance(string a, string b)
    {
        a = a.ToLowerInvariant();
        b = b.ToLowerInvariant();

        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];

        for (var j = 0; j <= b.Length; j++)
            previous[j] = j;

        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                current[j] = Math.Min(
                    Math.Min(current[j - 1] + 1, previous[j] + 1),
                    previous[j - 1] + cost);
            }

            (previous, current) = (current, previous);
        }

        return previous[b.Length];
    }
}
