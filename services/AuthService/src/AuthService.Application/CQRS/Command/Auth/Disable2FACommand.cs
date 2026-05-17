using System.Text.Json.Serialization;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;

namespace AuthService.Application.CQRS.Command.Auth;

public class Disable2FACommand : IRequest<CommonResponse<string>>, IValidatable<CommonResponse<string>>
{
    [JsonIgnore]
    public Guid AccountId { get; set; }

    public Task<CommonResponse<string>> ValidateAsync()
    {
        var response = new CommonResponse<string>();
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
