using AuthService.Application.CQRS.Query.Account;
using AuthService.Application.DTOs.Response.Account;
using AuthService.Application.Interfaces.Repositories;
using AuthService.Application.Mapping;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace AuthService.Application.CQRS.Handler.Account;

public class GetStaffAssignmentProfileQueryHandler : IRequestHandler<GetStaffAssignmentProfileQuery, StaffAssignmentProfileResponse>
{
    private readonly IAuthUnitOfWork _unitOfWork;

    public GetStaffAssignmentProfileQueryHandler(IAuthUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<StaffAssignmentProfileResponse> Handle(GetStaffAssignmentProfileQuery request, CancellationToken cancellationToken)
    {
        // #AUTH-36: ValidationBehavior pipeline đã chạy ValidateAsync TRƯỚC handler.

        var profile = await _unitOfWork.StaffProfiles
            .GetAllAsync()
            .AsNoTracking()
            .Include(sp => sp.Account)
                .ThenInclude(account => account.Profile)
            .Include(sp => sp.Skills)
            .FirstOrDefaultAsync(sp => sp.AccountId == request.StaffAccountId && !sp.IsDeleted, cancellationToken);

        if (profile is null)
        {
            return new StaffAssignmentProfileResponse
            {
                IsSuccess = false,
                StatusCode = 404,
                Message = "Staff profile not found."
            };
        }

        return new StaffAssignmentProfileResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Staff assignment profile retrieved successfully.",
            Data = AccountProfileMapper.ToStaffAssignmentProfileDto(profile)
        };
    }
}
