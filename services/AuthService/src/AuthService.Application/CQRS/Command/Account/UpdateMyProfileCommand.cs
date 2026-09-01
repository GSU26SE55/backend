using System.Text.Json.Serialization;
using AuthService.Application.DTOs.Response.Account;
using AuthService.Application.Validation;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;

namespace AuthService.Application.CQRS.Command.Account;

public class UpdateMyProfileCommand : IRequest<AccountResponse>, IValidatable<AccountResponse>
{
    [JsonIgnore]
    public Guid AccountId { get; set; }

    public string FullName { get; set; } = string.Empty;

    public string? PhoneNumber { get; set; }

    public string? Address { get; set; }

    public DateTime? BirthDate { get; set; }

    /// <summary>
    /// Xoá ngày sinh đang lưu. Cần cờ riêng vì <see cref="BirthDate"/> null nghĩa là
    /// "client không gửi field này" (giữ nguyên giá trị cũ) — nếu không có cờ thì client
    /// nào không render ô ngày sinh sẽ vô tình xoá mất dữ liệu người dùng đặt ở nơi khác.
    /// </summary>
    public bool ClearBirthDate { get; set; }

    public string? TimeZone { get; set; }

    public Task<AccountResponse> ValidateAsync()
    {
        var response = new AccountResponse();

        if (AccountId == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = nameof(AccountId), Detail = "Invalid AccountId." });

        AccountFieldPolicy.AddFullNameErrors(response.ListErrors, FullName, nameof(FullName));
        AccountFieldPolicy.AddPhoneErrors(response.ListErrors, PhoneNumber, nameof(PhoneNumber));
        AccountFieldPolicy.AddAddressErrors(response.ListErrors, Address, nameof(Address));
        AccountFieldPolicy.AddDateOfBirthErrors(response.ListErrors, BirthDate, nameof(BirthDate));

        if (!string.IsNullOrWhiteSpace(TimeZone) && TimeZone.Trim().Length > 100)
            response.ListErrors.Add(new Errors { Field = nameof(TimeZone), Detail = "TimeZone must not exceed 100 characters." });

        if (response.ListErrors.Count > 0)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Invalid input data.";
        }

        return Task.FromResult(response);
    }
}
