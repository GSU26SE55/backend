using System.Text.Json.Serialization;
using AuthService.Application.DTOs.Response.Account;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;

namespace AuthService.Application.CQRS.Command.Account;

public class AssignRolesCommand : IRequest<AccountActionResponse>, IValidatable<AccountActionResponse>
{
    [JsonIgnore]
    public Guid AccountId { get; set; }
    public List<Guid> RoleIds { get; set; } = new();
    public DateTime? ExpiredAt { get; set; }

    public Task<AccountActionResponse> ValidateAsync()
    {
        var response = new AccountActionResponse();

        if (AccountId == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = "AccountId", Detail = "Account không hợp lệ." });

        if (RoleIds == null || RoleIds.Count == 0)
            response.ListErrors.Add(new Errors { Field = "RoleIds", Detail = "Phải chọn ít nhất 1 role." });
        else if (RoleIds.Any(id => id == Guid.Empty))
            response.ListErrors.Add(new Errors { Field = "RoleIds", Detail = "RoleIds chứa giá trị không hợp lệ." });

        if (ExpiredAt.HasValue && ExpiredAt.Value <= DateTime.UtcNow)
            response.ListErrors.Add(new Errors { Field = "ExpiredAt", Detail = "Thời gian hết hạn phải ở tương lai." });

        if (response.ListErrors.Count > 0)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Dữ liệu đầu vào không hợp lệ.";
        }

        return Task.FromResult(response);
    }
}
