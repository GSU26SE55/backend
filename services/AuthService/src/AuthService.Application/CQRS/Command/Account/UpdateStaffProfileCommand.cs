using System.Text.Json.Serialization;
using AuthService.Application.DTOs.Response.Account;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;

namespace AuthService.Application.CQRS.Command.Account;

public class UpdateStaffProfileCommand : IRequest<AccountActionResponse>, IValidatable<AccountActionResponse>
{
    [JsonIgnore]
    public Guid AccountId { get; set; }

    public string? EmployeeCode { get; set; }

    public string? Department { get; set; }

    public int MaxConcurrentTickets { get; set; } = 3;

    public bool IsAvailable { get; set; } = true;

    public int SkillTier { get; set; } = 1;

    public string? Notes { get; set; }

    public Task<AccountActionResponse> ValidateAsync()
    {
        var response = new AccountActionResponse();

        if (AccountId == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = nameof(AccountId), Detail = "Invalid AccountId." });

        if (SkillTier is < 1 or > 3)
            response.ListErrors.Add(new Errors { Field = nameof(SkillTier), Detail = "Invalid SkillTier (1-3)." });

        if (!string.IsNullOrWhiteSpace(EmployeeCode) && EmployeeCode.Trim().Length > 50)
            response.ListErrors.Add(new Errors { Field = nameof(EmployeeCode), Detail = "EmployeeCode must not exceed 50 characters." });

        if (!string.IsNullOrWhiteSpace(Department) && Department.Trim().Length > 100)
            response.ListErrors.Add(new Errors { Field = nameof(Department), Detail = "Department must not exceed 100 characters." });

        if (MaxConcurrentTickets is < 1 or > 50)
            response.ListErrors.Add(new Errors { Field = nameof(MaxConcurrentTickets), Detail = "MaxConcurrentTickets must be between 1 and 50." });

        if (!string.IsNullOrWhiteSpace(Notes) && Notes.Trim().Length > 1000)
            response.ListErrors.Add(new Errors { Field = nameof(Notes), Detail = "Notes must not exceed 1000 characters." });

        if (response.ListErrors.Count > 0)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Invalid input data.";
        }

        return Task.FromResult(response);
    }
}
