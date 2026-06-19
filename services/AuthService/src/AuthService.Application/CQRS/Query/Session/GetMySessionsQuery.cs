using AuthService.Application.DTOs.Response.RefreshToken;
using MediatR;

namespace AuthService.Application.CQRS.Query.Session;

public class GetMySessionsQuery : IRequest<SessionListResponse>
{
    public bool ActiveOnly { get; set; } = true;

    /// <summary>#AUTH-44: filter sessions theo DeviceId (hash UA hoặc header X-Device-Id). Null = mọi device.</summary>
    public string? DeviceId { get; set; }
}
