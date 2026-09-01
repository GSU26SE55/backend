using System.Security.Cryptography;
using AuthService.Application.CQRS.Command.Auth;
using AuthService.Application.CQRS.Notification.Audit;
using AuthService.Application.Interfaces.Repositories;
using AuthService.Application.Interfaces.Services;
using AuthService.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SharedContracts.Common.Responses;
using SharedContracts.Events;
using SharedContracts.Interfaces;

namespace AuthService.Application.CQRS.Handler.Auth;

/// <summary>
/// #AUTH-51: Initiate cross-device 2FA setup.
/// Sinh secret + confirm token (32 bytes hex), lưu Redis (TTL 10p), publish email event.
/// </summary>
public class RequestCrossDevice2FAConfirmCommandHandler
    : IRequestHandler<RequestCrossDevice2FAConfirmCommand, CommonResponse<RequestCrossDevice2FAConfirmResponseDto>>
{
    private static readonly TimeSpan ConfirmTokenTtl = TimeSpan.FromMinutes(10);

    private readonly IAuthUnitOfWork _unitOfWork;
    private readonly ITotpService _totp;
    private readonly ITwoFactorCrossDeviceConfirmStore _store;
    private readonly IMessageProducerService _messageProducer;
    private readonly IPublisher _publisher;
    private readonly IConfiguration _configuration;

    public RequestCrossDevice2FAConfirmCommandHandler(
        IAuthUnitOfWork unitOfWork,
        ITotpService totp,
        ITwoFactorCrossDeviceConfirmStore store,
        IMessageProducerService messageProducer,
        IPublisher publisher,
        IConfiguration configuration)
    {
        _unitOfWork = unitOfWork;
        _totp = totp;
        _store = store;
        _messageProducer = messageProducer;
        _publisher = publisher;
        _configuration = configuration;
    }

    public async Task<CommonResponse<RequestCrossDevice2FAConfirmResponseDto>> Handle(
        RequestCrossDevice2FAConfirmCommand request, CancellationToken cancellationToken)
    {
        var account = await _unitOfWork.Accounts.GetAllAsync()
            .Where(a => !a.IsDeleted)
            .FirstOrDefaultAsync(a => a.Id == request.AccountId, cancellationToken);
        if (account == null)
            return Fail(404, "Account not found.");

        if (account.TwoFactorEnabled && !string.IsNullOrEmpty(account.TwoFactorSecret))
            return Fail(409, "2FA is already enabled. Please disable it before enrolling again.");

        // Sinh secret base32 + confirm token cryptographic random 32 bytes (256 bits).
        var secret = _totp.GenerateSecret();
        var tokenBytes = RandomNumberGenerator.GetBytes(32);
        var confirmToken = Convert.ToHexString(tokenBytes).ToLowerInvariant();

        await _store.SetAsync(confirmToken, account.Id, secret, request.RequestingSessionId, ConfirmTokenTtl, cancellationToken);

        var feBaseUrl = _configuration["Frontend:WebBaseUrl"]?.TrimEnd('/') ?? "https://app.local";
        var confirmUrl = $"{feBaseUrl}/2fa/cross-device-confirm?token={confirmToken}";

        // Outbox publish — atomic với SaveChanges (audit).
        await _messageProducer.PublishAsync(new SendTwoFactorCrossDeviceConfirmEmailEvent(
            ToEmail: account.Email,
            FullName: account.FullName,
            ConfirmUrl: confirmUrl,
            ExpiresInMinutes: (int)ConfirmTokenTtl.TotalMinutes), cancellationToken);

        await _publisher.Publish(new AuditTrailNotification(
            AuditActionEnum.TwoFactorSetupCrossDeviceRequested, account.Id, IsSuccess: true,
            TargetEmail: account.Email,
            ActorAccountIdOverride: account.Id,
            Metadata: new Dictionary<string, object?>
            {
                ["ttlMinutes"] = (int)ConfirmTokenTtl.TotalMinutes,
                ["requestingSessionId"] = request.RequestingSessionId
            }), cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // #28 QA solars.io.vn 2026-08-29: JwtSettings:Issuer là URL API (vd "https://api.solaris.io.vn")
        // dùng để validate claim `iss` của JWT — KHÔNG phải tên hiển thị cho authenticator app.
        var issuer = "Solar Battery Maintenance";
        var otpAuthUri = _totp.BuildOtpAuthUri(secret, account.Email, issuer);

        return new CommonResponse<RequestCrossDevice2FAConfirmResponseDto>
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "A confirmation link has been sent to your email. Open the email on your second device (phone) to complete setup.",
            Data = new RequestCrossDevice2FAConfirmResponseDto
            {
                ConfirmToken = confirmToken,
                ExpiresInSeconds = (int)ConfirmTokenTtl.TotalSeconds,
                OtpAuthUri = otpAuthUri,
                Secret = secret
            }
        };
    }

    private static CommonResponse<RequestCrossDevice2FAConfirmResponseDto> Fail(int statusCode, string message) => new()
    {
        IsSuccess = false,
        StatusCode = statusCode,
        Message = message
    };
}
