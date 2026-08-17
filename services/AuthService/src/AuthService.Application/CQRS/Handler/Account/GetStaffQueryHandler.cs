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
    /// <summary>
    /// Endpoint này phục vụ màn hình phân công ticket, nên chỉ trả kỹ thuật viên.
    /// Manager/Admin bị loại kể cả khi họ có <c>StaffProfile</c>.
    /// </summary>
    private const string StaffRoleName = "Staff";

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
            .Include(profile => profile.Account)
                .ThenInclude(account => account.Role)
            .Include(profile => profile.Skills)
            .Where(profile => !profile.IsDeleted && profile.Account != null && !profile.Account.IsDeleted)
            // Sự tồn tại của StaffProfile KHÔNG chứng minh account là kỹ thuật viên:
            // UpdateStaffProfileCommandHandler và AddStaffSkillCommandHandler tạo profile cho bất kỳ
            // accountId nào tồn tại, không kiểm tra role. Gán tier/skill cho một Manager một lần là
            // họ nằm lại trong danh sách phân công vĩnh viễn. Role mới là nguồn sự thật.
            // RoleId nullable: account tạo qua Google OAuth có thể chưa onboard nên chưa gán role —
            // chưa gán thì chưa phải kỹ thuật viên, loại luôn.
            .Where(profile => profile.Account.RoleId != null && profile.Account.Role.Name == StaffRoleName);

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
