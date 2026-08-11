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
        var account = await _unitOfWork.Accounts
            .GetAllAsync()
            .FirstOrDefaultAsync(a => a.Id == request.AccountId && !a.IsDeleted, cancellationToken);
        if (account == null)
            return Fail(404, "Account not found.");

        if (string.IsNullOrWhiteSpace(account.PhoneNumber))
            return Fail(422, "Account does not have a phone number yet. Please update your profile first.");

        if (account.PhoneConfirmed)
            return Fail(409, "Phone number has already been verified.");

        if (account.OtpPurpose == OtpPurposeEnum.PhoneVerify && account.OtpExpiredAt.HasValue)
        {
            var lastSentAt = account.OtpExpiredAt.Value.AddMinutes(-OtpLifetimeMinutes);
            var elapsed = (DateTime.UtcNow - lastSentAt).TotalSeconds;
            if (elapsed < ResendCooldownSeconds)
            {
                var waitSeconds = (int)Math.Ceiling(ResendCooldownSeconds - elapsed);
                return Fail(429, $"Please wait {waitSeconds} seconds before requesting another OTP.");
            }
        }

        var otp = OtpHelper.GenerateOtp(6);
        account.OtpCode = otp;
        account.OtpExpiredAt = DateTime.UtcNow.AddMinutes(OtpLifetimeMinutes);
        account.OtpPurpose = OtpPurposeEnum.PhoneVerify;
        _unitOfWork.Accounts.UpdateAsync(account);

        // Outbox: publish TRƯỚC SaveChanges để event atomic với Account update.
        // Phase 9 atomic switch (Sprint SMS): publish SendSmsCommand qua SmsService gateway thay vì
        // SendPhoneOtpEvent trực tiếp. SendPhoneOtpEvent đã được xoá khỏi flow này — backward-compat
        // consumer ở SmsService vẫn chạy thêm 1-2 sprint phòng rollback rồi xoá.
        // ⚠️ KHÔNG publish cả 2 event cùng commit — Inbox dedup theo (consumerName, messageId), 2 event
        // khác Id → tạo 2 row sms_messages → customer nhận 2 SMS OTP.
        var smsBody = $"Your OTP code is {otp}. Please do not share this code.";
        // CorrelationId fresh per OTP request — KHÔNG dùng account.Id (nhiều OTP cùng account
        // sẽ trùng correlation, break tracking khi subscriber consume SmsDeliveryReportEvent).
        await _messageProducer.PublishAsync(new SendSmsCommand(
            PhoneNumber: account.PhoneNumber,
            Message: smsBody,
            SourceService: "auth",
            CorrelationId: Guid.NewGuid(),
            Category: "otp"
        ), cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new CommonResponse<string>
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "OTP has been sent to your registered phone number.",
            Data = account.PhoneNumber
        };
    }

    private static CommonResponse<string> Fail(int statusCode, string message) => new()
    {
        IsSuccess = false,
        StatusCode = statusCode,
        Message = message,
    };
}
