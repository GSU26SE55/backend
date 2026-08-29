using AuthService.Application.DTOs.Response.Account;
using AuthService.Application.Validation;
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
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    /// <summary>Role gán cho user mới khi accept invite. Bắt buộc — mỗi account chỉ có 1 role.</summary>
    public Guid RoleId { get; set; }

    public Task<AccountActionResponse> ValidateAsync()
    {
        var response = new AccountActionResponse();

        AccountFieldPolicy.AddEmailErrors(response.ListErrors, Email);
        AccountFieldPolicy.AddFullNameErrors(response.ListErrors, FullName);
        AccountFieldPolicy.AddPhoneErrors(response.ListErrors, PhoneNumber);

        if (RoleId == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = "RoleId", Detail = "A valid role must be assigned when inviting." });

        if (response.ListErrors.Count > 0)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Invalid input data.";
        }

        return Task.FromResult(response);
    }
}
