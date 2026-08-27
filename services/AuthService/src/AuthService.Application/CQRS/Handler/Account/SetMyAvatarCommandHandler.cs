using AuthService.Application.CQRS.Command.Account;
using AuthService.Application.DTOs.Response.Account;
using AuthService.Application.Interfaces.Repositories;
using AuthService.Application.Mapping;
using AuthService.Domain.Entities;
using AuthService.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Events;
using SharedContracts.Interfaces;

namespace AuthService.Application.CQRS.Handler.Account;

public class SetMyAvatarCommandHandler : IRequestHandler<SetMyAvatarCommand, AccountResponse>
{
    private readonly IAuthUnitOfWork _unitOfWork;
    private readonly IMessageProducerService _messageProducer;

    public SetMyAvatarCommandHandler(IAuthUnitOfWork unitOfWork, IMessageProducerService messageProducer)
    {
        _unitOfWork = unitOfWork;
        _messageProducer = messageProducer;
    }

    public async Task<AccountResponse> Handle(SetMyAvatarCommand request, CancellationToken cancellationToken)
    {
        // #AUTH-36: ValidationBehavior pipeline đã chạy ValidateAsync TRƯỚC handler.

        var account = await _unitOfWork.Accounts
            .GetAllAsync()
            .Include(a => a.Role)
            .Include(a => a.Profile)
            .Include(a => a.StaffProfile!)
                .ThenInclude(sp => sp.Skills)
            .FirstOrDefaultAsync(a => a.Id == request.AccountId && !a.IsDeleted, cancellationToken);

        if (account is null)
            return new AccountResponse { IsSuccess = false, StatusCode = 404, Message = "Account not found." };

        var createdProfile = account.Profile is null;
        var profile = account.Profile ?? new AccountProfile
        {
            Id = Guid.NewGuid(),
            AccountId = account.Id
        };

        if (createdProfile)
        {
            account.Profile = profile;
            await _unitOfWork.AccountProfiles.AddAsync(profile);
        }

        profile.AvatarFileId = request.AvatarFileId;
        profile.AvatarSource = AvatarSourceEnum.Uploaded;

        if (!createdProfile)
            _unitOfWork.AccountProfiles.UpdateAsync(profile);

        await _messageProducer.PublishAsync(new AccountProfileUpdatedEvent(
            account.Id,
            account.Email,
            account.FullName,
            account.PhoneNumber,
            AccountProfileMapper.ResolveDisplayAvatarUrl(profile),
            Role: account.Role?.Name ?? string.Empty,
            AccountStatus: (int)account.Status,
            IsActive: account.Status.IsNotifiable() && !account.IsDeleted), cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new AccountResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Avatar updated successfully.",
            Data = AccountProfileMapper.ToAccountDto(account)
        };
    }
}
