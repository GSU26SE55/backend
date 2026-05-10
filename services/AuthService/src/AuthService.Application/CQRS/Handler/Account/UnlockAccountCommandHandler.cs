using AuthService.Application.CQRS.Command.Account;
using AuthService.Application.DTOs.Response.Account;
using AuthService.Application.Interfaces.Repositories;
using AuthService.Domain.Enums;
using MediatR;
using SharedContracts.Common.Responses;

namespace AuthService.Application.CQRS.Handler.Account;

public class UnlockAccountCommandHandler : IRequestHandler<UnlockAccountCommand, AccountActionResponse>
{
    private readonly IAuthUnitOfWork _unitOfWork;

    public UnlockAccountCommandHandler(IAuthUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<AccountActionResponse> Handle(UnlockAccountCommand request, CancellationToken cancellationToken)
    {
        var account = await _unitOfWork.Accounts.GetByIdAsync(request.Id);
        if (account == null)
        {
            return new AccountActionResponse
            {
                IsSuccess = false,
                StatusCode = 404,
                Message = "Không tìm thấy tài khoản."
            };
        }

        account.FailedLoginAttempts = 0;
        account.LockoutEndAt = null;
        if (account.Status == AccountStatusEnum.Locked)
            account.Status = AccountStatusEnum.Active;

        _unitOfWork.Accounts.UpdateAsync(account);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new AccountActionResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Đã unlock tài khoản.",
            Data = account.Id
        };
    }
}
