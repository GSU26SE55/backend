using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using AuthService.Application.DTOs.Response.Account;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;

namespace AuthService.Application.CQRS.Command.Account;

public class ConfirmEmailChangeCommand : IRequest<AccountActionResponse>, IValidatable<AccountActionResponse>
{
    private static readonly Regex OtpRegex = new(@"^\d{6}$", RegexOptions.Compiled);

    [JsonIgnore]
    public Guid AccountId { get; set; }
    public string Otp { get; set; } = string.Empty;

    public Task<AccountActionResponse> ValidateAsync()
    {
        var response = new AccountActionResponse();

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
