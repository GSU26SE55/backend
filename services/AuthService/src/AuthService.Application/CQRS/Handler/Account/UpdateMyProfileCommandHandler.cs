using AuthService.Application.CQRS.Command.Account;
using AuthService.Application.DTOs.Response.Account;
using AuthService.Application.Interfaces.Repositories;
using AuthService.Application.Mapping;
using AuthService.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using SharedContracts.Events;
using SharedContracts.Interfaces;

namespace AuthService.Application.CQRS.Handler.Account;

public class UpdateMyProfileCommandHandler : IRequestHandler<UpdateMyProfileCommand, AccountResponse>
{
    private readonly IAuthUnitOfWork _unitOfWork;
    private readonly IMessageProducerService _messageProducer;

    public UpdateMyProfileCommandHandler(IAuthUnitOfWork unitOfWork, IMessageProducerService messageProducer)
    {
        _unitOfWork = unitOfWork;
        _messageProducer = messageProducer;
    }

    public async Task<AccountResponse> Handle(UpdateMyProfileCommand request, CancellationToken cancellationToken)
    {
        var validation = await request.ValidateAsync();
        if (!validation.IsSuccess)
            return validation;

        var account = await _unitOfWork.Accounts
            .GetAllAsync()
            .Include(a => a.Role)
            .Include(a => a.Profile)
            .Include(a => a.StaffProfile!)
                .ThenInclude(sp => sp.Skills)
            .FirstOrDefaultAsync(a => a.Id == request.AccountId && !a.IsDeleted, cancellationToken);

        if (account is null)
            return NotFound();

        if (!string.IsNullOrWhiteSpace(request.PhoneNumber))
        {
            var phone = request.PhoneNumber.Trim();
            var duplicated = await _unitOfWork.Accounts
                .GetAllAsync()
                .AnyAsync(a => a.Id != request.AccountId && a.PhoneNumber == phone && !a.IsDeleted, cancellationToken);

            if (duplicated)
            {
                return new AccountResponse
                {
                    IsSuccess = false,
                    StatusCode = 409,
                    Message = "Số điện thoại đã được sử dụng.",
                    ListErrors = { new Errors { Field = nameof(request.PhoneNumber), Detail = "Số điện thoại đã được sử dụng." } }
                };
            }

            if (account.PhoneNumber != phone)
                account.PhoneConfirmed = false;

            account.PhoneNumber = phone;
        }
        else
        {
            account.PhoneNumber = null;
            account.PhoneConfirmed = false;
        }

        var profile = account.Profile ?? new AccountProfile
        {
            Id = Guid.NewGuid(),
            AccountId = account.Id
        };

        if (account.Profile is null)
        {
            account.Profile = profile;
            await _unitOfWork.AccountProfiles.AddAsync(profile);
        }

        account.FullName = request.FullName.Trim();
        account.Address = request.Address?.Trim();
        account.DateOfBirth = request.BirthDate;

        profile.Address = request.Address?.Trim();
        profile.BirthDate = request.BirthDate;
        profile.TimeZone = request.TimeZone?.Trim();

        await _messageProducer.PublishAsync(new AccountProfileUpdatedEvent(
            account.Id,
            account.Email,
            account.FullName,
            account.PhoneNumber,
            AccountProfileMapper.ResolveDisplayAvatarUrl(profile)), cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new AccountResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Cập nhật profile thành công.",
            Data = AccountProfileMapper.ToAccountDto(account)
        };
    }

    private static AccountResponse NotFound() => new()
    {
        IsSuccess = false,
        StatusCode = 404,
        Message = "Không tìm thấy tài khoản."
    };
}
