namespace TicketService.Application.Interfaces.Helpers;

public interface ITicketCodeGenerator
{
    Task<string> GenerateAsync();
}
