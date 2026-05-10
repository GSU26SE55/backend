using System.Text.RegularExpressions;
using AuthService.Application.DTOs.Response.Auth;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;

namespace AuthService.Application.CQRS.Command.Auth;

public class LoginCommand : IRequest<LoginResponse>, IValidatable<LoginResponse>
{
    private const int EmailMaxLength = 256;
    private const int PasswordMinLength = 6;
    private const int PasswordMaxLength = 100;

    private static readonly Regex EmailRegex = new(
        @"^[^\s@]+@[^\s@]+\.[^\s@]+$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;

    public Task<LoginResponse> ValidateAsync()
    {
        var response = new LoginResponse();

        if (string.IsNullOrWhiteSpace(Email))
        {
            response.ListErrors.Add(new Errors
            {
                Field = "Email",
                Detail = "Email không được để trống."
            });
        }
        else
        {
            var trimmed = Email.Trim();

            if (trimmed.Length > EmailMaxLength)
            {
                response.ListErrors.Add(new Errors
                {
                    Field = "Email",
                    Detail = $"Email không được vượt quá {EmailMaxLength} ký tự."
                });
            }
            else if (!EmailRegex.IsMatch(trimmed))
            {
                response.ListErrors.Add(new Errors
                {
                    Field = "Email",
                    Detail = "Email không đúng định dạng."
                });
            }
        }

        if (string.IsNullOrWhiteSpace(Password))
        {
            response.ListErrors.Add(new Errors
            {
                Field = "Password",
                Detail = "Mật khẩu không được để trống."
            });
        }
        else
        {
            if (Password.Length < PasswordMinLength)
            {
                response.ListErrors.Add(new Errors
                {
                    Field = "Password",
                    Detail = $"Mật khẩu phải có ít nhất {PasswordMinLength} ký tự."
                });
            }
            else if (Password.Length > PasswordMaxLength)
            {
                response.ListErrors.Add(new Errors
                {
                    Field = "Password",
                    Detail = $"Mật khẩu không được vượt quá {PasswordMaxLength} ký tự."
                });
            }

            if (Password.Contains(' '))
            {
                response.ListErrors.Add(new Errors
                {
                    Field = "Password",
                    Detail = "Mật khẩu không được chứa khoảng trắng."
                });
            }
        }

        if (response.ListErrors.Count > 0)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Dữ liệu đầu vào không hợp lệ.";
        }

        return Task.FromResult(response);
    }
}
