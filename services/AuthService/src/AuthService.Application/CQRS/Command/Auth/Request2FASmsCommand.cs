using MediatR;
using SharedContracts.Common.Responses;

namespace AuthService.Application.CQRS.Command.Auth;

/// <summary>
/// #AUTH-58: User submit challengeToken (từ bước 1 login) → server sinh OTP gửi SMS tới
/// account.PhoneNumber (đã verify). Backup khi user mất authenticator app.
/// </summary>
public class Request2FASmsCommand : IRequest<CommonResponse<string>>
{
    public string ChallengeToken { get; set; } = string.Empty;
}
