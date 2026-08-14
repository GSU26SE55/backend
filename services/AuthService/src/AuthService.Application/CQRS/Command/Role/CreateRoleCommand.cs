using AuthService.Application.DTOs.Response.Role;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;

namespace AuthService.Application.CQRS.Command.Role;

public class CreateRoleCommand : IRequest<RoleActionResponse>, IValidatable<RoleActionResponse>
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    public Task<RoleActionResponse> ValidateAsync()
    {
        var response = new RoleActionResponse();

        if (string.IsNullOrWhiteSpace(Name))
        {
            response.ListErrors.Add(new Errors { Field = "Name", Detail = "Role name is required." });
        }
        else if (Name.Trim().Length > 100)
        {
            response.ListErrors.Add(new Errors { Field = "Name", Detail = "Role name must be at most 100 characters." });
        }

        if (!string.IsNullOrEmpty(Description) && Description.Length > 500)
        {
            response.ListErrors.Add(new Errors { Field = "Description", Detail = "Description must be at most 500 characters." });
        }

        if (response.ListErrors.Count > 0)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Invalid input data.";
        }

        return Task.FromResult(response);
    }
}
