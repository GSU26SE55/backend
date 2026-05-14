using AuthService.Application.DTOs.Response.Account;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;
using System.Text.Json.Serialization;

namespace AuthService.Application.CQRS.Command.Auth;

public class LinkGoogleCommand : IRequest<AccountActionResponse>, IValidatable<AccountActionResponse>
{
    [JsonIgnore]
    public Guid AccountId { get; set; }
    public string IdToken { get; set; } = string.Empty;

    public Task<AccountActionResponse> ValidateAsync()
    {
        var response = new AccountActionResponse();

        if (AccountId == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = "AccountId", Detail = "AccountId không hợp lệ." });

        if (string.IsNullOrWhiteSpace(IdToken))
            response.ListErrors.Add(new Errors { Field = "IdToken", Detail = "IdToken không được để trống." });

        if (response.ListErrors.Count > 0)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Dữ liệu đầu vào không hợp lệ.";
        }
        return Task.FromResult(response);
    }
}
