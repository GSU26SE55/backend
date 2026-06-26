using BatteryService.Application.CQRS.Command.BatteryAsset;
using BatteryService.Application.DTOs;
using BatteryService.Application.Interfaces;
using BatteryService.Application.Mapping;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using SiteEntity = BatteryService.Domain.Entities.Site;

namespace BatteryService.Application.CQRS.Handler.BatteryAsset;

public class UpdateBatteryAssetCommandHandler : IRequestHandler<UpdateBatteryAssetCommand, CommonResponse<BatteryAssetDto>>
{
    private readonly IBatteryUnitOfWork _unitOfWork;
    private readonly MediatR.IPublisher _publisher;   // Sprint audit #AUDIT-22

    public UpdateBatteryAssetCommandHandler(IBatteryUnitOfWork unitOfWork, MediatR.IPublisher publisher)
    {
        _unitOfWork = unitOfWork;
        _publisher = publisher;
    }

    public async Task<CommonResponse<BatteryAssetDto>> Handle(UpdateBatteryAssetCommand request, CancellationToken cancellationToken)
    {
        var entity = await _unitOfWork.BatteryAssets
            .GetAllAsync()
            .Include(asset => asset.BatteryType)
            .Include(asset => asset.Site)
            .FirstOrDefaultAsync(asset => asset.Id == request.Id && !asset.IsDeleted, cancellationToken);

        if (entity is null)
        {
            return new CommonResponse<BatteryAssetDto>
            {
                IsSuccess = false,
                StatusCode = 404,
                Message = "Không tìm thấy tài sản pin."
            };
        }

        var serial = request.SerialNumber.Trim().ToUpperInvariant();
        var duplicate = await _unitOfWork.BatteryAssets
            .GetAllAsync()
            .AnyAsync(asset =>
                asset.Id != request.Id &&
                !asset.IsDeleted &&
                asset.SerialNumber == serial, cancellationToken);

        if (duplicate)
        {
            return new CommonResponse<BatteryAssetDto>
            {
                IsSuccess = false,
                StatusCode = 409,
                Message = "Serial pin đã tồn tại.",
            };
        }

        var batteryType = await _unitOfWork.BatteryTypes
            .GetAllAsync()
            .FirstOrDefaultAsync(type => type.Id == request.BatteryTypeId && !type.IsDeleted, cancellationToken);

        if (batteryType is null)
        {
            return new CommonResponse<BatteryAssetDto>
            {
                IsSuccess = false,
                StatusCode = 404,
                Message = "Không tìm thấy loại pin."
            };
        }

        var site = request.SiteId.HasValue
            ? await _unitOfWork.Sites
                .GetAllAsync()
                .FirstOrDefaultAsync(item => item.Id == request.SiteId.Value && !item.IsDeleted, cancellationToken)
            : null;

        if (request.SiteId.HasValue && site is null)
        {
            return new CommonResponse<BatteryAssetDto>
            {
                IsSuccess = false,
                StatusCode = 404,
                Message = "Không tìm thấy site."
            };
        }

        var relationError = ValidateSite(entity.CustomerId, site);
        if (relationError is not null)
            return relationError;

        var customerName = await _unitOfWork.CustomerAccounts
            .GetAllAsync()
            .AsNoTracking()
            .Where(account => account.Id == entity.CustomerId && !account.IsDeleted)
            .Select(account => account.FullName)
            .FirstOrDefaultAsync(cancellationToken) ?? string.Empty;

        entity.SerialNumber = serial;
        entity.BatteryTypeId = request.BatteryTypeId;
        entity.BatteryType = batteryType;
        entity.SiteId = site?.Id;
        entity.Site = site;
        entity.InstallDate = ToUtc(request.InstallDate);
        entity.WarrantyEndDate = ToUtc(request.WarrantyEndDate);
        entity.WarrantyStatus = request.WarrantyStatus;
        entity.Location = request.Location?.Trim();
        entity.Latitude = request.Latitude;
        entity.Longitude = request.Longitude;
        entity.Status = request.Status;
        entity.Notes = request.Notes?.Trim();

        _unitOfWork.BatteryAssets.UpdateAsync(entity);

        // #AUDIT-22
        await _publisher.Publish(BatteryService.Application.CQRS.Notification.Audit.BatteryAuditTrailNotification.For(
            BatteryService.Domain.Enums.BatteryAuditActionEnum.BatteryUpdated, entity.Id,
            targetDisplay: entity.SerialNumber), cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new CommonResponse<BatteryAssetDto>
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Cập nhật tài sản pin thành công.",
            Data = BatteryMapper.ToDto(entity, customerName)
        };
    }

    private static CommonResponse<BatteryAssetDto>? ValidateSite(Guid customerId, SiteEntity? site)
    {
        if (site is not null && site.CustomerId != customerId)
        {
            return new CommonResponse<BatteryAssetDto>
            {
                IsSuccess = false,
                StatusCode = 409,
                Message = "Site không thuộc khách hàng của tài sản pin."
            };
        }

        return null;
    }

    private static DateTime ToUtc(DateTime value)
    {
        return value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
            : value.ToUniversalTime();
    }

    private static DateTime? ToUtc(DateTime? value)
    {
        return value.HasValue ? ToUtc(value.Value) : null;
    }
}
