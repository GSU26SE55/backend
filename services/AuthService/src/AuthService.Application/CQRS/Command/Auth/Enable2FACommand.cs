using System.Text.Json.Serialization;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;

namespace AuthService.Application.CQRS.Command.Auth;

public class Enable2FACommand : IRequest<CommonResponse<TwoFactorSecretDto>>, IValidatable<CommonResponse<TwoFactorSecretDto>>
{
    [JsonIgnore]
    public Guid AccountId { get; set; }

    public Task<CommonResponse<TwoFactorSecretDto>> ValidateAsync()
    {
        var response = new CommonResponse<TwoFactorSecretDto>();
        if (AccountId == Guid.Empty)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Invalid AccountId.";
            response.ListErrors.Add(new Errors { Field = "AccountId", Detail = "Invalid AccountId." });
        }
        return Task.FromResult(response);
    }
}

public class TwoFactorSecretDto
{
    public string Secret { get; set; } = string.Empty;
    public string OtpAuthUri { get; set; } = string.Empty;
}
