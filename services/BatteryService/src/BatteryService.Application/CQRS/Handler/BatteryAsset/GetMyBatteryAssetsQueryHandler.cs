using BatteryService.Application.CQRS.Query.BatteryAsset;
using BatteryService.Application.DTOs;
using BatteryService.Application.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using SharedInfrastructure.Services;

namespace BatteryService.Application.CQRS.Handler.BatteryAsset;

public class GetMyBatteryAssetsQueryHandler : IRequestHandler<GetMyBatteryAssetsQuery, CommonResponse<PaginationResponse<BatteryAssetDto>>>
{
    private readonly IBatteryUnitOfWork _unitOfWork;
    private readonly ICurrentUserService _currentUserService;

    public GetMyBatteryAssetsQueryHandler(IBatteryUnitOfWork unitOfWork, ICurrentUserService currentUserService)
    {
        _unitOfWork = unitOfWork;
        _currentUserService = currentUserService;
    }

    public async Task<CommonResponse<PaginationResponse<BatteryAssetDto>>> Handle(GetMyBatteryAssetsQuery request, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(_currentUserService.UserId, out var customerId))
        {
            return new CommonResponse<PaginationResponse<BatteryAssetDto>>
            {
                IsSuccess = false,
                StatusCode = 401,
                Message = "Không xác định được người dùng hiện tại."
            };
        }

        var query = _unitOfWork.BatteryAssets
            .GetAllAsync()
            .AsNoTracking()
            .Include(asset => asset.BatteryType)
            .Include(asset => asset.Site)
            .Where(asset => asset.CustomerId == customerId && !asset.IsDeleted);

        var customerName = await _unitOfWork.CustomerAccounts
            .GetAllAsync()
            .AsNoTracking()
            .Where(account => account.Id == customerId && !account.IsDeleted)
            .Select(account => account.FullName)
            .FirstOrDefaultAsync(cancellationToken) ?? string.Empty;

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(asset => asset.CreatedAt)
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(asset => new BatteryAssetDto
            {
                Id = asset.Id.ToString(),
                SerialNumber = asset.SerialNumber,
                BatteryTypeId = asset.BatteryTypeId.ToString(),
                BatteryTypeName = asset.BatteryType.Name,
                SiteId = asset.SiteId.HasValue ? asset.SiteId.Value.ToString() : null,
                SiteName = asset.Site != null ? asset.Site.Name : null,
                CustomerId = asset.CustomerId.ToString(),
                CustomerName = customerName,
                InstallDate = asset.InstallDate,
                WarrantyEndDate = asset.WarrantyEndDate,
                WarrantyStatus = asset.WarrantyStatus,
                Location = asset.Location,
                Latitude = asset.Latitude,
                Longitude = asset.Longitude,
                Status = asset.Status,
                Notes = asset.Notes,
                LastSensorReadingAt = asset.LastSensorReadingAt,
                CreatedAt = asset.CreatedAt
            })
            .ToListAsync(cancellationToken);

        return new CommonResponse<PaginationResponse<BatteryAssetDto>>
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = new PaginationResponse<BatteryAssetDto>
            {
                Items = items,
                TotalItems = total,
                PageNumber = request.PageNumber,
                PageSize = request.PageSize
            }
        };
    }
}
