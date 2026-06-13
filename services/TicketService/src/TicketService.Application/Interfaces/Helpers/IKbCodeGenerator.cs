namespace TicketService.Application.Interfaces.Helpers;

public interface IKbCodeGenerator
{
    Task<string> GenerateNextCodeAsync(CancellationToken ct = default);
}
