using AuthService.Application.Validation;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;

namespace AuthService.Application.CQRS.Command.Auth;

public class ResetPasswordCommand : IRequest<CommonResponse<string>>, IValidatable<CommonResponse<string>>
{
    public string ResetToken { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;

    public Task<CommonResponse<string>> ValidateAsync()
    {
        var response = new CommonResponse<string>();

        if (string.IsNullOrWhiteSpace(ResetToken))
            response.ListErrors.Add(new Errors { Field = "ResetToken", Detail = "Reset token is required." });

        PasswordPolicy.AddStrongPasswordErrors(response.ListErrors, NewPassword, nameof(NewPassword), "New password");

        if (response.ListErrors.Count > 0)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Invalid input data.";
        }
        return Task.FromResult(response);
    }
}
