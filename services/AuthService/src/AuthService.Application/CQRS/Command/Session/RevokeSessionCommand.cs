using AuthService.Application.DTOs.Response.RefreshToken;
using MediatR;

namespace AuthService.Application.CQRS.Command.Session;

public class RevokeSessionCommand : IRequest<SessionActionResponse>
{
    public Guid SessionId { get; set; }
}
