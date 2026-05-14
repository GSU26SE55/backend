using BatteryService.Application.CQRS.Query.Site;
using BatteryService.Application.DTOs;
using BatteryService.Application.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using SharedInfrastructure.Services;

namespace BatteryService.Application.CQRS.Handler.Site;

public class GetMySitesQueryHandler : IRequestHandler<GetMySitesQuery, CommonResponse<PaginationResponse<SiteDto>>>
{
    private readonly IBatteryUnitOfWork _unitOfWork;
    private readonly ICurrentUserService _currentUserService;

    public GetMySitesQueryHandler(IBatteryUnitOfWork unitOfWork, ICurrentUserService currentUserService)
    {
        _unitOfWork = unitOfWork;
        _currentUserService = currentUserService;
    }

    public async Task<CommonResponse<PaginationResponse<SiteDto>>> Handle(GetMySitesQuery request, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(_currentUserService.UserId, out var customerId))
        {
            return new CommonResponse<PaginationResponse<SiteDto>>
            {
                IsSuccess = false,
                StatusCode = 401,
                Message = "Không xác định được người dùng hiện tại."
            };
        }

        var query = _unitOfWork.Sites
            .GetAllAsync()
            .AsNoTracking()
            .Include(site => site.BatteryGroups)
            .Include(site => site.BatteryAssets)
            .Where(site => site.CustomerId == customerId && !site.IsDeleted);

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(site => site.CreatedAt)
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(site => new SiteDto
            {
                Id = site.Id,
                Name = site.Name,
                CustomerId = site.CustomerId,
                Address = site.Address,
                Latitude = site.Latitude,
                Longitude = site.Longitude,
                CapacityKw = site.CapacityKw,
                InstallDate = site.InstallDate,
                Status = site.Status,
                ContactPersonName = site.ContactPersonName,
                ContactPersonPhone = site.ContactPersonPhone,
                BatteryGroupCount = site.BatteryGroups.Count(group => !group.IsDeleted),
                BatteryAssetCount = site.BatteryAssets.Count(asset => !asset.IsDeleted),
                ActiveBatteryAssetCount = site.BatteryAssets.Count(asset => !asset.IsDeleted && asset.Status == Domain.Enums.BatteryStatusEnum.Active),
                CreatedAt = site.CreatedAt
            })
            .ToListAsync(cancellationToken);

        return new CommonResponse<PaginationResponse<SiteDto>>
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = new PaginationResponse<SiteDto>
            {
                Items = items,
                TotalItems = total,
                PageNumber = request.PageNumber,
                PageSize = request.PageSize
            }
        };
    }
}
