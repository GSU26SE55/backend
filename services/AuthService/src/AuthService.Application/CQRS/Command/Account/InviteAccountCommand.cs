using System.Text.RegularExpressions;
using AuthService.Application.DTOs.Response.Account;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;

namespace AuthService.Application.CQRS.Command.Account;

/// <summary>
/// Admin tạo account ở chế độ invite. Không cần Password — user sẽ tự set khi accept invite.
/// Account ở Status=PendingVerification cho đến khi user accept invite thành công.
/// </summary>
public class InviteAccountCommand : IRequest<AccountActionResponse>, IValidatable<AccountActionResponse>
{
    private static readonly Regex EmailRegex = new(
        @"^[^\s@]+@[^\s@]+\.[^\s@]+$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public List<Guid> RoleIds { get; set; } = new();

    public Task<AccountActionResponse> ValidateAsync()
    {
        var response = new AccountActionResponse();

        if (string.IsNullOrWhiteSpace(Email))
            response.ListErrors.Add(new Errors { Field = "Email", Detail = "Email không được để trống." });
        else if (Email.Trim().Length > 256)
            response.ListErrors.Add(new Errors { Field = "Email", Detail = "Email tối đa 256 ký tự." });
        else if (!EmailRegex.IsMatch(Email.Trim()))
            response.ListErrors.Add(new Errors { Field = "Email", Detail = "Email không đúng định dạng." });

        if (string.IsNullOrWhiteSpace(FullName))
            response.ListErrors.Add(new Errors { Field = "FullName", Detail = "Họ tên không được để trống." });
        else if (FullName.Trim().Length > 150)
            response.ListErrors.Add(new Errors { Field = "FullName", Detail = "Họ tên tối đa 150 ký tự." });

        if (!string.IsNullOrWhiteSpace(PhoneNumber) && PhoneNumber.Trim().Length > 20)
            response.ListErrors.Add(new Errors { Field = "PhoneNumber", Detail = "Số điện thoại tối đa 20 ký tự." });

        if (RoleIds.Count == 0)
            response.ListErrors.Add(new Errors { Field = "RoleIds", Detail = "Phải gán ít nhất 1 role khi invite." });
        else if (RoleIds.Any(id => id == Guid.Empty))
            response.ListErrors.Add(new Errors { Field = "RoleIds", Detail = "RoleIds chứa giá trị không hợp lệ." });

        if (response.ListErrors.Count > 0)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Dữ liệu đầu vào không hợp lệ.";
        }

        return Task.FromResult(response);
    }
}
