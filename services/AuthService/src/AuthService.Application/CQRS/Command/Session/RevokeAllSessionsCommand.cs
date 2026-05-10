using AuthService.Application.DTOs.Response.RefreshToken;
using MediatR;

namespace AuthService.Application.CQRS.Command.Session;

public class RevokeAllSessionsCommand : IRequest<SessionActionResponse>
{
    public bool ExceptCurrent { get; set; } = true;
    public string? CurrentRefreshToken { get; set; }
}
