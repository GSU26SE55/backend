using AuthService.Application.CQRS.Command.Account;
using AuthService.Application.DTOs.Response.Account;
using AuthService.Application.Interfaces.Repositories;
using AuthService.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;

namespace AuthService.Application.CQRS.Handler.Account;

public class AddStaffSkillCommandHandler : IRequestHandler<AddStaffSkillCommand, AccountActionResponse>
{
    private readonly IAuthUnitOfWork _unitOfWork;

    public AddStaffSkillCommandHandler(IAuthUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<AccountActionResponse> Handle(AddStaffSkillCommand request, CancellationToken cancellationToken)
    {
        var validation = await request.ValidateAsync();
        if (!validation.IsSuccess)
            return validation;

        var accountExists = await _unitOfWork.Accounts
            .GetAllAsync()
            .AnyAsync(a => a.Id == request.StaffAccountId && !a.IsDeleted, cancellationToken);

        if (!accountExists)
            return Fail(404, "Không tìm thấy tài khoản.");

        var staffProfile = await _unitOfWork.StaffProfiles
            .GetAllAsync()
            .FirstOrDefaultAsync(profile => profile.AccountId == request.StaffAccountId && !profile.IsDeleted, cancellationToken);

        if (staffProfile is null)
        {
            staffProfile = new StaffProfile
            {
                Id = Guid.NewGuid(),
                AccountId = request.StaffAccountId
            };
            await _unitOfWork.StaffProfiles.AddAsync(staffProfile);
        }

        var skillCode = request.SkillCode.Trim();
        var skill = await _unitOfWork.StaffSkills
            .GetAllAsync()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.StaffAccountId == request.StaffAccountId && s.SkillCode == skillCode, cancellationToken);

        var createdSkill = skill is null;
        if (createdSkill)
        {
            skill = new StaffSkill
            {
                Id = Guid.NewGuid(),
                StaffAccountId = request.StaffAccountId,
                SkillCode = skillCode
            };
            await _unitOfWork.StaffSkills.AddAsync(skill);
        }
        else if (skill is not null)
        {
            skill.IsDeleted = false;
            skill.DeletedAt = null;
        }

        if (skill is null)
            throw new InvalidOperationException("StaffSkill could not be created.");

        skill.SkillLevel = request.SkillLevel;
        skill.CertifiedUntil = request.CertifiedUntil;

        if (!createdSkill)
            _unitOfWork.StaffSkills.UpdateAsync(skill);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new AccountActionResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Cập nhật staff skill thành công.",
            Data = request.StaffAccountId
        };
    }

    private static AccountActionResponse Fail(int statusCode, string message) => new()
    {
        IsSuccess = false,
        StatusCode = statusCode,
        Message = message,
    };
}
