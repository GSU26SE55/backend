using System.Text.Json.Serialization;
using AuthService.Application.DTOs.Response.Account;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;

namespace AuthService.Application.CQRS.Command.Account;

public class AddStaffSkillCommand : IRequest<AccountActionResponse>, IValidatable<AccountActionResponse>
{
    [JsonIgnore]
    public Guid StaffAccountId { get; set; }

    public string SkillCode { get; set; } = string.Empty;

    public int SkillLevel { get; set; } = 1;

    public DateTime? CertifiedUntil { get; set; }

    public Task<AccountActionResponse> ValidateAsync()
    {
        var response = new AccountActionResponse();

        if (StaffAccountId == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = nameof(StaffAccountId), Detail = "Invalid StaffAccountId." });

        if (string.IsNullOrWhiteSpace(SkillCode))
            response.ListErrors.Add(new Errors { Field = nameof(SkillCode), Detail = "SkillCode is required." });
        else if (SkillCode.Trim().Length > 64)
            response.ListErrors.Add(new Errors { Field = nameof(SkillCode), Detail = "SkillCode must not exceed 64 characters." });

        if (SkillLevel is < 1 or > 5)
            response.ListErrors.Add(new Errors { Field = nameof(SkillLevel), Detail = "SkillLevel must be between 1 and 5." });

        if (response.ListErrors.Count > 0)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Invalid input data.";
        }

        return Task.FromResult(response);
    }
}
