using System.Text.Json.Serialization;
using AuthService.Application.DTOs.Response.Role;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;

namespace AuthService.Application.CQRS.Command.Role;

public class UpdateRoleCommand : IRequest<RoleActionResponse>, IValidatable<RoleActionResponse>
{
    [JsonIgnore]
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    public Task<RoleActionResponse> ValidateAsync()
    {
        var response = new RoleActionResponse();

        if (Id == Guid.Empty)
        {
            response.ListErrors.Add(new Errors { Field = "Id", Detail = "Id role không hợp lệ." });
        }

        if (string.IsNullOrWhiteSpace(Name))
        {
            response.ListErrors.Add(new Errors { Field = "Name", Detail = "Tên role không được để trống." });
        }
        else if (Name.Trim().Length > 100)
        {
            response.ListErrors.Add(new Errors { Field = "Name", Detail = "Tên role tối đa 100 ký tự." });
        }

        if (!string.IsNullOrEmpty(Description) && Description.Length > 500)
        {
            response.ListErrors.Add(new Errors { Field = "Description", Detail = "Mô tả tối đa 500 ký tự." });
        }

        if (response.ListErrors.Count > 0)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Dữ liệu đầu vào không hợp lệ.";
        }

        return Task.FromResult(response);
    }
}
