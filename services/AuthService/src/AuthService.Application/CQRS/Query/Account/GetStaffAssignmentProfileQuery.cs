using AuthService.Application.DTOs.Response.Account;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;

namespace AuthService.Application.CQRS.Query.Account;

public class GetStaffAssignmentProfileQuery : IRequest<StaffAssignmentProfileResponse>, IValidatable<StaffAssignmentProfileResponse>
{
    public Guid StaffAccountId { get; set; }

    public Task<StaffAssignmentProfileResponse> ValidateAsync()
    {
        var response = new StaffAssignmentProfileResponse();

        if (StaffAccountId == Guid.Empty)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "StaffAccountId không hợp lệ.";
            response.ListErrors.Add(new Errors { Field = nameof(StaffAccountId), Detail = "StaffAccountId không hợp lệ." });
        }

        return Task.FromResult(response);
    }
}
