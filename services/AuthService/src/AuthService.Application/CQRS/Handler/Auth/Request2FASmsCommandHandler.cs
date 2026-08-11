using AuthService.Application.CQRS.Command.Auth;
using AuthService.Application.Interfaces.Helpers;
using AuthService.Application.Interfaces.Repositories;
using AuthService.Application.Interfaces.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using SharedContracts.Events;
using SharedContracts.Interfaces;

namespace AuthService.Application.CQRS.Handler.Auth;

/// <summary>
/// #AUTH-58: bước fallback sau khi user submit challengeToken — sinh OTP, lưu Redis, publish
/// SendSmsCommand → SmsService gửi tới số đã verify.
/// </summary>
public class Request2FASmsCommandHandler : IRequestHandler<Request2FASmsCommand, CommonResponse<string>>
{
    private static readonly TimeSpan OtpTtl = TimeSpan.FromMinutes(3);

    private readonly ITwoFactorChallengeStore _challengeStore;
    private readonly ITwoFactorSmsOtpStore _smsOtpStore;
    private readonly IAuthUnitOfWork _unitOfWork;
    private readonly IMessageProducerService _messageProducer;

    public Request2FASmsCommandHandler(
        ITwoFactorChallengeStore challengeStore,
        ITwoFactorSmsOtpStore smsOtpStore,
        IAuthUnitOfWork unitOfWork,
        IMessageProducerService messageProducer)
    {
        _challengeStore = challengeStore;
        _smsOtpStore = smsOtpStore;
        _unitOfWork = unitOfWork;
        _messageProducer = messageProducer;
    }

    public async Task<CommonResponse<string>> Handle(Request2FASmsCommand request, CancellationToken cancellationToken)
    {
        var challenge = await _challengeStore.GetAsync(request.ChallengeToken, cancellationToken);
        if (challenge == null)
            return Fail(422, "Authentication session is invalid or has expired.");

        var account = await _unitOfWork.Accounts.GetAllAsync()
            .FirstOrDefaultAsync(a => a.Id == challenge.AccountId && !a.IsDeleted, cancellationToken);
        if (account == null)
            return Fail(404, "Account does not exist.");

        if (!account.PhoneConfirmed || string.IsNullOrEmpty(account.PhoneNumber))
            return Fail(409, "Account has not verified a phone number — cannot receive SMS OTP fallback.");

        var otp = OtpHelper.GenerateOtp(6);
        await _smsOtpStore.SetAsync(request.ChallengeToken, otp, OtpTtl, cancellationToken);

        await _messageProducer.PublishAsync(new SendSmsCommand(
            PhoneNumber: account.PhoneNumber,
            Message: $"Your 2FA verification code: {otp}. Valid for {OtpTtl.TotalMinutes:F0} minutes.",
            SourceService: "auth",
            CorrelationId: challenge.AccountId,
            Category: "2fa_sms"), cancellationToken);

        return new CommonResponse<string>
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = $"OTP has been sent to your registered phone number. Valid for {OtpTtl.TotalMinutes:F0} minutes.",
            Data = MaskPhoneNumber(account.PhoneNumber)
        };
    }

    private static string MaskPhoneNumber(string phone)
    {
        if (phone.Length <= 4)
            return new string('*', phone.Length);
        return new string('*', phone.Length - 4) + phone[^4..];
    }

    private static CommonResponse<string> Fail(int statusCode, string message) => new()
    {
        IsSuccess = false,
        StatusCode = statusCode,
        Message = message,
    };
}
