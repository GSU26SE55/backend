using BatteryService.Application.CQRS.Command.Site;
using BatteryService.Application.DTOs;
using BatteryService.Application.Interfaces;
using BatteryService.Application.Mapping;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.CQRS.Handler.Site;

public class UpdateSiteCommandHandler : IRequestHandler<UpdateSiteCommand, CommonResponse<SiteDto>>
{
    private readonly IBatteryUnitOfWork _unitOfWork;

    public UpdateSiteCommandHandler(IBatteryUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<CommonResponse<SiteDto>> Handle(UpdateSiteCommand request, CancellationToken cancellationToken)
    {
        var entity = await _unitOfWork.Sites
            .GetAllAsync()
            .Include(site => site.BatteryAssets)
            .FirstOrDefaultAsync(site => site.Id == request.Id && !site.IsDeleted, cancellationToken);

        if (entity is null)
            return NotFound();

        if (request.CustomerId != Guid.Empty && request.CustomerId != entity.CustomerId)
        {
            return new CommonResponse<SiteDto>
            {
                IsSuccess = false,
                StatusCode = 409,
                Message = "Cannot change the site owner via the update endpoint."
            };
        }

        var name = request.Name.Trim();
        var duplicate = await _unitOfWork.Sites
            .GetAllAsync()
            .AnyAsync(site =>
                site.Id != request.Id &&
                !site.IsDeleted &&
                site.CustomerId == entity.CustomerId &&
                site.Name.ToLower() == name.ToLower(), cancellationToken);

        if (duplicate)
        {
            return new CommonResponse<SiteDto>
            {
                IsSuccess = false,
                StatusCode = 409,
                Message = "Site name already exists for this customer.",
            };
        }

        entity.Name = name;
        entity.Address = request.Address?.Trim();
        entity.Latitude = request.Latitude;
        entity.Longitude = request.Longitude;
        entity.InstallDate = ToUtc(request.InstallDate);
        entity.Status = request.Status;
        entity.ContactPersonName = request.ContactPersonName?.Trim();
        entity.ContactPersonPhone = request.ContactPersonPhone?.Trim();

        var customerName = await _unitOfWork.CustomerAccounts
            .GetAllAsync()
            .AsNoTracking()
            .Where(account => account.Id == entity.CustomerId && !account.IsDeleted)
            .Select(account => account.FullName)
            .FirstOrDefaultAsync(cancellationToken) ?? string.Empty;

        _unitOfWork.Sites.UpdateAsync(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new CommonResponse<SiteDto>
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Site updated successfully.",
            Data = BatteryMapper.ToDto(entity, customerName)
        };
    }

    private static CommonResponse<SiteDto> NotFound()
    {
        return new CommonResponse<SiteDto>
        {
            IsSuccess = false,
            StatusCode = 404,
            Message = "Site not found."
        };
    }

    private static DateTime ToUtc(DateTime value)
    {
        return value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
            : value.ToUniversalTime();
    }
}
