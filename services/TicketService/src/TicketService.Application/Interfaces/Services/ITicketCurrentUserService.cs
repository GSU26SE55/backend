using SharedInfrastructure.Services;

namespace TicketService.Application.Interfaces.Services;

public interface ITicketCurrentUserService : ICurrentUserService
{
    string? Email { get; }
    string? FullName { get; }
    string? Role { get; }
    IEnumerable<string> Permissions { get; }
    bool IsAuthenticated { get; }

    bool HasPermission(string permission);
}
