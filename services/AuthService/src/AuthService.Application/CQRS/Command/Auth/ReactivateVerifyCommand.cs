using MediatR;
using SharedContracts.Common.Responses;

namespace AuthService.Application.CQRS.Command.Auth;

/// <summary>
/// #AUTH-50: bước 2 reactivate — submit email + OTP để restore account.
/// </summary>
public class ReactivateVerifyCommand : IRequest<CommonResponse<string>>
{
    public string Email { get; set; } = string.Empty;
    public string Otp { get; set; } = string.Empty;
}
