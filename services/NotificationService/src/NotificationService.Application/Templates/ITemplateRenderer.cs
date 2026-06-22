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
}
