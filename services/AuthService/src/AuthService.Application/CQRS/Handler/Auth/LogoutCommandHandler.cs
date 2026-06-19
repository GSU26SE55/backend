using AuthService.Application.CQRS.Command.Auth;
using AuthService.Application.Interfaces.Helpers;
using AuthService.Application.Interfaces.Repositories;
using AuthService.Application.Interfaces.Services;
using AuthService.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;

namespace AuthService.Application.CQRS.Handler.Auth;

public class LogoutCommandHandler : IRequestHandler<LogoutCommand, CommonResponse<string>>
{
    private readonly IAuthUnitOfWork _unitOfWork;
    private readonly ITwoFactorChallengeStore _challengeStore;
    private readonly ITokenRevocationStore _revocationStore;

    public LogoutCommandHandler(
        IAuthUnitOfWork unitOfWork,
        ITwoFactorChallengeStore challengeStore,
        ITokenRevocationStore revocationStore)
    {
        _unitOfWork = unitOfWork;
        _challengeStore = challengeStore;
        _revocationStore = revocationStore;
    }

    public async Task<CommonResponse<string>> Handle(LogoutCommand request, CancellationToken cancellationToken)
    {
        // #AUTH-01: DB chỉ lưu hash → so sánh bằng hash của plaintext client gửi.
        var incomingHash = string.IsNullOrEmpty(request.RefreshToken)
            ? string.Empty
            : RefreshTokenHasher.Hash(request.RefreshToken);

        var existing = await _unitOfWork.RefreshTokens
            .GetAllAsync()
            .FirstOrDefaultAsync(rt => rt.Token == incomingHash, cancellationToken);

        if (existing == null || existing.Status != RefreshTokenStatus.Active)
        {
            // #AUTH-08: vẫn invalidate 2FA challenge nếu biết accountId (no-op nếu không có).
            await _challengeStore.InvalidateByAccountAsync(request.AccountId, cancellationToken);
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

        // #AUTH-08: invalidate pending 2FA challenge TRƯỚC khi revoke refresh token.
        await _challengeStore.InvalidateByAccountAsync(existing.AccountId, cancellationToken);

        existing.Status = RefreshTokenStatus.Revoked;
        existing.RevokedAt = DateTime.UtcNow;
        existing.RevokedReason = "UserLogout";
        _unitOfWork.RefreshTokens.UpdateAsync(existing);

        // #AUTH-54: bulk revoke access token đã cấp trước thời điểm logout (cutoff = now).
        // TTL = max access token life (1h) — sau đó tự dọn vì token đã expire tự nhiên.
        await _revocationStore.RevokeAllByAccountAsync(existing.AccountId, TimeSpan.FromHours(1), cancellationToken);

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
