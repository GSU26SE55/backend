using AuthService.Application.CQRS.Command.Auth;
using AuthService.Application.Interfaces.Repositories;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;

namespace AuthService.Application.CQRS.Handler.Auth;

public class Disable2FACommandHandler : IRequestHandler<Disable2FACommand, CommonResponse<string>>
{
    private readonly IAuthUnitOfWork _unitOfWork;

    public Disable2FACommandHandler(IAuthUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<CommonResponse<string>> Handle(Disable2FACommand request, CancellationToken cancellationToken)
    {
        var account = await _unitOfWork.Accounts
            .GetAllAsync()
            .FirstOrDefaultAsync(a => a.Id == request.AccountId && !a.IsDeleted, cancellationToken);
        if (account == null)
            return Fail(404, "Không tìm thấy tài khoản.");

        if (!account.TwoFactorEnabled && string.IsNullOrEmpty(account.TwoFactorSecret))
        {
            return new CommonResponse<string>
            {
                IsSuccess = true,
                StatusCode = 200,
                Message = "2FA vốn đã chưa bật.",
                Data = account.Id.ToString()
            };
        }

        account.TwoFactorEnabled = false;
        account.TwoFactorSecret = null;
        _unitOfWork.Accounts.UpdateAsync(account);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new CommonResponse<string>
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Tắt 2FA thành công.",
            Data = account.Id.ToString()
        };
    }

    private static CommonResponse<string> Fail(int statusCode, string message) => new()
    {
        IsSuccess = false,
        StatusCode = statusCode,
        Message = message,
        ListErrors = { new Errors { Field = "AccountId", Detail = message } }
    };
}
