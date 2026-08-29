using System.Text.RegularExpressions;
using SharedContracts.Common.Responses;
using TicketService.Application.Common.Models;

namespace TicketService.Application.Common.Helpers;

/// <summary>
/// Luật nội dung comment, dùng chung cho add/edit/override-edit.
///
/// <para>Trước đây chỉ <c>ChatAddCommand</c> chặn nội dung toàn khoảng trắng/emoji, còn hai
/// đường edit thì không — nên post "hello" rồi sửa thành "👍👍" là lách được luật mà đường
/// tạo cấm. Hai đường edit cũng hardcode 10000 kèm ghi chú "PHẢI đồng bộ tay", tức là ba
/// bản sao của cùng một con số.</para>
/// </summary>
public static class ChatBodyPolicy
{
    // Heuristic — chỉ cover whitespace + emoji range phổ biến (BMP symbol/dingbat + surrogate
    // pair khối emoji), không exhaustive toàn bộ Unicode emoji (#518 — Simplicity First).
    private static readonly Regex WhitespaceOrEmojiOnlyRegex = new(
        "^[\\s\\u2600-\\u27BF\\u2190-\\u21FF\\u2B00-\\u2BFF\\uD83C-\\uDBFF\\uDC00-\\uDFFF\\uFE0F\\u200D]*$",
        RegexOptions.Compiled);

    public static void AddBodyErrors(ICollection<Errors> errors, string? body, string field = "Body")
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            errors.Add(new Errors { Field = field, Detail = "Comment content is required." });
            return;
        }

        // ValidateAsync() không nhận DI nên không inject được IOptions<ChatOptions> — dùng hằng
        // số MaxBodyLengthDefault làm nguồn duy nhất thay vì chép số vào từng command.
        if (body.Length > ChatOptions.MaxBodyLengthDefault)
        {
            errors.Add(new Errors
            {
                Field = field,
                Detail = $"Comment content must be at most {ChatOptions.MaxBodyLengthDefault} characters."
            });
            return;
        }

        if (WhitespaceOrEmojiOnlyRegex.IsMatch(body))
            errors.Add(new Errors { Field = field, Detail = "Content must not contain only whitespace or emoji." });
    }
}
