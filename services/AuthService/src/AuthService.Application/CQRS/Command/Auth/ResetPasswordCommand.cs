using System.Text.RegularExpressions;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;

namespace AuthService.Application.CQRS.Command.Auth;

public class ResetPasswordCommand : IRequest<CommonResponse<string>>, IValidatable<CommonResponse<string>>
{
    private static readonly Regex StrongPasswordRegex = new(
        @"^(?=.*[A-Z])(?=.*[a-z])(?=.*\d)(?=.*[!@#$%^&*()_+\-=\[\]{};':""\\|,.<>\/?]).{8,}$",
        RegexOptions.Compiled);

    public string ResetToken { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;

    public Task<CommonResponse<string>> ValidateAsync()
    {
        var response = new CommonResponse<string>();

        if (string.IsNullOrWhiteSpace(ResetToken))
            response.ListErrors.Add(new Errors { Field = "ResetToken", Detail = "Reset token không được để trống." });

        if (string.IsNullOrWhiteSpace(NewPassword))
            response.ListErrors.Add(new Errors { Field = "NewPassword", Detail = "Mật khẩu mới không được để trống." });
        else if (NewPassword.Length > 100)
            response.ListErrors.Add(new Errors { Field = "NewPassword", Detail = "Mật khẩu tối đa 100 ký tự." });
        else if (!StrongPasswordRegex.IsMatch(NewPassword))
            response.ListErrors.Add(new Errors
            {
                Field = "NewPassword",
                Detail = "Mật khẩu phải tối thiểu 8 ký tự, có chữ hoa, chữ thường, số và ký tự đặc biệt."
            });

        if (response.ListErrors.Count > 0)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Dữ liệu đầu vào không hợp lệ.";
        }
        return Task.FromResult(response);
    }
}
