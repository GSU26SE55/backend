using AuthService.Application.CQRS.Command.Auth;
using AuthService.Application.DTOs.Response.Auth;
using AuthService.Application.Interfaces.Repositories;
using AuthService.Application.Interfaces.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SharedContracts.Common.Responses;

namespace AuthService.Application.CQRS.Handler.Auth;

public class Init2FACommandHandler : IRequestHandler<Init2FACommand, CommonResponse<TwoFactorSetupDto>>
{
    private static readonly TimeSpan PendingTtl = TimeSpan.FromMinutes(10);

    private readonly IAuthUnitOfWork _unitOfWork;
    private readonly ITotpService _totp;
    private readonly ITwoFactorPendingStore _pending;
    private readonly IConfiguration _configuration;

    public Init2FACommandHandler(
        IAuthUnitOfWork unitOfWork,
        ITotpService totp,
        ITwoFactorPendingStore pending,
        IConfiguration configuration)
    {
        _unitOfWork = unitOfWork;
        _totp = totp;
        _pending = pending;
        _configuration = configuration;
    }

    public async Task<CommonResponse<TwoFactorSetupDto>> Handle(Init2FACommand request, CancellationToken cancellationToken)
    {
        var account = await _unitOfWork.Accounts.GetAllAsync()
            .Where(a => !a.IsDeleted)
            .FirstOrDefaultAsync(a => a.Id == request.AccountId, cancellationToken);

        if (account == null)
            return Fail(404, "Không tìm thấy tài khoản.");

        if (account.TwoFactorEnabled && !string.IsNullOrEmpty(account.TwoFactorSecret))
            return Fail(409, "2FA đã được bật. Hãy disable trước khi enroll lại.");

        var secret = _totp.GenerateSecret();
        var pendingToken = Guid.NewGuid().ToString("N");
        await _pending.SetAsync(account.Id, secret, pendingToken, PendingTtl, cancellationToken);

        var issuer = _configuration["JwtSettings:Issuer"] ?? "GSU26SE55 Auth";
        var label = account.Email;
        var otpAuthUri = _totp.BuildOtpAuthUri(secret, label, issuer);

        return new CommonResponse<TwoFactorSetupDto>
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Đã sinh secret. Quét QR bằng Authenticator rồi gọi /2fa/confirm với mã 6 số để kích hoạt.",
            Data = new TwoFactorSetupDto
            {
                Secret = secret,
                OtpAuthUri = otpAuthUri,
                PendingToken = pendingToken
            }
        };
    }

    private static CommonResponse<TwoFactorSetupDto> Fail(int statusCode, string message) => new()
    {
        IsSuccess = false,
        StatusCode = statusCode,
        Message = message,
    };
}
