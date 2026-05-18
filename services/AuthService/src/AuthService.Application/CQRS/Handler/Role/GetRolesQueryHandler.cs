using AuthService.Application.CQRS.Query.Role;
using AuthService.Application.DTOs.Response.Role;
using AuthService.Application.Interfaces.Repositories;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;

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

        var total = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(r => r.CreatedAt)
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
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
            .ToListAsync(cancellationToken);

        return new RoleListResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = new PaginationResponse<RoleDto>
            {
                Items = items,
                TotalItems = total,
                PageNumber = request.PageNumber,
                PageSize = request.PageSize
            }
        };
    }
}
