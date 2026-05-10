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

namespace AuthService.Application.CQRS.Handler.Auth;

public class ResendResetOtpCommandHandler : IRequestHandler<ResendResetOtpCommand, CommonResponse<string>>
{
    private const int OtpLifetimeMinutes = 10;
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
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();

        var account = await _unitOfWork.Accounts
            .GetAllAsync()
            .FirstOrDefaultAsync(a => a.Email.ToLower() == normalizedEmail, cancellationToken);

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
                        Message = $"Vui lòng đợi {waitSeconds} giây trước khi yêu cầu gửi lại.",
                        ListErrors = { new Errors { Field = "Auth", Detail = "Resend too fast." } }
                    };
                }
            }

            var otp = OtpHelper.GenerateOtp(6);
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
            Message = "Nếu email tồn tại và đang trong luồng đặt lại mật khẩu, OTP đã được gửi lại.",
            Data = normalizedEmail
        };
    }
}
