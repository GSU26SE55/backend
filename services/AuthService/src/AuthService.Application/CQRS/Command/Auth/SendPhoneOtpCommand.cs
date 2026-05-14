using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;
using System.Text.Json.Serialization;

namespace AuthService.Application.CQRS.Command.Auth;

public class SendPhoneOtpCommand : IRequest<CommonResponse<string>>, IValidatable<CommonResponse<string>>
{
    /// <summary>Lấy từ JWT claim, controller gán.</summary>
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
