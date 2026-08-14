using System.Text.Json.Serialization;
using AuthService.Application.DTOs.Response.Role;
using AuthService.Domain.Enums;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;

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
            response.ListErrors.Add(new Errors { Field = "Id", Detail = "Invalid role Id." });

        if (!Enum.IsDefined(typeof(RoleStatusEnum), Status))
            response.ListErrors.Add(new Errors { Field = "Status", Detail = "Invalid status." });

        if (response.ListErrors.Count > 0)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Invalid input data.";
        }

        return Task.FromResult(response);
    }
}
