using AuthService.Application.CQRS.Command.Auth;
using AuthService.Application.CQRS.Notification.Audit;
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
    private readonly IPublisher _publisher;   // Sprint audit #AUDIT-11

    public LinkGoogleCommandHandler(IAuthUnitOfWork unitOfWork, IGoogleOAuthHelper googleOAuthHelper, IPublisher publisher)
    {
        _unitOfWork = unitOfWork;
        _googleOAuthHelper = googleOAuthHelper;
        _publisher = publisher;
    }

    public async Task<AccountActionResponse> Handle(LinkGoogleCommand request, CancellationToken cancellationToken)
    {
        var googleUser = await _googleOAuthHelper.ValidateAsync(request.IdToken, cancellationToken);
        if (googleUser == null || string.IsNullOrWhiteSpace(googleUser.Email))
            return Fail(401, "Invalid Google ID token.");

        var account = await _unitOfWork.Accounts
            .GetAllAsync()
            .Include(a => a.Profile)
            .FirstOrDefaultAsync(a => a.Id == request.AccountId && !a.IsDeleted, cancellationToken);
        if (account == null)
            return Fail(404, "Account not found.");

        if (!string.Equals(account.Email, googleUser.Email, StringComparison.OrdinalIgnoreCase))
            return Fail(422, "Google email does not match the current account email.");

        var googleAlreadyLinked = await _unitOfWork.Accounts
            .GetAllAsync()
            .AnyAsync(a => a.Id != request.AccountId && a.GoogleId == googleUser.Subject && !a.IsDeleted, cancellationToken);

        if (googleAlreadyLinked)
            return Fail(409, "This Google account is already linked to another account.");

        account.GoogleId = googleUser.Subject;
        account.Provider = ProviderName;

        await UpsertGoogleAvatarProfileAsync(account, googleUser.Picture);

        _unitOfWork.Accounts.UpdateAsync(account);

        // #AUDIT-11
        await _publisher.Publish(new AuditTrailNotification(
            AuditActionEnum.GoogleLinked, account.Id, true, TargetEmail: account.Email), cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new AccountActionResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Google account linked successfully.",
            Data = account.Id
        };
    }

    private static AccountActionResponse Fail(int statusCode, string message) => new()
    {
        IsSuccess = false,
        StatusCode = statusCode,
        Message = message,
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
