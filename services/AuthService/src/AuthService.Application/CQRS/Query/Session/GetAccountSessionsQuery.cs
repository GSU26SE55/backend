using AuthService.Application.DTOs.Response.RefreshToken;
using MediatR;

namespace AuthService.Application.CQRS.Query.Session;

public class GetAccountSessionsQuery : IRequest<SessionListResponse>
{
    public Guid AccountId { get; set; }
    public bool ActiveOnly { get; set; } = true;
}
