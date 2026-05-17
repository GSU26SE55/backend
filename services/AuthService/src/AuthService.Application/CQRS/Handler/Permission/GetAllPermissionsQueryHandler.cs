using AuthService.Application.CQRS.Query.Permission;
using AuthService.Application.DTOs.Response.Permission;
using AuthService.Application.Interfaces.Repositories;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace AuthService.Application.CQRS.Handler.Permission;

public class GetAllPermissionsQueryHandler : IRequestHandler<GetAllPermissionsQuery, PermissionListResponse>
{
    private readonly IAuthUnitOfWork _unitOfWork;

    public GetAllPermissionsQueryHandler(IAuthUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<PermissionListResponse> Handle(GetAllPermissionsQuery request, CancellationToken cancellationToken)
    {
        var query = _unitOfWork.Permissions.GetAllAsync().AsNoTracking();

        if (!string.IsNullOrWhiteSpace(request.Module))
        {
            var module = request.Module.Trim();
            query = query.Where(p => p.Module == module);
        }

        var items = await query
            .OrderBy(p => p.Module)
            .ThenBy(p => p.Code)
            .Select(p => new PermissionDto
            {
                Id = p.Id,
                Code = p.Code,
                Module = p.Module,
                Description = p.Description,
                IsSystemPermission = p.IsSystemPermission,
                CreatedAt = p.CreatedAt
            })
            .ToListAsync(cancellationToken);

        return new PermissionListResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = items
        };
    }
}
