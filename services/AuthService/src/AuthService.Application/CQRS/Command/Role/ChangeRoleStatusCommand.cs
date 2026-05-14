using AuthService.Application.DTOs.Response.Role;
using AuthService.Domain.Enums;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;
using System.Text.Json.Serialization;

namespace AuthService.Application.CQRS.Command.Role;

public class ChangeRoleStatusCommand : IRequest<RoleActionResponse>, IValidatable<RoleActionResponse>
{
    [JsonIgnore]
    public Guid Id { get; set; }
    public RoleStatusEnum Status { get; set; }

    public Task<RoleActionResponse> ValidateAsync()
    {
        var response = new RoleActionResponse();

        if (Id == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = "Id", Detail = "Id role không hợp lệ." });

        if (!Enum.IsDefined(typeof(RoleStatusEnum), Status))
            response.ListErrors.Add(new Errors { Field = "Status", Detail = "Trạng thái không hợp lệ." });

        if (response.ListErrors.Count > 0)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Dữ liệu đầu vào không hợp lệ.";
        }

        return Task.FromResult(response);
    }
}
