namespace TicketService.Application.Interfaces.Services;

public interface ITemplateRenderer
{
    string Render(string template, object? context = null);
}
