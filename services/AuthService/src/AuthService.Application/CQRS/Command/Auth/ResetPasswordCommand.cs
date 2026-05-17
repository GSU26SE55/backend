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
            response.ListErrors.Add(new Errors { Field = "ResetToken", Detail = "Reset token không được để trống." });

        PasswordPolicy.AddStrongPasswordErrors(response.ListErrors, NewPassword, nameof(NewPassword), "Mật khẩu mới");

        if (response.ListErrors.Count > 0)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Dữ liệu đầu vào không hợp lệ.";
        }
        return Task.FromResult(response);
    }
}
