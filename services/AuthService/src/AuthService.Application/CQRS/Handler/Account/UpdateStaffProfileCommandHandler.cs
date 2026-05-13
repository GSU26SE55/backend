using AuthService.Application.CQRS.Command.Account;
using AuthService.Application.DTOs.Response.Account;
using AuthService.Application.Interfaces.Repositories;
using AuthService.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;

namespace AuthService.Application.CQRS.Handler.Account;

public class UpdateStaffProfileCommandHandler : IRequestHandler<UpdateStaffProfileCommand, AccountActionResponse>
{
    private readonly IAuthUnitOfWork _unitOfWork;

    public UpdateStaffProfileCommandHandler(IAuthUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<AccountActionResponse> Handle(UpdateStaffProfileCommand request, CancellationToken cancellationToken)
    {
        var validation = await request.ValidateAsync();
        if (!validation.IsSuccess)
            return validation;

        var accountExists = await _unitOfWork.Accounts
            .GetAllAsync()
            .AnyAsync(a => a.Id == request.AccountId, cancellationToken);

        if (!accountExists)
            return Fail(404, "Không tìm thấy tài khoản.", nameof(request.AccountId));

        var employeeCode = string.IsNullOrWhiteSpace(request.EmployeeCode) ? null : request.EmployeeCode.Trim();
        if (employeeCode is not null)
        {
            var duplicated = await _unitOfWork.StaffProfiles
                .GetAllAsync()
                .AnyAsync(profile => profile.AccountId != request.AccountId && profile.EmployeeCode == employeeCode, cancellationToken);

            if (duplicated)
                return Fail(409, "EmployeeCode đã được sử dụng.", nameof(request.EmployeeCode));
        }

        var profile = await _unitOfWork.StaffProfiles
            .GetAllAsync()
            .FirstOrDefaultAsync(sp => sp.AccountId == request.AccountId, cancellationToken);

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
        profile.Notes = request.Notes?.Trim();

        if (!createdProfile)
            _unitOfWork.StaffProfiles.UpdateAsync(profile);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new AccountActionResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Cập nhật staff profile thành công.",
            Data = request.AccountId
        };
    }

    private static AccountActionResponse Fail(int statusCode, string message, string field) => new()
    {
        IsSuccess = false,
        StatusCode = statusCode,
        Message = message,
        ListErrors = { new Errors { Field = field, Detail = message } }
    };
}
