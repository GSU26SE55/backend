using System.Text.Json.Serialization;
using AuthService.Application.DTOs.Response.RefreshToken;
using MediatR;

namespace AuthService.Application.CQRS.Command.Session;

public class RevokeSessionCommand : IRequest<SessionActionResponse>
{
    [JsonIgnore]
    public Guid SessionId { get; set; }
}
