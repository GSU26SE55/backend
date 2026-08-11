using System.Security.Cryptography;
using System.Text;
using AuthService.Application.CQRS.Command.Auth;
using AuthService.Application.Interfaces.Helpers;
using AuthService.Application.Interfaces.Repositories;
using AuthService.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SharedContracts.Common.Responses;
using SharedContracts.Events;
using SharedContracts.Interfaces;
using SharedInfrastructure.Metrics;
using StackExchange.Redis;

namespace AuthService.Application.CQRS.Handler.Auth;

public class ForgotPasswordCommandHandler : IRequestHandler<ForgotPasswordCommand, CommonResponse<string>>
{
    private const int OtpLifetimeMinutes = 5;

    // #AUTH-13: per-email rate limit (bổ sung tầng cho IP-based PolicyAnonOtp).
    private const int MaxAttemptsPerHour = 10;
    private static readonly TimeSpan RateLimitWindow = TimeSpan.FromHours(1);

    private readonly IAuthUnitOfWork _unitOfWork;
    private readonly IMessageProducerService _messageProducer;
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<ForgotPasswordCommandHandler> _logger;

    public ForgotPasswordCommandHandler(
        IAuthUnitOfWork unitOfWork,
        IMessageProducerService messageProducer,
        IConnectionMultiplexer redis,
        ILogger<ForgotPasswordCommandHandler> logger)
    {
        _unitOfWork = unitOfWork;
        _messageProducer = messageProducer;
        _redis = redis;
        _logger = logger;
    }

    public async Task<CommonResponse<string>> Handle(ForgotPasswordCommand request, CancellationToken cancellationToken)
    {
        var normalizedEmail = EmailNormalizer.Normalize(request.Email);

        // #AUTH-13: tăng counter atomic; nếu vượt threshold → silent skip (vẫn trả message generic
        // để không leak fact rate-limited cho attacker, đồng thời không tiết lộ email exists).
        if (await IsRateLimited(normalizedEmail, cancellationToken))
        {
            _logger.LogWarning("ForgotPassword rate-limited for emailHash. Skipping OTP send.");
            return GenericResponse(normalizedEmail);
        }

        var account = await _unitOfWork.Accounts
            .GetAllAsync()
            .FirstOrDefaultAsync(a => a.Email.ToLower() == normalizedEmail && !a.IsDeleted, cancellationToken);

        // Không tiết lộ email tồn tại hay không.
        if (account != null && account.Status == AccountStatusEnum.Active)
        {
            var otp = OtpHelper.GenerateOtp(6);
            AppMetrics.AuthOtpUsageTotal.WithLabels("password_reset", "generated").Inc(); // #AUTH-78
            account.OtpCode = otp;
            account.OtpExpiredAt = DateTime.UtcNow.AddMinutes(OtpLifetimeMinutes);
            account.OtpPurpose = OtpPurposeEnum.PasswordReset;
            account.FailedLoginAttempts = 0;
            _unitOfWork.Accounts.UpdateAsync(account);

            // Outbox: publish TRƯỚC SaveChanges để event atomic với Account update.
            await _messageProducer.PublishAsync(new SendPasswordResetOtpEvent(normalizedEmail, otp), cancellationToken);

            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return GenericResponse(normalizedEmail);
    }

    private static CommonResponse<string> GenericResponse(string normalizedEmail) => new()
    {
        IsSuccess = true,
        StatusCode = 200,
        Message = "If the email exists in the system, a password reset OTP has been sent.",
        Data = normalizedEmail
    };

    private async Task<bool> IsRateLimited(string normalizedEmail, CancellationToken ct)
    {
        var key = BuildRateLimitKey(normalizedEmail);
        var db = _redis.GetDatabase();
        var current = await db.StringIncrementAsync(key);
        if (current == 1)
            await db.KeyExpireAsync(key, RateLimitWindow);
        return current > MaxAttemptsPerHour;
    }

    private static string BuildRateLimitKey(string normalizedEmail)
    {
        // Hash email để không lưu raw PII trong Redis key.
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalizedEmail));
        return $"otp_attempts:fp:{Convert.ToHexString(bytes)[..16]}";
    }
}
