namespace NotificationService.Application.Templates;

public interface ITemplateRenderer
{
    /// <summary>
    /// Render một .hbs template (embedded resource) với data object.
    /// </summary>
    /// <param name="templateName">Tên file không có đuôi .hbs (ví dụ: "sla-breach")</param>
    /// <param name="data">Object chứa các biến được dùng trong template</param>
    /// <returns>HTML string đã render</returns>
    string Render(string templateName, object data);

    /// <summary>
    /// Sprint 6.2 NOTI-14 (#685) — render một chuỗi template có sẵn (không phải file embedded),
    /// dùng cho <c>NotificationTemplate.TitleTemplate</c>/<c>BodyTemplate</c> lấy từ DB.
    /// </summary>
    /// <param name="templateSource">Nội dung template Handlebars.</param>
    /// <param name="data">Object/dictionary chứa biến dùng trong template.</param>
    string RenderInline(string templateSource, object data);
}
