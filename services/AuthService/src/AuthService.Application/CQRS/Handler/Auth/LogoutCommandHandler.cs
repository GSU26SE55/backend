using AuthService.Application.CQRS.Command.Auth;
using AuthService.Application.Interfaces.Repositories;
using AuthService.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;

namespace AuthService.Application.CQRS.Handler.Auth;

public class LogoutCommandHandler : IRequestHandler<LogoutCommand, CommonResponse<string>>
{
    private readonly IAuthUnitOfWork _unitOfWork;

    public LogoutCommandHandler(IAuthUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<CommonResponse<string>> Handle(LogoutCommand request, CancellationToken cancellationToken)
    {
        var existing = await _unitOfWork.RefreshTokens
            .GetAllAsync()
            .FirstOrDefaultAsync(rt => rt.Token == request.RefreshToken, cancellationToken);

        if (existing == null || existing.Status != RefreshTokenStatus.Active)
        {
            return new CommonResponse<string>
            {
                IsSuccess = true,
                StatusCode = 200,
                Message = "Refresh token đã không còn hiệu lực.",
                Data = "AlreadyInactive"
            };
        }

        if (existing.AccountId != request.AccountId)
        {
            return new CommonResponse<string>
            {
                IsSuccess = false,
                StatusCode = 403,
                Message = "Không có quyền đăng xuất session này.",
            };
        }

        existing.Status = RefreshTokenStatus.Revoked;
        existing.RevokedAt = DateTime.UtcNow;
        existing.RevokedReason = "UserLogout";
        _unitOfWork.RefreshTokens.UpdateAsync(existing);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new CommonResponse<string>
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Đăng xuất thành công.",
            Data = "Revoked"
        };
    }
}
