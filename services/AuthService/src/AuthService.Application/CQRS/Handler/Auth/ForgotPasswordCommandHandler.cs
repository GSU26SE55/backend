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

public class ForgotPasswordCommandHandler : IRequestHandler<ForgotPasswordCommand, CommonResponse<string>>
{
    private const int OtpLifetimeMinutes = 10;

    private readonly IAuthUnitOfWork _unitOfWork;
    private readonly IMessageProducerService _messageProducer;
    private readonly ILogger<ForgotPasswordCommandHandler> _logger;

    public ForgotPasswordCommandHandler(
        IAuthUnitOfWork unitOfWork,
        IMessageProducerService messageProducer,
        ILogger<ForgotPasswordCommandHandler> logger)
    {
        _unitOfWork = unitOfWork;
        _messageProducer = messageProducer;
        _logger = logger;
    }

    public async Task<CommonResponse<string>> Handle(ForgotPasswordCommand request, CancellationToken cancellationToken)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();

        var account = await _unitOfWork.Accounts
            .GetAllAsync()
            .FirstOrDefaultAsync(a => a.Email.ToLower() == normalizedEmail && !a.IsDeleted, cancellationToken);

        // Không tiết lộ email tồn tại hay không.
        if (account != null && account.Status == AccountStatusEnum.Active)
        {
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
            Message = "Nếu email tồn tại trong hệ thống, OTP đặt lại mật khẩu đã được gửi.",
            Data = normalizedEmail
        };
    }
}
