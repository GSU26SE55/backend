using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;

namespace AuthService.Application.CQRS.Command.Auth;

public class VerifyPhoneOtpCommand : IRequest<CommonResponse<string>>, IValidatable<CommonResponse<string>>
{
    private static readonly Regex OtpRegex = new(@"^\d{6}$", RegexOptions.Compiled);

    [JsonIgnore]
    public Guid AccountId { get; set; }
    public string Otp { get; set; } = string.Empty;

    public Task<CommonResponse<string>> ValidateAsync()
    {
        var response = new CommonResponse<string>();

        if (AccountId == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = "AccountId", Detail = "Invalid AccountId." });

        if (string.IsNullOrWhiteSpace(Otp) || !OtpRegex.IsMatch(Otp.Trim()))
            response.ListErrors.Add(new Errors { Field = "Otp", Detail = "OTP must be 6 digits." });

        if (response.ListErrors.Count > 0)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Invalid input data.";
        }
        return Task.FromResult(response);
    }
}
