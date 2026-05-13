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
        var validation = await request.ValidateAsync();
        if (!validation.IsSuccess)
            return validation;

        var profile = await _unitOfWork.StaffProfiles
            .GetAllAsync()
            .AsNoTracking()
            .Include(sp => sp.Account)
                .ThenInclude(account => account.Profile)
            .Include(sp => sp.Skills)
            .FirstOrDefaultAsync(sp => sp.AccountId == request.StaffAccountId, cancellationToken);

        if (profile is null)
        {
            return new StaffAssignmentProfileResponse
            {
                IsSuccess = false,
                StatusCode = 404,
                Message = "Không tìm thấy staff profile."
            };
        }

        return new StaffAssignmentProfileResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Lấy staff assignment profile thành công.",
            Data = AccountProfileMapper.ToStaffAssignmentProfileDto(profile)
        };
    }
}
