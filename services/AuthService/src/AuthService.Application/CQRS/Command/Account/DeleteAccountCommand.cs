using AuthService.Application.DTOs.Response.Account;
using MediatR;
using System.Text.Json.Serialization;

namespace AuthService.Application.CQRS.Command.Account;

public class DeleteAccountCommand : IRequest<AccountActionResponse>
{
    [JsonIgnore]
    public Guid Id { get; set; }
}
