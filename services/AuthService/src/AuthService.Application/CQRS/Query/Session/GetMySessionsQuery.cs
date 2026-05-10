using AuthService.Application.DTOs.Response.RefreshToken;
using MediatR;

namespace AuthService.Application.CQRS.Query.Session;

public class GetMySessionsQuery : IRequest<SessionListResponse>
{
    public bool ActiveOnly { get; set; } = true;
}
