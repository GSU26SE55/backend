using System.Text.Json.Serialization;
using AuthService.Application.DTOs.Response.RefreshToken;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;

namespace AuthService.Application.CQRS.Command.Session;

public class AdminRevokeAccountSessionsCommand : IRequest<SessionActionResponse>, IValidatable<SessionActionResponse>
{
    [JsonIgnore]
    public Guid AccountId { get; set; }
    public string? Reason { get; set; }

    public Task<SessionActionResponse> ValidateAsync()
    {
        var response = new SessionActionResponse();
        if (AccountId == Guid.Empty)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "AccountId không hợp lệ.";
            response.ListErrors.Add(new Errors { Field = "AccountId", Detail = "AccountId không hợp lệ." });
        }
        return Task.FromResult(response);
    }
}
