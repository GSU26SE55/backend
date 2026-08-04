using System.Collections.Concurrent;
using HandlebarsDotNet;

namespace NotificationService.Application.Templates;

public class HandlebarsTemplateRenderer : ITemplateRenderer
{
    /// <summary>
    /// Sprint 6.3 NOTI3-16 (#716) — ĐÃ BỎ <c>NoEscape = true</c>.
    ///
    /// **Vì sao phải bỏ:** nội dung chèn vào template đến từ dữ liệu người dùng nhập —
    /// tên khách hàng, tiêu đề ticket, ghi chú xử lý. Tắt escape nghĩa là ai đó đặt tiêu đề ticket
    /// chứa <c>&lt;script&gt;</c> sẽ được chèn thẳng vào email HTML gửi cho người khác (stored XSS, R-45).
    ///
    /// **Vì sao KHÔNG chỉ đơn giản đặt <c>NoEscape = false</c>:** encoder mặc định của Handlebars.NET
    /// mã hoá cả ký tự ngoài ASCII — "Nguyễn" thành <c>Nguy&amp;#7877;n</c>. Email HTML thì trình duyệt
    /// dựng lại đúng, nhưng <c>RenderInline</c> còn dùng cho **push và SMS**, nơi không ai giải mã
    /// entity ⇒ người dùng nhận chuỗi rác. Ghi chú cũ trên dòng <c>NoEscape</c> đã đúng ở điểm này;
    /// cái sai là cách chữa. Nên thay bằng <see cref="HtmlOnlyTextEncoder"/>: escape đúng 5 ký tự
    /// HTML, giữ nguyên tiếng Việt ở mọi kênh.
    ///
    /// **Cần HTML thật thì làm sao:** dùng <c>{{{ba-ngoặc}}}</c> ở đúng chỗ đó. Hiện 16 template
    /// đều chỉ chèn dữ liệu thuần (tên, mã, ngày giờ, ghi chú) nên KHÔNG chỗ nào cần — có test bao
    /// để nếu ai thêm <c>{{{...}}}</c> mới thì phải cân nhắc có ý thức.
    /// </summary>
    private static readonly IHandlebars _handlebars = Handlebars.Create(new HandlebarsConfiguration
    {
        TextEncoder = HtmlOnlyTextEncoder.Instance,
    });
    private static readonly ConcurrentDictionary<string, HandlebarsTemplate<object, object>> _cache = new();

    private static readonly ConcurrentDictionary<string, HandlebarsTemplate<object, object>> _inlineCache = new();

    public string Render(string templateName, object data)
    {
        var compiled = _cache.GetOrAdd(templateName, LoadAndCompile);
        return compiled(data);
    }

    /// <summary>
    /// Sprint 6.2 NOTI-14 (#685) — compile + cache template lấy từ DB (key = chính nội dung template
    /// nên template đổi trong DB là tự dùng bản mới, không cần restart).
    /// </summary>
    public string RenderInline(string templateSource, object data)
    {
        if (string.IsNullOrWhiteSpace(templateSource))
            return string.Empty;

        var compiled = _inlineCache.GetOrAdd(templateSource, src => _handlebars.Compile(src));
        return compiled(data);
    }

    /// <summary>
    /// Template lưu dưới dạng <c>.html</c> embedded resource (trước đây là <c>.hbs</c> — đổi để mở
    /// bằng trình duyệt/IDE xem trước được; nội dung vẫn là cú pháp Handlebars).
    /// Phải đi kèm <c>&lt;EmbeddedResource Include="Templates\*.html" /&gt;</c> trong csproj.
    /// </summary>
    private static HandlebarsTemplate<object, object> LoadAndCompile(string templateName)
    {
        var assembly = typeof(HandlebarsTemplateRenderer).Assembly;
        var resourceName = $"NotificationService.Application.Templates.{templateName}.html";

        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"Email template '{templateName}.html' không tìm thấy. " +
                $"Kiểm tra EmbeddedResource trong csproj và tên file có đúng không. " +
                $"Resource name expected: '{resourceName}'");

        using var reader = new StreamReader(stream);
        return _handlebars.Compile(reader.ReadToEnd());
    }
}
