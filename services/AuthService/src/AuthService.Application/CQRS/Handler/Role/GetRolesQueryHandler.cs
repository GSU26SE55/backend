using AuthService.Application.CQRS.Query.Role;
using AuthService.Application.DTOs.Response.Role;
using AuthService.Application.Interfaces.Repositories;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedInfrastructure.Extensions;

namespace AuthService.Application.CQRS.Handler.Role;

public class GetRolesQueryHandler : IRequestHandler<GetRolesQuery, RoleListResponse>
{
    private readonly IAuthUnitOfWork _unitOfWork;

    public GetRolesQueryHandler(IAuthUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<RoleListResponse> Handle(GetRolesQuery request, CancellationToken cancellationToken)
    {
        var query = _unitOfWork.Roles.GetAllAsync().Where(r => !r.IsDeleted).AsNoTracking();

        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var kw = request.Keyword.Trim().ToLower();
            query = query.Where(r =>
                r.Name.ToLower().Contains(kw) ||
                (r.Description != null && r.Description.ToLower().Contains(kw)));
        }

        if (request.Status.HasValue)
            query = query.Where(r => r.Status == request.Status.Value);

        if (request.IsSystemRole.HasValue)
            query = query.Where(r => r.IsSystemRole == request.IsSystemRole.Value);

        var page = await query
            .OrderByDescending(r => r.CreatedAt)
            .ThenBy(r => r.Id) // tie-breaker cố định — pagination ổn định
            .Select(r => new RoleDto
            {
                Id = r.Id,
                Name = r.Name,
                NormalizedName = r.NormalizedName,
                Description = r.Description,
                Status = r.Status,
                IsSystemRole = r.IsSystemRole,
                CreatedAt = r.CreatedAt,
                UpdatedAt = r.UpdatedAt
            })
            .ToPagedEntityListAsync(request.PageNumber, request.PageSize, cancellationToken);

        return new RoleListResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = page
        };
    }
}
