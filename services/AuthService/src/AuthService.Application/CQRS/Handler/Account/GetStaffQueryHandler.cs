using AuthService.Application.CQRS.Query.Account;
using AuthService.Application.DTOs.Response.Account;
using AuthService.Application.Interfaces.Repositories;
using AuthService.Application.Mapping;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace AuthService.Application.CQRS.Handler.Account;

public class GetStaffQueryHandler : IRequestHandler<GetStaffQuery, StaffAssignmentProfileListResponse>
{
    private readonly IAuthUnitOfWork _unitOfWork;

    public GetStaffQueryHandler(IAuthUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<StaffAssignmentProfileListResponse> Handle(GetStaffQuery request, CancellationToken cancellationToken)
    {
        var query = _unitOfWork.StaffProfiles
            .GetAllAsync()
            .AsNoTracking()
            .Include(profile => profile.Account)
                .ThenInclude(account => account.Profile)
            .Include(profile => profile.Skills)
            .Where(profile => !profile.IsDeleted && profile.Account != null && !profile.Account.IsDeleted);

        if (!string.IsNullOrWhiteSpace(request.Skill))
        {
            var skill = request.Skill.Trim();
            query = query.Where(profile => profile.Skills.Any(s => s.SkillCode == skill));
        }

        var staff = await query
            .OrderByDescending(profile => profile.IsAvailable)
            .ThenBy(profile => profile.Account.FullName)
            .ToListAsync(cancellationToken);

        return new StaffAssignmentProfileListResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Lấy danh sách staff thành công.",
            Data = staff.Select(AccountProfileMapper.ToStaffAssignmentProfileDto).ToList()
        };
    }
}
