using System.Security.Cryptography;
using System.Text;
using AuthService.Application.CQRS.Command.Account;
using AuthService.Application.CQRS.Notification.Audit;
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
using StackExchange.Redis;

namespace AuthService.Application.CQRS.Handler.Account;

public class ChangeEmailCommandHandler : IRequestHandler<ChangeEmailCommand, AccountActionResponse>
{
    private const int OtpLifetimeMinutes = 5;

    // #AUTH-24: reserve email mới trong Redis suốt giai đoạn chờ confirm OTP (5 phút). Chống race
    // user A đang chờ verify đổi sang X, user B register/change-email X cùng lúc.
    private const string EmailReserveKeyPrefix = "email_reserve:";
    private static readonly TimeSpan EmailReserveTtl = TimeSpan.FromMinutes(5);

    private readonly IAuthUnitOfWork _unitOfWork;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IMessageProducerService _messageProducer;
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<ChangeEmailCommandHandler> _logger;
    private readonly IPublisher _publisher;   // Sprint audit #AUDIT-11

    public ChangeEmailCommandHandler(
        IAuthUnitOfWork unitOfWork,
        IPasswordHasher passwordHasher,
        IMessageProducerService messageProducer,
        IConnectionMultiplexer redis,
        ILogger<ChangeEmailCommandHandler> logger,
        IPublisher publisher)
    {
        _unitOfWork = unitOfWork;
        _passwordHasher = passwordHasher;
        _messageProducer = messageProducer;
        _redis = redis;
        _logger = logger;
        _publisher = publisher;
    }

    public async Task<AccountActionResponse> Handle(ChangeEmailCommand request, CancellationToken cancellationToken)
    {
        var account = await _unitOfWork.Accounts
            .GetAllAsync()
            .FirstOrDefaultAsync(a => a.Id == request.AccountId && !a.IsDeleted, cancellationToken);
        if (account == null)
            return Fail(404, "Account not found.");

        // #38 QA solars.io.vn 2026-08-29: 401 ở đây từng bị axios.ts coi là hết phiên (mọi 401
        // != TOKEN_EXPIRED ⇒ auto-logout) ⇒ gõ sai mật khẩu hiện tại là bị đăng xuất luôn.
        // ChangePasswordCommandHandler dùng 400 cho đúng tình huống này — đồng bộ theo đó.
        if (!_passwordHasher.Verify(request.CurrentPassword, account.PasswordHash))
            return Fail(400, "Current password is incorrect.");

        var newEmail = EmailNormalizer.Normalize(request.NewEmail);

        if (account.Email.Equals(newEmail, StringComparison.OrdinalIgnoreCase))
            return Fail(422, "New email must be different from the current email.");

        var emailTaken = await _unitOfWork.Accounts
            .GetAllAsync()
            .AnyAsync(a => a.Id != request.AccountId && a.Email.ToLower() == newEmail && !a.IsDeleted, cancellationToken);

        if (emailTaken)
            return Fail(409, "New email is already used by another account.");

        // #AUTH-24: SET NX reserve email mới với owner = current accountId.
        // Nếu key đã tồn tại với owner khác → user khác đang trong flow đổi sang email này → reject.
        // Nếu owner = mình → re-issue (user request OTP mới cùng email) — TTL refresh.
        var reserveKey = BuildReserveKey(newEmail);
        var reserveOwner = request.AccountId.ToString("N");
        var db = _redis.GetDatabase();
        var acquired = await db.StringSetAsync(reserveKey, reserveOwner, EmailReserveTtl, When.NotExists);
        if (!acquired)
        {
            var existingOwner = await db.StringGetAsync(reserveKey);
            if (existingOwner != reserveOwner)
                return Fail(409, "New email is currently being processed by another account. Please choose a different email or try again later.");
            // Cùng owner → refresh TTL.
            await db.KeyExpireAsync(reserveKey, EmailReserveTtl);
        }

        var otp = OtpHelper.GenerateOtp(6);
        account.PendingEmail = newEmail;
        account.OtpCode = otp;
        account.OtpExpiredAt = DateTime.UtcNow.AddMinutes(OtpLifetimeMinutes);
        account.OtpPurpose = OtpPurposeEnum.EmailChange;
        account.FailedLoginAttempts = 0;
        _unitOfWork.Accounts.UpdateAsync(account);

        // Outbox: publish TRƯỚC SaveChanges để event atomic với Account update.
        await _messageProducer.PublishAsync(new SendEmailChangeOtpEvent(newEmail, otp), cancellationToken);

        // #AUDIT-11
        await _publisher.Publish(new AuditTrailNotification(
            AuditActionEnum.EmailChangeRequested, account.Id, true, TargetEmail: account.Email), cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new AccountActionResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "OTP has been sent to the new email. Please confirm to complete the email change.",
            Data = account.Id
        };
    }

    private static AccountActionResponse Fail(int statusCode, string message) => new()
    {
        IsSuccess = false,
        StatusCode = statusCode,
        Message = message,
    };

    /// <summary>#AUTH-24: key = "email_reserve:" + SHA256(normalizedEmail)[..16] để không lưu raw PII trong Redis.</summary>
    private static string BuildReserveKey(string normalizedEmail)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalizedEmail));
        return EmailReserveKeyPrefix + Convert.ToHexString(bytes)[..16];
    }
}
