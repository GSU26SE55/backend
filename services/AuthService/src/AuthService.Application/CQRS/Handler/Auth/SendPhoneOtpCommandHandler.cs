using AuthService.Application.CQRS.Command.Auth;
using AuthService.Application.Interfaces.Helpers;
using AuthService.Application.Interfaces.Repositories;
using AuthService.Domain.Enums;
using MediatR;
using Microsoft.Extensions.Logging;
using SharedContracts.Common.Responses;
using SharedContracts.Events;
using SharedContracts.Interfaces;

namespace AuthService.Application.CQRS.Handler.Auth;

public class SendPhoneOtpCommandHandler : IRequestHandler<SendPhoneOtpCommand, CommonResponse<string>>
{
    private const int OtpLifetimeMinutes = 5;
    private const int ResendCooldownSeconds = 60;

    private readonly IAuthUnitOfWork _unitOfWork;
    private readonly IMessageProducerService _messageProducer;
    private readonly ILogger<SendPhoneOtpCommandHandler> _logger;

    public SendPhoneOtpCommandHandler(
        IAuthUnitOfWork unitOfWork,
        IMessageProducerService messageProducer,
        ILogger<SendPhoneOtpCommandHandler> logger)
    {
        _unitOfWork = unitOfWork;
        _messageProducer = messageProducer;
        _logger = logger;
    }

    public async Task<CommonResponse<string>> Handle(SendPhoneOtpCommand request, CancellationToken cancellationToken)
    {
        var account = await _unitOfWork.Accounts.GetByIdAsync(request.AccountId);
        if (account == null)
            return Fail(404, "Không tìm thấy tài khoản.");

        if (string.IsNullOrWhiteSpace(account.PhoneNumber))
            return Fail(400, "Tài khoản chưa có số điện thoại. Vui lòng cập nhật profile trước.");

        if (account.PhoneConfirmed)
            return Fail(400, "Số điện thoại đã được xác thực.");

        if (account.OtpPurpose == OtpPurposeEnum.PhoneVerify && account.OtpExpiredAt.HasValue)
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
        account.OtpPurpose = OtpPurposeEnum.PhoneVerify;
        _unitOfWork.Accounts.UpdateAsync(account);

        // Outbox: publish TRƯỚC SaveChanges để event atomic với Account update.
        await _messageProducer.PublishAsync(new SendPhoneOtpEvent(account.PhoneNumber, otp), cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new CommonResponse<string>
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Đã gửi OTP tới số điện thoại đã đăng ký.",
            Data = account.PhoneNumber
        };
    }

    private static CommonResponse<string> Fail(int statusCode, string message) => new()
    {
        IsSuccess = false,
        StatusCode = statusCode,
        Message = message,
        ListErrors = { new Errors { Field = "Phone", Detail = message } }
    };
}
