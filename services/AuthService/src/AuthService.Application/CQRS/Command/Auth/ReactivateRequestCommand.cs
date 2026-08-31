using AuthService.Application.Validation;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;

namespace AuthService.Application.CQRS.Command.Auth;

/// <summary>
/// #AUTH-50: bước 1 reactivate — user submit email của soft-deleted account.
/// Server tìm account trong soft-delete window 90 ngày, gửi OTP nếu hợp lệ.
/// Trả message generic kể cả khi không tìm thấy để chống enumeration.
/// </summary>
public class ReactivateRequestCommand : IRequest<CommonResponse<string>>, IValidatable<CommonResponse<string>>
{
    public string Email { get; set; } = string.Empty;

    public Task<CommonResponse<string>> ValidateAsync()
    {
        var response = new CommonResponse<string>();

        AccountFieldPolicy.AddEmailErrors(response.ListErrors, Email);

        if (response.ListErrors.Count > 0)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Invalid input data.";
        }

        return Task.FromResult(response);
    }
}
