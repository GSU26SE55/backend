using AuthService.Application.DTOs.Response.Auth;
using AuthService.Application.Validation;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;

namespace AuthService.Application.CQRS.Command.Auth;

public class LoginCommand : IRequest<LoginResponse>, IValidatable<LoginResponse>
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;

    public Task<LoginResponse> ValidateAsync()
    {
        var response = new LoginResponse();

        AccountFieldPolicy.AddEmailErrors(response.ListErrors, Email);

        // Login chỉ kiểm tra có nhập hay không — độ mạnh là việc của lúc đặt mật khẩu,
        // áp policy ở đây sẽ tiết lộ luật mật khẩu cho người chưa đăng nhập.
        if (string.IsNullOrWhiteSpace(Password))
        {
            response.ListErrors.Add(new Errors
            {
                Field = "Password",
                Detail = "Password is required."
            });
        }

        if (response.ListErrors.Count > 0)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Invalid input data.";
        }

        return Task.FromResult(response);
    }
}
