using AuthService.Application.CQRS.Command.Account;
using AuthService.Application.DTOs.Response.Account;
using AuthService.Application.Interfaces.Helpers;
using AuthService.Application.Interfaces.Repositories;
using AuthService.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SharedContracts.Common.Responses;
using SharedContracts.Events;
using SharedContracts.Interfaces;

namespace AuthService.Application.CQRS.Handler.Account;

public class ChangeEmailCommandHandler : IRequestHandler<ChangeEmailCommand, AccountActionResponse>
{
    private const int OtpLifetimeMinutes = 10;

    private readonly IAuthUnitOfWork _unitOfWork;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IMessageProducerService _messageProducer;
    private readonly ILogger<ChangeEmailCommandHandler> _logger;

    public ChangeEmailCommandHandler(
        IAuthUnitOfWork unitOfWork,
        IPasswordHasher passwordHasher,
        IMessageProducerService messageProducer,
        ILogger<ChangeEmailCommandHandler> logger)
    {
        _unitOfWork = unitOfWork;
        _passwordHasher = passwordHasher;
        _messageProducer = messageProducer;
        _logger = logger;
    }

    public async Task<AccountActionResponse> Handle(ChangeEmailCommand request, CancellationToken cancellationToken)
    {
        var account = await _unitOfWork.Accounts
            .GetAllAsync()
            .FirstOrDefaultAsync(a => a.Id == request.AccountId && !a.IsDeleted, cancellationToken);
        if (account == null)
            return Fail(404, "Account", "Không tìm thấy tài khoản.");

        if (!_passwordHasher.Verify(request.CurrentPassword, account.PasswordHash))
            return Fail(400, "CurrentPassword", "Mật khẩu hiện tại không chính xác.");

        var newEmail = request.NewEmail.Trim().ToLowerInvariant();

        if (account.Email.Equals(newEmail, StringComparison.OrdinalIgnoreCase))
            return Fail(400, "NewEmail", "Email mới phải khác email hiện tại.");

        var emailTaken = await _unitOfWork.Accounts
            .GetAllAsync()
            .AnyAsync(a => a.Id != request.AccountId && a.Email.ToLower() == newEmail && !a.IsDeleted, cancellationToken);

        if (emailTaken)
            return Fail(409, "NewEmail", "Email mới đã được sử dụng bởi tài khoản khác.");

        var otp = OtpHelper.GenerateOtp(6);
        account.PendingEmail = newEmail;
        account.OtpCode = otp;
        account.OtpExpiredAt = DateTime.UtcNow.AddMinutes(OtpLifetimeMinutes);
        account.OtpPurpose = OtpPurposeEnum.EmailChange;
        account.FailedLoginAttempts = 0;
        _unitOfWork.Accounts.UpdateAsync(account);

        // Outbox: publish TRƯỚC SaveChanges để event atomic với Account update.
        await _messageProducer.PublishAsync(new SendEmailChangeOtpEvent(newEmail, otp), cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new AccountActionResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "OTP đã gửi tới email mới. Vui lòng confirm để hoàn tất đổi email.",
            Data = account.Id
        };
    }

    private static AccountActionResponse Fail(int statusCode, string field, string message) => new()
    {
        IsSuccess = false,
        StatusCode = statusCode,
        Message = message,
        ListErrors = { new Errors { Field = field, Detail = message } }
    };
}
