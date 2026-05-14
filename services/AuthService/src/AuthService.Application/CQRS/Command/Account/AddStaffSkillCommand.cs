using AuthService.Application.DTOs.Response.Account;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;
using System.Text.Json.Serialization;

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
            response.ListErrors.Add(new Errors { Field = nameof(StaffAccountId), Detail = "StaffAccountId không hợp lệ." });

        if (string.IsNullOrWhiteSpace(SkillCode))
            response.ListErrors.Add(new Errors { Field = nameof(SkillCode), Detail = "SkillCode không được để trống." });
        else if (SkillCode.Trim().Length > 64)
            response.ListErrors.Add(new Errors { Field = nameof(SkillCode), Detail = "SkillCode tối đa 64 ký tự." });

        if (SkillLevel is < 1 or > 5)
            response.ListErrors.Add(new Errors { Field = nameof(SkillLevel), Detail = "SkillLevel phải nằm trong khoảng 1 đến 5." });

        if (response.ListErrors.Count > 0)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Dữ liệu đầu vào không hợp lệ.";
        }

        return Task.FromResult(response);
    }
}
