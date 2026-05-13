using AuthService.Application.DTOs.Response.Account;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;

namespace AuthService.Application.CQRS.Command.Account;

public class UpdateMyProfileCommand : IRequest<AccountResponse>, IValidatable<AccountResponse>
{
    public Guid AccountId { get; set; }

    public string FullName { get; set; } = string.Empty;

    public string? PhoneNumber { get; set; }

    public string? Address { get; set; }

    public DateTime? BirthDate { get; set; }

    public string? TimeZone { get; set; }

    public Task<AccountResponse> ValidateAsync()
    {
        var response = new AccountResponse();

        if (AccountId == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = nameof(AccountId), Detail = "AccountId không hợp lệ." });

        if (string.IsNullOrWhiteSpace(FullName))
            response.ListErrors.Add(new Errors { Field = nameof(FullName), Detail = "Họ tên không được để trống." });
        else if (FullName.Trim().Length > 150)
            response.ListErrors.Add(new Errors { Field = nameof(FullName), Detail = "Họ tên tối đa 150 ký tự." });

        if (!string.IsNullOrWhiteSpace(PhoneNumber) && PhoneNumber.Trim().Length > 20)
            response.ListErrors.Add(new Errors { Field = nameof(PhoneNumber), Detail = "Số điện thoại tối đa 20 ký tự." });

        if (!string.IsNullOrWhiteSpace(Address) && Address.Trim().Length > 500)
            response.ListErrors.Add(new Errors { Field = nameof(Address), Detail = "Địa chỉ tối đa 500 ký tự." });

        if (BirthDate.HasValue)
        {
            if (BirthDate.Value > DateTime.UtcNow)
                response.ListErrors.Add(new Errors { Field = nameof(BirthDate), Detail = "Ngày sinh không hợp lệ." });
            else if (BirthDate.Value.Year < 1900)
                response.ListErrors.Add(new Errors { Field = nameof(BirthDate), Detail = "Năm sinh không hợp lệ." });
        }

        if (!string.IsNullOrWhiteSpace(TimeZone) && TimeZone.Trim().Length > 100)
            response.ListErrors.Add(new Errors { Field = nameof(TimeZone), Detail = "TimeZone tối đa 100 ký tự." });

        if (response.ListErrors.Count > 0)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Dữ liệu đầu vào không hợp lệ.";
        }

        return Task.FromResult(response);
    }
}
