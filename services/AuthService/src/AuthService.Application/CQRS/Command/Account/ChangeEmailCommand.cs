using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using AuthService.Application.DTOs.Response.Account;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;

namespace AuthService.Application.CQRS.Command.Account;

public class ChangeEmailCommand : IRequest<AccountActionResponse>, IValidatable<AccountActionResponse>
{
    private static readonly Regex EmailRegex = new(
        @"^[^\s@]+@[^\s@]+\.[^\s@]+$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    [JsonIgnore]
    public Guid AccountId { get; set; }
    public string NewEmail { get; set; } = string.Empty;
    public string CurrentPassword { get; set; } = string.Empty;

    public Task<AccountActionResponse> ValidateAsync()
    {
        var response = new AccountActionResponse();

        if (AccountId == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = "AccountId", Detail = "AccountId không hợp lệ." });

        if (string.IsNullOrWhiteSpace(NewEmail))
            response.ListErrors.Add(new Errors { Field = "NewEmail", Detail = "Email mới không được để trống." });
        else if (NewEmail.Trim().Length > 256)
            response.ListErrors.Add(new Errors { Field = "NewEmail", Detail = "Email tối đa 256 ký tự." });
        else if (!EmailRegex.IsMatch(NewEmail.Trim()))
            response.ListErrors.Add(new Errors { Field = "NewEmail", Detail = "Email không đúng định dạng." });

        if (string.IsNullOrWhiteSpace(CurrentPassword))
            response.ListErrors.Add(new Errors { Field = "CurrentPassword", Detail = "Mật khẩu hiện tại không được để trống." });

        if (response.ListErrors.Count > 0)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Dữ liệu đầu vào không hợp lệ.";
        }
        return Task.FromResult(response);
    }
}
