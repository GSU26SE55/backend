using AuthService.Application.Validation;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;

namespace AuthService.Application.CQRS.Command.Auth;

public class ResendResetOtpCommand : IRequest<CommonResponse<string>>, IValidatable<CommonResponse<string>>
{
    public string Email { get; set; } = string.Empty;

    public Task<CommonResponse<string>> ValidateAsync()
    {
        var response = new CommonResponse<string>();

        AccountFieldPolicy.AddEmailErrors(response.ListErrors, Email);

        if (response.ListErrors.Count > 0)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Invalid email.";
        }

        return Task.FromResult(response);
    }
}
