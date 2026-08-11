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

namespace AuthService.Application.CQRS.Handler.Auth;

public class ResendResetOtpCommandHandler : IRequestHandler<ResendResetOtpCommand, CommonResponse<string>>
{
    private const int OtpLifetimeMinutes = 5;
    private const int ResendCooldownSeconds = 60;

    private readonly IAuthUnitOfWork _unitOfWork;
    private readonly IMessageProducerService _messageProducer;
    private readonly ILogger<ResendResetOtpCommandHandler> _logger;

    public ResendResetOtpCommandHandler(
        IAuthUnitOfWork unitOfWork,
        IMessageProducerService messageProducer,
        ILogger<ResendResetOtpCommandHandler> logger)
    {
        _unitOfWork = unitOfWork;
        _messageProducer = messageProducer;
        _logger = logger;
    }

    public async Task<CommonResponse<string>> Handle(ResendResetOtpCommand request, CancellationToken cancellationToken)
    {
        var normalizedEmail = EmailNormalizer.Normalize(request.Email);

        var account = await _unitOfWork.Accounts
            .GetAllAsync()
            .FirstOrDefaultAsync(a => a.Email.ToLower() == normalizedEmail && !a.IsDeleted, cancellationToken);

        // Không tiết lộ email tồn tại hay không.
        if (account != null && account.Status == AccountStatusEnum.Active && account.OtpPurpose == OtpPurposeEnum.PasswordReset)
        {
            if (account.OtpExpiredAt.HasValue)
            {
                var lastSentAt = account.OtpExpiredAt.Value.AddMinutes(-OtpLifetimeMinutes);
                var elapsed = (DateTime.UtcNow - lastSentAt).TotalSeconds;
                if (elapsed < ResendCooldownSeconds)
                {
                    var waitSeconds = (int)Math.Ceiling(ResendCooldownSeconds - elapsed);
                    return new CommonResponse<string>
                    {
                        IsSuccess = false,
                        StatusCode = 429,
                        Message = $"Please wait {waitSeconds} seconds before requesting a resend.",
                    };
                }
            }

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

        return new CommonResponse<string>
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "If the email exists and is in the password reset flow, the OTP has been resent.",
            Data = normalizedEmail
        };
    }
}
