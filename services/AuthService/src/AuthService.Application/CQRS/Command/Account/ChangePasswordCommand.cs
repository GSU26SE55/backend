using System.Text.Json.Serialization;
using AuthService.Application.DTOs.Response.Account;
using AuthService.Application.Validation;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;

namespace AuthService.Application.CQRS.Command.Account;

public class ChangePasswordCommand : IRequest<AccountActionResponse>, IValidatable<AccountActionResponse>
{
    [JsonIgnore]
    public Guid AccountId { get; set; }
    public string CurrentPassword { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
    public string ConfirmPassword { get; set; } = string.Empty;

    public Task<AccountActionResponse> ValidateAsync()
    {
        var response = new AccountActionResponse();

        if (AccountId == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = "AccountId", Detail = "Account không hợp lệ." });

        if (string.IsNullOrWhiteSpace(CurrentPassword))
            response.ListErrors.Add(new Errors { Field = "CurrentPassword", Detail = "Mật khẩu hiện tại không được để trống." });

        PasswordPolicy.AddStrongPasswordErrors(response.ListErrors, NewPassword, nameof(NewPassword), "Mật khẩu mới");

        var hasCrossFieldError = false;

        if (NewPassword != ConfirmPassword)
        {
            response.ListErrors.Add(new Errors { Field = "ConfirmPassword", Detail = "Xác nhận mật khẩu không khớp." });
            hasCrossFieldError = true;
        }

        if (!string.IsNullOrEmpty(CurrentPassword) && CurrentPassword == NewPassword)
        {
            response.ListErrors.Add(new Errors { Field = "NewPassword", Detail = "Mật khẩu mới phải khác mật khẩu hiện tại." });
            hasCrossFieldError = true;
        }

        if (response.ListErrors.Count > 0)
        {
            response.IsSuccess = false;
            // 422 nếu có cross-field business rule (confirm không match / new = old), 400 cho field validation đơn
            response.StatusCode = hasCrossFieldError ? 422 : 400;
            response.Message = "Dữ liệu đầu vào không hợp lệ.";
        }

        return Task.FromResult(response);
    }
}
