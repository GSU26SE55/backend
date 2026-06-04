using AuthService.Application.CQRS.Command.Account;
using AuthService.Application.CQRS.Notification.Audit;
using AuthService.Application.DTOs.Response.Account;
using AuthService.Application.Interfaces.Helpers;
using AuthService.Application.Interfaces.Repositories;
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

    public ChangePasswordCommandHandler(
        IAuthUnitOfWork unitOfWork,
        IPasswordHasher passwordHasher,
        IPublisher publisher)
    {
        _unitOfWork = unitOfWork;
        _passwordHasher = passwordHasher;
        _publisher = publisher;
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
                StatusCode = 401,
                Message = "Mật khẩu hiện tại không chính xác.",
                ListErrors = { new Errors { Field = "CurrentPassword", Detail = "Mật khẩu hiện tại không chính xác." } }
            };
        }

        account.PasswordHash = _passwordHasher.Hash(request.NewPassword);
        _unitOfWork.Accounts.UpdateAsync(account);

        var activeTokens = await _unitOfWork.RefreshTokens
            .GetAllAsync()
            .Where(rt => rt.AccountId == account.Id && rt.Status == RefreshTokenStatus.Active)
            .ToListAsync(cancellationToken);

        foreach (var rt in activeTokens)
        {
            rt.Status = RefreshTokenStatus.Revoked;
            rt.RevokedAt = DateTime.UtcNow;
            rt.RevokedReason = "Password changed";
            _unitOfWork.RefreshTokens.UpdateAsync(rt);
        }

        await _publisher.Publish(new AuditTrailNotification(
            AuditActionEnum.PasswordChanged, account.Id, IsSuccess: true,
            TargetEmail: account.Email,
            Metadata: new Dictionary<string, object?> { ["revokedSessions"] = activeTokens.Count }), cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new AccountActionResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Đổi mật khẩu thành công. Vui lòng đăng nhập lại.",
            Data = account.Id
        };
    }
}
