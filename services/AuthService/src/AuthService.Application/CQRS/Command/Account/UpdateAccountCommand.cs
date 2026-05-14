using System.Text.Json.Serialization;
using AuthService.Application.DTOs.Response.Account;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;

namespace AuthService.Application.CQRS.Command.Account;

public class UpdateAccountCommand : IRequest<AccountActionResponse>, IValidatable<AccountActionResponse>
{
    [JsonIgnore]
    public Guid Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public string? AvatarUrl { get; set; }
    public DateTime? DateOfBirth { get; set; }
    public string? Address { get; set; }

    public Task<AccountActionResponse> ValidateAsync()
    {
        var response = new AccountActionResponse();

        if (Id == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = "Id", Detail = "Id account không hợp lệ." });

        if (string.IsNullOrWhiteSpace(FullName))
            response.ListErrors.Add(new Errors { Field = "FullName", Detail = "Họ tên không được để trống." });
        else if (FullName.Trim().Length > 150)
            response.ListErrors.Add(new Errors { Field = "FullName", Detail = "Họ tên tối đa 150 ký tự." });

        if (!string.IsNullOrWhiteSpace(PhoneNumber) && PhoneNumber.Trim().Length > 20)
            response.ListErrors.Add(new Errors { Field = "PhoneNumber", Detail = "Số điện thoại tối đa 20 ký tự." });

        if (!string.IsNullOrEmpty(AvatarUrl) && AvatarUrl.Length > 500)
            response.ListErrors.Add(new Errors { Field = "AvatarUrl", Detail = "Avatar URL tối đa 500 ký tự." });

        if (DateOfBirth.HasValue && DateOfBirth.Value > DateTime.UtcNow)
            response.ListErrors.Add(new Errors { Field = "DateOfBirth", Detail = "Ngày sinh không hợp lệ." });

        if (!string.IsNullOrEmpty(Address) && Address.Length > 500)
            response.ListErrors.Add(new Errors { Field = "Address", Detail = "Địa chỉ tối đa 500 ký tự." });

        if (response.ListErrors.Count > 0)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Dữ liệu đầu vào không hợp lệ.";
        }

        return Task.FromResult(response);
    }
}
