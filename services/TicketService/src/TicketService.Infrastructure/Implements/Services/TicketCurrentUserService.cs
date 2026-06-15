using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using TicketService.Application.Interfaces.Services;

namespace TicketService.Infrastructure.Implements.Services;

public class TicketCurrentUserService : ITicketCurrentUserService
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public TicketCurrentUserService(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    private ClaimsPrincipal? User => _httpContextAccessor.HttpContext?.User;

    // Ưu tiên lấy AccountId từ JWT mới, nếu không có thì fallback về NameIdentifier
    public string? UserId => User?.FindFirstValue("AccountId")
                             ?? User?.FindFirstValue(ClaimTypes.NameIdentifier);

    public string? Email => User?.FindFirstValue("email")
                            ?? User?.FindFirstValue(ClaimTypes.Email);

    public string? FullName => User?.FindFirstValue("FullName")
                               ?? User?.FindFirstValue(ClaimTypes.Name);

    public string? Role => User?.FindFirstValue("role")
                           ?? User?.FindFirstValue(ClaimTypes.Role);

    public IEnumerable<string> Permissions => User?.FindAll("perm").Select(c => c.Value)
                                              ?? Enumerable.Empty<string>();

    public bool IsAuthenticated => User?.Identity?.IsAuthenticated ?? false;

    public bool HasPermission(string permission)
    {
        return Permissions.Contains(permission);
    }
}
