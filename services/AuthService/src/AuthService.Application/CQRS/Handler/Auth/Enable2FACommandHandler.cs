using AuthService.Application.CQRS.Command.Auth;
using MediatR;
using SharedContracts.Common.Responses;

namespace AuthService.Application.CQRS.Handler.Auth;

/// <summary>
/// DEPRECATED (GH-295) — endpoint <c>POST /api/accounts/me/2fa/enable</c> bị thay bằng flow 2 bước
/// <c>/2fa/init</c> → <c>/2fa/confirm</c>. Handler luôn trả HTTP 410 Gone.
/// Giữ class để tránh break consumer cũ chưa update.
/// </summary>
public class Enable2FACommandHandler : IRequestHandler<Enable2FACommand, CommonResponse<TwoFactorSecretDto>>
{
    public Task<CommonResponse<TwoFactorSecretDto>> Handle(Enable2FACommand request, CancellationToken cancellationToken)
    {
        return Task.FromResult(new CommonResponse<TwoFactorSecretDto>
        {
            IsSuccess = false,
            StatusCode = 410,
            Message = "This endpoint has been replaced. Use POST /api/accounts/me/2fa/init followed by POST /api/accounts/me/2fa/confirm.",
        });
    }
}
