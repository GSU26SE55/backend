using AuthService.Application.CQRS.Command.Auth;
using AuthService.Application.DTOs.Response.Account;
using AuthService.Application.Interfaces.Helpers;
using AuthService.Application.Interfaces.Repositories;
using AuthService.Domain.Entities;
using AuthService.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;

namespace AuthService.Application.CQRS.Handler.Auth;

public class LinkGoogleCommandHandler : IRequestHandler<LinkGoogleCommand, AccountActionResponse>
{
    private const string ProviderName = "Google";

    private readonly IAuthUnitOfWork _unitOfWork;
    private readonly IGoogleOAuthHelper _googleOAuthHelper;

    public LinkGoogleCommandHandler(IAuthUnitOfWork unitOfWork, IGoogleOAuthHelper googleOAuthHelper)
    {
        _unitOfWork = unitOfWork;
        _googleOAuthHelper = googleOAuthHelper;
    }

    public async Task<AccountActionResponse> Handle(LinkGoogleCommand request, CancellationToken cancellationToken)
    {
        var googleUser = await _googleOAuthHelper.ValidateAsync(request.IdToken, cancellationToken);
        if (googleUser == null || string.IsNullOrWhiteSpace(googleUser.Email))
            return Fail(401, "Google ID token không hợp lệ.");

        var account = await _unitOfWork.Accounts
            .GetAllAsync()
            .Include(a => a.Profile)
            .FirstOrDefaultAsync(a => a.Id == request.AccountId && !a.IsDeleted, cancellationToken);
        if (account == null)
            return Fail(404, "Không tìm thấy tài khoản.");

        if (!string.Equals(account.Email, googleUser.Email, StringComparison.OrdinalIgnoreCase))
            return Fail(422, "Email Google không khớp với email tài khoản hiện tại.");

        var googleAlreadyLinked = await _unitOfWork.Accounts
            .GetAllAsync()
            .AnyAsync(a => a.Id != request.AccountId && a.GoogleId == googleUser.Subject && !a.IsDeleted, cancellationToken);

        if (googleAlreadyLinked)
            return Fail(409, "Google account này đã được liên kết với tài khoản khác.");

        account.GoogleId = googleUser.Subject;
        account.Provider = ProviderName;

        await UpsertGoogleAvatarProfileAsync(account, googleUser.Picture);

        _unitOfWork.Accounts.UpdateAsync(account);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new AccountActionResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Liên kết Google thành công.",
            Data = account.Id
        };
    }

    private static AccountActionResponse Fail(int statusCode, string message) => new()
    {
        IsSuccess = false,
        StatusCode = statusCode,
        Message = message,
        ListErrors = { new Errors { Field = "Google", Detail = message } }
    };

    private async Task UpsertGoogleAvatarProfileAsync(Domain.Entities.Account account, string? pictureUrl)
    {
        if (string.IsNullOrWhiteSpace(pictureUrl))
            return;

        var profile = account.Profile ?? new AccountProfile
        {
            Id = Guid.NewGuid(),
            AccountId = account.Id
        };

        profile.ExternalAvatarUrl = pictureUrl.Trim();
        if (profile.AvatarFileId is null)
            profile.AvatarSource = AvatarSourceEnum.Google;

        if (account.Profile is null)
        {
            account.Profile = profile;
            await _unitOfWork.AccountProfiles.AddAsync(profile);
        }
        else
        {
            _unitOfWork.AccountProfiles.UpdateAsync(profile);
        }
    }
}
