using System.Text.RegularExpressions;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;

namespace AuthService.Application.CQRS.Command.Auth;

public class ResendResetOtpCommand : IRequest<CommonResponse<string>>, IValidatable<CommonResponse<string>>
{
    private static readonly Regex EmailRegex = new(
        @"^[^\s@]+@[^\s@]+\.[^\s@]+$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public string Email { get; set; } = string.Empty;

    public Task<CommonResponse<string>> ValidateAsync()
    {
        var response = new CommonResponse<string>();
        if (string.IsNullOrWhiteSpace(Email) || !EmailRegex.IsMatch(Email.Trim()))
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Invalid email.";
            response.ListErrors.Add(new Errors { Field = "Email", Detail = "Invalid email." });
        }
        return Task.FromResult(response);
    }
}
