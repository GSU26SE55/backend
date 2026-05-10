using AuthService.Application.CQRS.Query.Role;
using AuthService.Application.DTOs.Response.Role;
using AuthService.Application.Interfaces.Repositories;
using MediatR;

namespace AuthService.Application.CQRS.Handler.Role;

public class GetRoleByIdQueryHandler : IRequestHandler<GetRoleByIdQuery, RoleResponse>
{
    private readonly IAuthUnitOfWork _unitOfWork;

    public GetRoleByIdQueryHandler(IAuthUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<RoleResponse> Handle(GetRoleByIdQuery request, CancellationToken cancellationToken)
    {
        var role = await _unitOfWork.Roles.GetByIdAsync(request.Id);
        if (role == null)
        {
            return new RoleResponse
            {
                IsSuccess = false,
                StatusCode = 404,
                Message = "Không tìm thấy role."
            };
        }

        return new RoleResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = new RoleDto
            {
                Id = role.Id,
                Name = role.Name,
                NormalizedName = role.NormalizedName,
                Description = role.Description,
                Status = role.Status,
                IsSystemRole = role.IsSystemRole,
                CreatedAt = role.CreatedAt,
                UpdatedAt = role.UpdatedAt
            }
        };
    }
}
