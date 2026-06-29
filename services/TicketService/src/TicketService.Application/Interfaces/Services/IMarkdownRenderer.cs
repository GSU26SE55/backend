namespace TicketService.Application.Interfaces.Services;

public interface IMarkdownRenderer
{
    /// <summary>Render Markdown → HTML đã sanitize. &lt;img&gt; chỉ giữ lại nếu src trùng 1 attachment ID trong <paramref name="ticketAttachmentIds"/>.</summary>
    string RenderToHtml(string markdownBody, IEnumerable<Guid> ticketAttachmentIds);
}
