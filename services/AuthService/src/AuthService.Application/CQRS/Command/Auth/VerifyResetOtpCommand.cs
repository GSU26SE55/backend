using System.Text.RegularExpressions;
using AuthService.Application.Validation;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;

namespace AuthService.Application.CQRS.Command.Auth;

public class VerifyResetOtpCommand : IRequest<CommonResponse<ResetTokenDto>>, IValidatable<CommonResponse<ResetTokenDto>>
{
    private static readonly Regex OtpRegex = new(@"^\d{6}$", RegexOptions.Compiled);

    public string Email { get; set; } = string.Empty;
    public string Otp { get; set; } = string.Empty;

    public Task<CommonResponse<ResetTokenDto>> ValidateAsync()
    {
        var response = new CommonResponse<ResetTokenDto>();

        AccountFieldPolicy.AddEmailErrors(response.ListErrors, Email);

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

public class ResetTokenDto
{
    public string ResetToken { get; set; } = string.Empty;
    public int ExpiresInSeconds { get; set; }
}
