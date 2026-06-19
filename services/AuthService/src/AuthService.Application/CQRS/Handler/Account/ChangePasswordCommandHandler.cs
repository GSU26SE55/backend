using AuthService.Application.CQRS.Command.Account;
using AuthService.Application.CQRS.Notification.Audit;
using AuthService.Application.DTOs.Response.Account;
using AuthService.Application.Interfaces.Helpers;
using AuthService.Application.Interfaces.Repositories;
using AuthService.Application.Interfaces.Services;
using AuthService.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;

namespace AuthService.Application.CQRS.Handler.Account;

public class ChangePasswordCommandHandler : IRequestHandler<ChangePasswordCommand, AccountActionResponse>
{
    private readonly IAuthUnitOfWork _unitOfWork;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IPublisher _publisher;
    private readonly ITokenRevocationStore _revocationStore;
    private readonly IMediator _mediator;

    public ChangePasswordCommandHandler(
        IAuthUnitOfWork unitOfWork,
        IPasswordHasher passwordHasher,
        IPublisher publisher,
        ITokenRevocationStore revocationStore,
        IMediator mediator)
    {
        _unitOfWork = unitOfWork;
        _passwordHasher = passwordHasher;
        _publisher = publisher;
        _revocationStore = revocationStore;
        _mediator = mediator;
    }

    public async Task<AccountActionResponse> Handle(ChangePasswordCommand request, CancellationToken cancellationToken)
    {
        var account = await _unitOfWork.Accounts
            .GetAllAsync()
            .FirstOrDefaultAsync(a => a.Id == request.AccountId && !a.IsDeleted, cancellationToken);
        if (account == null)
        {
            return new AccountActionResponse
            {
                IsSuccess = false,
                StatusCode = 404,
                Message = "Không tìm thấy tài khoản."
            };
        }

        // #AUTH-23: reject nếu new = old để tránh "force revoke session" trá hình mà mật khẩu không đổi.
        // So sánh plaintext input vì PasswordHash đã được BCrypt với salt → 2 hash của cùng plaintext khác nhau.
        if (string.Equals(request.NewPassword, request.CurrentPassword, StringComparison.Ordinal))
        {
            await _publisher.Publish(new AuditTrailNotification(
                AuditActionEnum.PasswordChanged, account.Id, IsSuccess: false,
                TargetEmail: account.Email,
                Reason: "Mật khẩu mới trùng mật khẩu hiện tại."), cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return new AccountActionResponse
            {
                IsSuccess = false,
                StatusCode = 400,
                Message = "Mật khẩu mới phải khác mật khẩu hiện tại.",
            };
        }

        if (!_passwordHasher.Verify(request.CurrentPassword, account.PasswordHash))
        {
            await _publisher.Publish(new AuditTrailNotification(
                AuditActionEnum.PasswordChanged, account.Id, IsSuccess: false,
                TargetEmail: account.Email,
                Reason: "Mật khẩu hiện tại không chính xác."), cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return new AccountActionResponse
            {
                IsSuccess = false,
                StatusCode = 400,
                Message = "Mật khẩu hiện tại không chính xác.",
            };
        }

        var newHash = _passwordHasher.Hash(request.NewPassword);

        // #AUTH-34: retry trên DbUpdateConcurrencyException khi admin/handler khác đang update Account.
        // auditPublished flag tránh double-audit nếu retry (audit chỉ publish 1 lần ở attempt đầu, tracker giữ qua retry).
        var auditPublished = false;
        var revokedSessions = 0;
        var accountInvalidAfterReload = false;

        await ConcurrencyRetryHelper.ExecuteAsync<bool>(
            operation: async (attempt, ct) =>
            {
                if (attempt > 1 && (account.IsDeleted || account.Status != AccountStatusEnum.Active))
                {
                    accountInvalidAfterReload = true;
                    return false;
                }

                account.PasswordHash = newHash;
                _unitOfWork.Accounts.UpdateAsync(account);

                var activeTokens = await _unitOfWork.RefreshTokens
                    .GetAllAsync()
                    .Where(rt => rt.AccountId == account.Id && rt.Status == RefreshTokenStatus.Active)
                    .ToListAsync(ct);

                foreach (var rt in activeTokens)
                {
                    rt.Status = RefreshTokenStatus.Revoked;
                    rt.RevokedAt = DateTime.UtcNow;
                    rt.RevokedReason = "Password changed";
                    _unitOfWork.RefreshTokens.UpdateAsync(rt);
                }
                revokedSessions = activeTokens.Count;

                if (!auditPublished)
                {
                    await _publisher.Publish(new AuditTrailNotification(
                        AuditActionEnum.PasswordChanged, account.Id, IsSuccess: true,
                        TargetEmail: account.Email,
                        Metadata: new Dictionary<string, object?> { ["revokedSessions"] = revokedSessions }), ct);
                    auditPublished = true;
                }

                await _unitOfWork.SaveChangesAsync(ct);
                return true;
            },
            reload: ct => _unitOfWork.Accounts.ReloadAsync(account, ct),
            cancellationToken: cancellationToken);

        if (accountInvalidAfterReload)
        {
            return new AccountActionResponse
            {
                IsSuccess = false,
                StatusCode = 409,
                Message = "Tài khoản đã bị thay đổi bởi tiến trình khác. Vui lòng thử lại."
            };
        }

        // #AUTH-54: bulk revoke ALL access tokens đã issue trước thời điểm đổi password.
        // TTL = max access token life (1h) — sau đó tự dọn vì token đã expire tự nhiên.
        // Redis op, KHÔNG transactional với DB — chạy sau khi DB save thành công để tránh revoke nhầm nếu DB rollback.
        await _revocationStore.RevokeAllByAccountAsync(account.Id, TimeSpan.FromHours(1), cancellationToken);

        // #AUTH-48: revoke tất cả trusted device sau đổi password — security best practice.
        // Nếu attacker biết password cũ + đã trust device, đổi password phải reset trust để cắt access.
        await _mediator.Send(new RevokeAllTrustedDevicesCommand
        {
            AccountId = account.Id,
            Reason = "Password changed"
        }, cancellationToken);

        return new AccountActionResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Đổi mật khẩu thành công. Vui lòng đăng nhập lại.",
            Data = account.Id
        };
    }
}
