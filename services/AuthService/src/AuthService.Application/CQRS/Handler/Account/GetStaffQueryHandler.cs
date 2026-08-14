using AuthService.Application.CQRS.Query.Account;
using AuthService.Application.DTOs.Response.Account;
using AuthService.Application.Interfaces.Repositories;
using AuthService.Application.Mapping;
using AuthService.Domain.Enums;
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
        if (!TryGetMinimumSkillTier(request.TicketPriority, out var minimumSkillTier))
        {
            return new StaffAssignmentProfileListResponse
            {
                IsSuccess = false,
                StatusCode = 400,
                Message = "Invalid TicketPriority. Valid values: Urgent, P1Critical, P2High, P3Normal."
            };
        }

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

        if (minimumSkillTier.HasValue)
        {
            query = query.Where(profile =>
                profile.IsAvailable &&
                profile.Account.Status == AccountStatusEnum.Active &&
                profile.SkillTier >= minimumSkillTier.Value);
        }

        var staff = await query
            .OrderByDescending(profile => profile.IsAvailable)
            .ThenBy(profile => profile.Account.FullName)
            .ToListAsync(cancellationToken);

        return new StaffAssignmentProfileListResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Staff list retrieved successfully.",
            Data = staff.Select(AccountProfileMapper.ToStaffAssignmentProfileDto).ToList()
        };
    }

    private static bool TryGetMinimumSkillTier(string? ticketPriority, out StaffSkillTierEnum? minimumSkillTier)
    {
        if (string.IsNullOrWhiteSpace(ticketPriority))
        {
            minimumSkillTier = null;
            return true;
        }

        minimumSkillTier = ticketPriority.Trim().ToUpperInvariant() switch
        {
            "URGENT" => StaffSkillTierEnum.SeniorSpecialist,
            "P1CRITICAL" => StaffSkillTierEnum.SeniorSpecialist,
            "P2HIGH" => StaffSkillTierEnum.ModuleSpecialist,
            "P3NORMAL" => StaffSkillTierEnum.Generalist,
            _ => null
        };

        return minimumSkillTier.HasValue;
    }
}
