using AuthService.Application.CQRS.Command.Role;
using AuthService.Application.DTOs.Response.Role;
using AuthService.Application.Interfaces.Repositories;
using AuthService.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using RoleEntity = AuthService.Domain.Entities.Role;

namespace AuthService.Application.CQRS.Handler.Role;

public class CreateRoleCommandHandler : IRequestHandler<CreateRoleCommand, RoleActionResponse>
{
    private readonly IAuthUnitOfWork _unitOfWork;

    public CreateRoleCommandHandler(IAuthUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<RoleActionResponse> Handle(CreateRoleCommand request, CancellationToken cancellationToken)
    {
        var normalized = request.Name.Trim().ToUpperInvariant();

        var existed = await _unitOfWork.Roles
            .GetAllAsync()
            .AnyAsync(r => r.NormalizedName == normalized && !r.IsDeleted, cancellationToken);

        if (existed)
        {
            return new RoleActionResponse
            {
                IsSuccess = false,
                StatusCode = 409,
                Message = "Tên role đã tồn tại.",
            };
        }

        var role = new RoleEntity
        {
            Id = Guid.NewGuid(),
            Name = request.Name.Trim(),
            NormalizedName = normalized,
            Description = request.Description?.Trim(),
            Status = RoleStatusEnum.Active,
            IsSystemRole = false
        };

        await _unitOfWork.Roles.AddAsync(role);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new RoleActionResponse
        {
            IsSuccess = true,
            StatusCode = 201,
            Message = "Tạo role thành công.",
            Data = role.Id
        };
    }
}
