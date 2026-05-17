using AuthService.Application.DTOs.Response.Auth;
using AuthService.Application.Validation;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;

namespace AuthService.Application.CQRS.Command.Auth;

/// <summary>
/// User accept invitation token và đặt mật khẩu lần đầu. Sau khi accept thành công, account
/// chuyển sang Status=Active và issue JWT để login luôn.
/// </summary>
public class AcceptInviteCommand : IRequest<LoginResponse>, IValidatable<LoginResponse>
{
    public string InvitationToken { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string ConfirmPassword { get; set; } = string.Empty;

    public Task<LoginResponse> ValidateAsync()
    {
        var response = new LoginResponse();

        if (string.IsNullOrWhiteSpace(InvitationToken))
            response.ListErrors.Add(new Errors { Field = "InvitationToken", Detail = "Token không được để trống." });

        PasswordPolicy.AddStrongPasswordErrors(response.ListErrors, Password, nameof(Password), "Mật khẩu");

        if (Password != ConfirmPassword)
            response.ListErrors.Add(new Errors { Field = "ConfirmPassword", Detail = "Xác nhận mật khẩu không khớp." });

        if (response.ListErrors.Count > 0)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Dữ liệu đầu vào không hợp lệ.";
        }

        return Task.FromResult(response);
    }
}
