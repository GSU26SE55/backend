namespace TicketService.Application.Interfaces.Utils;

public interface ITicketCodeGenerator
{
    Task<string> GenerateAsync();
}
