using AuthService.Application.CQRS.Command.Auth;
using AuthService.Application.CQRS.Notification.Audit;
using AuthService.Application.DTOs.Response.Account;
using AuthService.Application.Interfaces.Repositories;
using AuthService.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;

namespace AuthService.Application.CQRS.Handler.Auth;

public class UnlinkGoogleCommandHandler : IRequestHandler<UnlinkGoogleCommand, AccountActionResponse>
{
    private readonly IAuthUnitOfWork _unitOfWork;
    private readonly IPublisher _publisher;   // Sprint audit #AUDIT-11

    public UnlinkGoogleCommandHandler(IAuthUnitOfWork unitOfWork, IPublisher publisher)
    {
        _unitOfWork = unitOfWork;
        _publisher = publisher;
    }

    public async Task<AccountActionResponse> Handle(UnlinkGoogleCommand request, CancellationToken cancellationToken)
    {
        var account = await _unitOfWork.Accounts
            .GetAllAsync()
            .FirstOrDefaultAsync(a => a.Id == request.AccountId && !a.IsDeleted, cancellationToken);
        if (account == null)
            return Fail(404, "Không tìm thấy tài khoản.");

        if (string.IsNullOrEmpty(account.GoogleId))
            return Fail(409, "Tài khoản chưa được liên kết với Google.");

        if (string.IsNullOrEmpty(account.PasswordHash))
            return Fail(422, "Không thể bỏ liên kết: tài khoản chưa có mật khẩu local. Vui lòng đặt mật khẩu trước.");

        account.GoogleId = null;
        account.Provider = null;
        _unitOfWork.Accounts.UpdateAsync(account);

        // #AUDIT-11
        await _publisher.Publish(new AuditTrailNotification(
            AuditActionEnum.GoogleUnlinked, account.Id, true, TargetEmail: account.Email), cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new AccountActionResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Bỏ liên kết Google thành công.",
            Data = account.Id
        };
    }

    private static AccountActionResponse Fail(int statusCode, string message) => new()
    {
        IsSuccess = false,
        StatusCode = statusCode,
        Message = message,
    };
}
