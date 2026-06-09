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

public class ResendOtpCommandHandler : IRequestHandler<ResendOtpCommand, CommonResponse<string>>
{
    private const int OtpLifetimeMinutes = 5;
    private const int ResendCooldownSeconds = 60;

    private readonly IAuthUnitOfWork _unitOfWork;
    private readonly IMessageProducerService _messageProducer;
    private readonly ILogger<ResendOtpCommandHandler> _logger;

    public ResendOtpCommandHandler(
        IAuthUnitOfWork unitOfWork,
        IMessageProducerService messageProducer,
        ILogger<ResendOtpCommandHandler> logger)
    {
        _unitOfWork = unitOfWork;
        _messageProducer = messageProducer;
        _logger = logger;
    }

    public async Task<CommonResponse<string>> Handle(ResendOtpCommand request, CancellationToken cancellationToken)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();

        var account = await _unitOfWork.Accounts
            .GetAllAsync()
            .FirstOrDefaultAsync(a => a.Email.ToLower() == normalizedEmail && !a.IsDeleted, cancellationToken);

        if (account == null)
            return Fail(404, "Không tìm thấy tài khoản.");

        if (account.Status != AccountStatusEnum.PendingVerification)
            return Fail(409, "Tài khoản đã được xác thực hoặc không ở trạng thái chờ verify.");

        if (account.OtpExpiredAt.HasValue)
        {
            var lastSentAt = account.OtpExpiredAt.Value.AddMinutes(-OtpLifetimeMinutes);
            var elapsed = (DateTime.UtcNow - lastSentAt).TotalSeconds;
            if (elapsed < ResendCooldownSeconds)
            {
                var waitSeconds = (int)Math.Ceiling(ResendCooldownSeconds - elapsed);
                return Fail(429, $"Vui lòng đợi {waitSeconds} giây trước khi yêu cầu gửi lại OTP.");
            }
        }

        var otp = OtpHelper.GenerateOtp(6);
        account.OtpCode = otp;
        account.OtpExpiredAt = DateTime.UtcNow.AddMinutes(OtpLifetimeMinutes);
        account.OtpPurpose = OtpPurposeEnum.Register;
        _unitOfWork.Accounts.UpdateAsync(account);

        // Outbox: publish TRƯỚC SaveChanges để event atomic với Account update.
        await _messageProducer.PublishAsync(new SendOtpRegisterEvent(normalizedEmail, otp), cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new CommonResponse<string>
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Đã gửi lại OTP. Vui lòng kiểm tra email.",
            Data = normalizedEmail
        };
    }

    private static CommonResponse<string> Fail(int statusCode, string message) => new()
    {
        IsSuccess = false,
        StatusCode = statusCode,
        Message = message,
        ListErrors = { new Errors { Field = nameof(ResendOtpCommand.Email), Detail = message } }
    };
}
