using System.Text.Json.Serialization;
using AuthService.Application.DTOs.Response.Account;
using MediatR;

namespace AuthService.Application.CQRS.Command.Account;

public class RevokeRoleCommand : IRequest<AccountActionResponse>
{
    [JsonIgnore]
    public Guid AccountId { get; set; }

    [JsonIgnore]
    public Guid RoleId { get; set; }
}
