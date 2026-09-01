using AuthService.Application.CQRS.Command.Account;
using AuthService.Application.DTOs.Response.Account;
using AuthService.Application.Interfaces.Helpers;
using AuthService.Application.Interfaces.Repositories;
using AuthService.Application.Mapping;
using AuthService.Domain.Entities;
using AuthService.Domain.Enums;
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
        // #AUTH-36: ValidationBehavior pipeline đã chạy ValidateAsync TRƯỚC handler.

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
            var phone = PhoneNormalizer.Normalize(request.PhoneNumber);
            var duplicated = await _unitOfWork.Accounts
                .GetAllAsync()
                .AnyAsync(a => a.Id != request.AccountId && a.PhoneNumber == phone && !a.IsDeleted, cancellationToken);

            if (duplicated)
            {
                return new AccountResponse
                {
                    IsSuccess = false,
                    StatusCode = 409,
                    Message = "Phone number is already in use.",
                };
            }

            if (account.PhoneNumber != phone)
                account.PhoneConfirmed = false;

            account.PhoneNumber = phone;
        }
        else if (request.PhoneNumber is not null)
        {
            // Chuỗi rỗng = user chủ động xoá số. Field vắng mặt thì giữ nguyên: form profile
            // trên mobile không gửi khoá này, và xoá vô điều kiện sẽ thu hồi luôn trạng thái
            // đã xác thực SMS — user phải làm lại OTP mà không hiểu vì sao.
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

        // Chỉ ghi đè khi client thực sự gửi field. Form profile trên mobile chỉ PUT
        // fullName/phoneNumber/address — ghi đè vô điều kiện là ngày sinh và timezone user
        // đặt trên web bị xoá sạch ngay lần đầu họ sửa hồ sơ trên điện thoại.
        if (request.Address is not null)
        {
            account.Address = request.Address.Trim();
            profile.Address = request.Address.Trim();
        }

        if (request.ClearBirthDate)
        {
            account.DateOfBirth = null;
            profile.BirthDate = null;
        }
        else if (request.BirthDate.HasValue)
        {
            account.DateOfBirth = request.BirthDate;
            profile.BirthDate = request.BirthDate;
        }

        // TimeZone là hằng số của deployment (Asia/Ho_Chi_Minh), không client nào có ô sửa —
        // nên chuỗi rỗng chỉ có thể là do client gửi nhầm, không phải ý người dùng. Ghi đè
        // bằng "" sẽ xoá mất timezone dùng để tính quiet hours.
        if (!string.IsNullOrWhiteSpace(request.TimeZone))
            profile.TimeZone = request.TimeZone.Trim();

        await _messageProducer.PublishAsync(new AccountProfileUpdatedEvent(
            account.Id,
            account.Email,
            account.FullName,
            account.PhoneNumber,
            AccountProfileMapper.ResolveDisplayAvatarUrl(profile),
            Role: account.Role?.Name ?? string.Empty,
            AccountStatus: (int)account.Status,
            IsActive: account.Status.IsNotifiable()), cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new AccountResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Profile updated successfully.",
            Data = AccountProfileMapper.ToAccountDto(account)
        };
    }

    private static AccountResponse NotFound() => new()
    {
        IsSuccess = false,
        StatusCode = 404,
        Message = "Account not found."
    };
}
