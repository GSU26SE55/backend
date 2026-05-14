using AuthService.Application.DTOs.Response.Account;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;
using System.Text.Json.Serialization;

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

        if (string.IsNullOrWhiteSpace(NewPassword))
            response.ListErrors.Add(new Errors { Field = "NewPassword", Detail = "Mật khẩu mới không được để trống." });
        else
        {
            if (NewPassword.Length < 6)
                response.ListErrors.Add(new Errors { Field = "NewPassword", Detail = "Mật khẩu mới tối thiểu 6 ký tự." });
            if (NewPassword.Length > 100)
                response.ListErrors.Add(new Errors { Field = "NewPassword", Detail = "Mật khẩu mới tối đa 100 ký tự." });
            if (NewPassword.Contains(' '))
                response.ListErrors.Add(new Errors { Field = "NewPassword", Detail = "Mật khẩu không được chứa khoảng trắng." });
        }

        if (NewPassword != ConfirmPassword)
            response.ListErrors.Add(new Errors { Field = "ConfirmPassword", Detail = "Xác nhận mật khẩu không khớp." });

        if (!string.IsNullOrEmpty(CurrentPassword) && CurrentPassword == NewPassword)
            response.ListErrors.Add(new Errors { Field = "NewPassword", Detail = "Mật khẩu mới phải khác mật khẩu hiện tại." });

        if (response.ListErrors.Count > 0)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Dữ liệu đầu vào không hợp lệ.";
        }

        return Task.FromResult(response);
    }
}
