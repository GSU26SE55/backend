using System.Text;
using HandlebarsDotNet;

namespace NotificationService.Application.Templates;

/// <summary>
/// Sprint 6.3 NOTI3-16 (#716) — encoder chỉ escape 5 ký tự có ý nghĩa trong HTML
/// (<c>&amp; &lt; &gt; " '</c>) và để nguyên mọi ký tự Unicode.
///
/// **Vì sao không dùng encoder mặc định của Handlebars.NET?**
/// Nó mã hoá cả ký tự ngoài ASCII: "Nguyễn" → <c>Nguy&amp;#7877;n</c>. Trong email HTML thì trình
/// duyệt vẫn dựng lại đúng, nhưng renderer này còn dùng cho **push và SMS** (<c>RenderInline</c> ở
/// <c>NotificationDispatcher</c>) — nơi không có ai giải mã entity, nên người dùng sẽ nhận được
/// một chuỗi rác. Đó chính là lý do trước đây có người bật <c>NoEscape = true</c> cho xong.
///
/// **Vì sao không giữ <c>NoEscape</c>?** Nội dung chèn vào template đến từ dữ liệu người dùng nhập
/// (tên khách hàng, tiêu đề ticket, ghi chú xử lý). Tắt escape nghĩa là tiêu đề ticket chứa
/// <c>&lt;script&gt;</c> được chèn thẳng vào email gửi cho người khác — stored XSS (R-45).
///
/// Encoder này lấy cả hai: an toàn với HTML, mà tiếng Việt vẫn nguyên vẹn ở mọi kênh.
/// Cần HTML thô thì dùng <c>{{{ba-ngoặc}}}</c> — bỏ qua encoder một cách có ý thức.
/// </summary>
public sealed class HtmlOnlyTextEncoder : ITextEncoder
{
    public static readonly HtmlOnlyTextEncoder Instance = new();

    public void Encode(StringBuilder text, TextWriter target)
    {
        if (text is null)
            return;

        for (var i = 0; i < text.Length; i++)
            WriteEscaped(text[i], target);
    }

    public void Encode(string text, TextWriter target)
    {
        if (string.IsNullOrEmpty(text))
            return;

        foreach (var c in text)
            WriteEscaped(c, target);
    }

    public void Encode<T>(T text, TextWriter target) where T : IEnumerator<char>
    {
        if (text is null)
            return;

        while (text.MoveNext())
            WriteEscaped(text.Current, target);
    }

    private static void WriteEscaped(char c, TextWriter target)
    {
        switch (c)
        {
            case '&':
                target.Write("&amp;");
                break;
            case '<':
                target.Write("&lt;");
                break;
            case '>':
                target.Write("&gt;");
                break;
            case '"':
                target.Write("&quot;");
                break;
            // Nháy đơn phải escape: thuộc tính HTML dùng nháy đơn rất phổ biến và bỏ sót nó
            // vẫn cho phép thoát khỏi thuộc tính để gắn onerror=…
            case '\'':
                target.Write("&#39;");
                break;
            default:
                target.Write(c);
                break;
        }
    }
}
