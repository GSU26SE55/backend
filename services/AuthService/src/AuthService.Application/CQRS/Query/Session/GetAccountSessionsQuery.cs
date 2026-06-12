using System.Text.Json.Serialization;
using AuthService.Application.DTOs.Response.RefreshToken;
using MediatR;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace AuthService.Application.CQRS.Query.Session;

public class GetAccountSessionsQuery : IRequest<SessionListResponse>
{
    [JsonIgnore]
    [BindNever]
    public Guid AccountId { get; set; }
    public bool ActiveOnly { get; set; } = true;
}
