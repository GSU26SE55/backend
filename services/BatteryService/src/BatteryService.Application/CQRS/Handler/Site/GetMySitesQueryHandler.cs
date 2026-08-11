using BatteryService.Application.CQRS.Query.Site;
using BatteryService.Application.DTOs;
using BatteryService.Application.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using SharedInfrastructure.Extensions;
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
                StatusCode = 500,
                Message = "Could not identify the current user."
            };
        }

        var query = _unitOfWork.Sites
            .GetAllAsync()
            .AsNoTracking()
            .Include(site => site.BatteryAssets)
            .Where(site => site.CustomerId == customerId && !site.IsDeleted);

        var customerName = await _unitOfWork.CustomerAccounts
            .GetAllAsync()
            .AsNoTracking()
            .Where(account => account.Id == customerId && !account.IsDeleted)
            .Select(account => account.FullName)
            .FirstOrDefaultAsync(cancellationToken) ?? string.Empty;

        var page = await query
            .OrderByDescending(site => site.CreatedAt)
            .ThenBy(site => site.Id) // tie-breaker cố định — pagination ổn định
            .Select(site => new SiteDto
            {
                Id = site.Id.ToString(),
                Name = site.Name,
                CustomerId = site.CustomerId.ToString(),
                CustomerName = customerName,
                Address = site.Address,
                Latitude = site.Latitude,
                Longitude = site.Longitude,
                InstallDate = site.InstallDate,
                Status = site.Status,
                ContactPersonName = site.ContactPersonName,
                ContactPersonPhone = site.ContactPersonPhone,
                BatteryAssetCount = site.BatteryAssets.Count(asset => !asset.IsDeleted),
                ActiveBatteryAssetCount = site.BatteryAssets.Count(asset => !asset.IsDeleted && asset.Status == Domain.Enums.BatteryStatusEnum.Active),
                CreatedAt = site.CreatedAt
            })
            .ToPagedEntityListAsync(request.PageNumber, request.PageSize, cancellationToken);

        return new CommonResponse<PaginationResponse<SiteDto>>
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = page
        };
    }
}
