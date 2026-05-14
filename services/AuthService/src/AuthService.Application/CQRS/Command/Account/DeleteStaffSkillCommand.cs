using System.Text.Json.Serialization;
using AuthService.Application.DTOs.Response.Account;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;

namespace AuthService.Application.CQRS.Command.Account;

public class DeleteStaffSkillCommand : IRequest<AccountActionResponse>, IValidatable<AccountActionResponse>
{
    [JsonIgnore]
    public Guid StaffAccountId { get; set; }

    [JsonIgnore]
    public string SkillCode { get; set; } = string.Empty;

    public Task<AccountActionResponse> ValidateAsync()
    {
        var response = new AccountActionResponse();

        if (StaffAccountId == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = nameof(StaffAccountId), Detail = "StaffAccountId không hợp lệ." });

        if (string.IsNullOrWhiteSpace(SkillCode))
            response.ListErrors.Add(new Errors { Field = nameof(SkillCode), Detail = "SkillCode không được để trống." });

        if (response.ListErrors.Count > 0)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Dữ liệu đầu vào không hợp lệ.";
        }

        return Task.FromResult(response);
    }
}
