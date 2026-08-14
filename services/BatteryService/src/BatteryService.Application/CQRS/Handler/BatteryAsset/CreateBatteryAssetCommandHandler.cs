using BatteryService.Application.CQRS.Command.BatteryAsset;
using BatteryService.Application.DTOs;
using BatteryService.Application.Interfaces;
using BatteryService.Application.Mapping;
using BatteryService.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using SharedContracts.Events;
using SharedContracts.Interfaces;
using BatteryAssetEntity = BatteryService.Domain.Entities.BatteryAsset;
using SiteEntity = BatteryService.Domain.Entities.Site;

namespace BatteryService.Application.CQRS.Handler.BatteryAsset;

public class CreateBatteryAssetCommandHandler : IRequestHandler<CreateBatteryAssetCommand, CommonResponse<BatteryAssetDto>>
{
    private readonly IBatteryUnitOfWork _unitOfWork;
    private readonly IIntegrationEventOutboxWriter _outbox;
    private readonly MediatR.IPublisher _publisher;   // Sprint audit #AUDIT-22

    public CreateBatteryAssetCommandHandler(IBatteryUnitOfWork unitOfWork, IIntegrationEventOutboxWriter outbox, MediatR.IPublisher publisher)
    {
        _unitOfWork = unitOfWork;
        _outbox = outbox;
        _publisher = publisher;
    }

    public async Task<CommonResponse<BatteryAssetDto>> Handle(CreateBatteryAssetCommand request, CancellationToken cancellationToken)
    {
        var serial = request.SerialNumber.Trim().ToUpperInvariant();
        var duplicate = await _unitOfWork.BatteryAssets
            .GetAllAsync()
            .AnyAsync(asset => !asset.IsDeleted && asset.SerialNumber == serial, cancellationToken);

        if (duplicate)
        {
            return new CommonResponse<BatteryAssetDto>
            {
                IsSuccess = false,
                StatusCode = 409,
                Message = "Battery serial number already exists.",
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
                Message = "Battery type not found."
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
                Message = "Site not found."
            };
        }

        var relationError = ValidateSite(request.CustomerId, site);
        if (relationError is not null)
            return relationError;

        var customer = await _unitOfWork.CustomerAccounts
            .GetAllAsync()
            .FirstOrDefaultAsync(account =>
                account.Id == request.CustomerId &&
                account.IsActive &&
                !account.IsDeleted, cancellationToken);

        if (customer is null)
        {
            return new CommonResponse<BatteryAssetDto>
            {
                IsSuccess = false,
                StatusCode = 404,
                Message = "Active customer not found in BatteryService. Resynchronize the account from AuthService.",
            };
        }

        var entity = new BatteryAssetEntity
        {
            Id = Guid.NewGuid(),
            SerialNumber = serial,
            BatteryTypeId = request.BatteryTypeId,
            BatteryType = batteryType,
            SiteId = site?.Id,
            Site = site,
            CustomerId = request.CustomerId,
            InstallDate = ToUtc(request.InstallDate),
            WarrantyEndDate = ToUtc(request.WarrantyEndDate),
            WarrantyStatus = request.WarrantyEndDate.HasValue && ToUtc(request.WarrantyEndDate.Value) <= DateTime.UtcNow
                ? WarrantyStatusEnum.Expired
                : WarrantyStatusEnum.Active,
            Location = request.Location?.Trim(),
            Latitude = request.Latitude,
            Longitude = request.Longitude,
            Status = BatteryStatusEnum.Active,
            Notes = request.Notes?.Trim()
        };

        await _unitOfWork.BatteryAssets.AddAsync(entity);

        await _outbox.WriteAsync(new BatteryAssetCreatedEvent(
            BatteryAssetId: entity.Id,
            CustomerId: entity.CustomerId,
            SiteId: entity.SiteId,
            BatteryTypeId: entity.BatteryTypeId,
            SerialNumber: entity.SerialNumber,
            CreatedAt: DateTime.UtcNow), cancellationToken);

        // #AUDIT-22
        await _publisher.Publish(BatteryService.Application.CQRS.Notification.Audit.BatteryAuditTrailNotification.For(
            BatteryService.Domain.Enums.BatteryAuditActionEnum.BatteryCreated, entity.Id,
            targetDisplay: entity.SerialNumber), cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new CommonResponse<BatteryAssetDto>
        {
            IsSuccess = true,
            StatusCode = 201,
            Message = "Battery asset created successfully.",
            Data = BatteryMapper.ToDto(entity, customer.FullName)
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
                Message = "Site does not belong to the battery asset's customer."
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
