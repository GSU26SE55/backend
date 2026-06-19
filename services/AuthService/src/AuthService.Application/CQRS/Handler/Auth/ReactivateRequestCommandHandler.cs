using AuthService.Application.CQRS.Command.Auth;
using AuthService.Application.Interfaces.Helpers;
using AuthService.Application.Interfaces.Repositories;
using AuthService.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using SharedContracts.Events;
using SharedContracts.Interfaces;

namespace AuthService.Application.CQRS.Handler.Auth;

/// <summary>
/// #AUTH-50: tìm soft-deleted account trong window 90 ngày + gửi OTP qua email cũ.
/// </summary>
public class ReactivateRequestCommandHandler : IRequestHandler<ReactivateRequestCommand, CommonResponse<string>>
{
    private const int OtpLifetimeMinutes = 5;
    private static readonly TimeSpan ReactivationWindow = TimeSpan.FromDays(90);

    private readonly IAuthUnitOfWork _unitOfWork;
    private readonly IMessageProducerService _messageProducer;

    public ReactivateRequestCommandHandler(IAuthUnitOfWork unitOfWork, IMessageProducerService messageProducer)
    {
        _unitOfWork = unitOfWork;
        _messageProducer = messageProducer;
    }

    public async Task<CommonResponse<string>> Handle(ReactivateRequestCommand request, CancellationToken cancellationToken)
    {
        var normalizedEmail = EmailNormalizer.Normalize(request.Email);
        var windowCutoff = DateTime.UtcNow - ReactivationWindow;

        // IgnoreQueryFilters() để bypass !IsDeleted global filter và tìm soft-deleted account.
        var account = await _unitOfWork.Accounts
            .GetAllAsync()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(a =>
                a.Email.ToLower() == normalizedEmail
                && a.IsDeleted
                && a.DeletedAt.HasValue
                && a.DeletedAt.Value >= windowCutoff,
                cancellationToken);

        if (account != null)
        {
            // OTP để verify chính chủ. PendingReactivation purpose phân biệt với các flow khác.
            account.OtpCode = OtpHelper.GenerateOtp(6);
            account.OtpExpiredAt = DateTime.UtcNow.AddMinutes(OtpLifetimeMinutes);
            account.OtpPurpose = OtpPurposeEnum.PasswordReset; // tái dùng enum sẵn — semantics: "verify by email"
            _unitOfWork.Accounts.UpdateAsync(account);

            await _messageProducer.PublishAsync(new SendPasswordResetOtpEvent(normalizedEmail, account.OtpCode), cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        // Generic response chống enumeration — luôn 200 OK kể cả khi không tìm thấy.
        return new CommonResponse<string>
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Nếu tài khoản trong window restore 90 ngày, OTP đã được gửi tới email.",
            Data = normalizedEmail
        };
    }
}
