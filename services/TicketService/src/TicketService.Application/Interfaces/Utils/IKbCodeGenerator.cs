namespace TicketService.Application.Interfaces.Utils;

public interface IKbCodeGenerator
{
    Task<string> GenerateNextCodeAsync(CancellationToken ct = default);
}
