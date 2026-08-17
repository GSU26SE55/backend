using AuthService.Application.CQRS.Command.Account;
using AuthService.Application.DTOs.Response.Account;
using AuthService.Application.Interfaces.Repositories;
using AuthService.Domain.Entities;
using AuthService.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using SharedContracts.Events;
using SharedContracts.Interfaces;

namespace AuthService.Application.CQRS.Handler.Account;

public class UpdateStaffProfileCommandHandler : IRequestHandler<UpdateStaffProfileCommand, AccountActionResponse>
{
    /// <summary>Chỉ account role Staff mới được có hồ sơ kỹ thuật viên — xem check trong Handle.</summary>
    private const string StaffRoleName = "Staff";

    private readonly IAuthUnitOfWork _unitOfWork;
    private readonly IMessageProducerService _messageProducer;

    public UpdateStaffProfileCommandHandler(IAuthUnitOfWork unitOfWork, IMessageProducerService messageProducer)
    {
        _unitOfWork = unitOfWork;
        _messageProducer = messageProducer;
    }

    public async Task<AccountActionResponse> Handle(UpdateStaffProfileCommand request, CancellationToken cancellationToken)
    {
        // #AUTH-36: ValidationBehavior pipeline đã chạy ValidateAsync TRƯỚC handler.

        var account = await _unitOfWork.Accounts
            .GetAllAsync()
            .Include(a => a.Role)
            .FirstOrDefaultAsync(a => a.Id == request.AccountId && !a.IsDeleted, cancellationToken);

        if (account is null)
            return Fail(404, "Account not found.");

        // Chỉ kiểm tra account tồn tại là chưa đủ: StaffProfile được các truy vấn phân công dùng làm
        // "danh sách kỹ thuật viên", nên tạo profile cho một Manager là đưa họ vào dropdown phân công
        // ticket và không có gì gỡ ra. Role phải là Staff mới được có hồ sơ kỹ thuật viên.
        if (account.RoleId is null || !string.Equals(account.Role?.Name, StaffRoleName, StringComparison.OrdinalIgnoreCase))
            return Fail(409, "Account is not a Staff member. Only Staff accounts can have a staff profile.");

        var employeeCode = string.IsNullOrWhiteSpace(request.EmployeeCode) ? null : request.EmployeeCode.Trim();
        if (employeeCode is not null)
        {
            var duplicated = await _unitOfWork.StaffProfiles
                .GetAllAsync()
                .AnyAsync(profile => profile.AccountId != request.AccountId && profile.EmployeeCode == employeeCode && !profile.IsDeleted, cancellationToken);

            if (duplicated)
                return Fail(409, "EmployeeCode is already in use.");
        }

        var profile = await _unitOfWork.StaffProfiles
            .GetAllAsync()
            .FirstOrDefaultAsync(sp => sp.AccountId == request.AccountId && !sp.IsDeleted, cancellationToken);

        var createdProfile = profile is null;
        if (createdProfile)
        {
            profile = new StaffProfile
            {
                Id = Guid.NewGuid(),
                AccountId = request.AccountId
            };
            await _unitOfWork.StaffProfiles.AddAsync(profile);
        }

        if (profile is null)
            throw new InvalidOperationException("StaffProfile could not be created.");

        profile.EmployeeCode = employeeCode;
        profile.Department = request.Department?.Trim();
        profile.MaxConcurrentTickets = request.MaxConcurrentTickets;
        profile.IsAvailable = request.IsAvailable;
        profile.SkillTier = (StaffSkillTierEnum)request.SkillTier;
        profile.Notes = request.Notes?.Trim();

        if (!createdProfile)
            _unitOfWork.StaffProfiles.UpdateAsync(profile);

        await _messageProducer.PublishAsync(new StaffProfileUpdatedEvent(
            profile.AccountId,
            profile.EmployeeCode,
            profile.MaxConcurrentTickets,
            profile.IsAvailable,
            (int)profile.SkillTier), cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new AccountActionResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Staff profile updated successfully.",
            Data = request.AccountId
        };
    }

    private static AccountActionResponse Fail(int statusCode, string message) => new()
    {
        IsSuccess = false,
        StatusCode = statusCode,
        Message = message,
    };
}
