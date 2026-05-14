using System.Text.RegularExpressions;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;

namespace AuthService.Application.CQRS.Command.Auth;

public class VerifyOtpCommand : IRequest<CommonResponse<string>>, IValidatable<CommonResponse<string>>
{
    private static readonly Regex EmailRegex = new(
        @"^[^\s@]+@[^\s@]+\.[^\s@]+$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex OtpRegex = new(@"^\d{6}$", RegexOptions.Compiled);

    public string Email { get; set; } = string.Empty;
    public string Otp { get; set; } = string.Empty;

    public Task<CommonResponse<string>> ValidateAsync()
    {
        var response = new CommonResponse<string>();

        if (string.IsNullOrWhiteSpace(Email) || !EmailRegex.IsMatch(Email.Trim()))
            response.ListErrors.Add(new Errors { Field = "Email", Detail = "Email không hợp lệ." });

        if (string.IsNullOrWhiteSpace(Otp) || !OtpRegex.IsMatch(Otp.Trim()))
            response.ListErrors.Add(new Errors { Field = "Otp", Detail = "OTP phải gồm 6 chữ số." });

        if (response.ListErrors.Count > 0)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Dữ liệu đầu vào không hợp lệ.";
        }

        return Task.FromResult(response);
    }
}
