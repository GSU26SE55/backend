using System.Text.Json.Serialization;
using AuthService.Application.DTOs.Response.Account;
using MediatR;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;

namespace AuthService.Application.CQRS.Query.Account;

public class GetStaffAssignmentProfileQuery : IRequest<StaffAssignmentProfileResponse>, IValidatable<StaffAssignmentProfileResponse>
{
    [JsonIgnore]
    [BindNever]
    public Guid StaffAccountId { get; set; }

    public Task<StaffAssignmentProfileResponse> ValidateAsync()
    {
        var response = new StaffAssignmentProfileResponse();

        if (StaffAccountId == Guid.Empty)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Invalid StaffAccountId.";
            response.ListErrors.Add(new Errors { Field = nameof(StaffAccountId), Detail = "Invalid StaffAccountId." });
        }

        return Task.FromResult(response);
    }
}
