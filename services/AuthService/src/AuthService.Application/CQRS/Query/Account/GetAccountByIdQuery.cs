using System.Text.Json.Serialization;
using AuthService.Application.DTOs.Response.Account;
using MediatR;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace AuthService.Application.CQRS.Query.Account;

public class GetAccountByIdQuery : IRequest<AccountResponse>
{
    [JsonIgnore]
    [BindNever]
    public Guid Id { get; set; }
}
