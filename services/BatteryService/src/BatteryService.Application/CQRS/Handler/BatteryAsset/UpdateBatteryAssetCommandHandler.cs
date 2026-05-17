using BatteryService.Application.CQRS.Command.BatteryAsset;
using BatteryService.Application.DTOs;
using BatteryService.Application.Interfaces;
using BatteryService.Application.Mapping;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using BatteryGroupEntity = BatteryService.Domain.Entities.BatteryGroup;
using SiteEntity = BatteryService.Domain.Entities.Site;

namespace BatteryService.Application.CQRS.Handler.BatteryAsset;

public class UpdateBatteryAssetCommandHandler : IRequestHandler<UpdateBatteryAssetCommand, CommonResponse<BatteryAssetDto>>
{
    private readonly IBatteryUnitOfWork _unitOfWork;

    public UpdateBatteryAssetCommandHandler(IBatteryUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<CommonResponse<BatteryAssetDto>> Handle(UpdateBatteryAssetCommand request, CancellationToken cancellationToken)
    {
        var entity = await _unitOfWork.BatteryAssets
            .GetAllAsync()
            .Include(asset => asset.BatteryType)
            .Include(asset => asset.Site)
            .Include(asset => asset.BatteryGroup)
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

        var oldGroupId = entity.BatteryGroupId;
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
                ListErrors = { new Errors { Field = nameof(request.SerialNumber), Detail = "Serial pin đã tồn tại." } }
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

        var group = request.BatteryGroupId.HasValue
            ? await _unitOfWork.BatteryGroups
                .GetAllAsync()
                .Include(item => item.Site)
                .FirstOrDefaultAsync(item => item.Id == request.BatteryGroupId.Value && !item.IsDeleted && !item.Site.IsDeleted, cancellationToken)
            : null;

        if (request.BatteryGroupId.HasValue && group is null)
        {
            return new CommonResponse<BatteryAssetDto>
            {
                IsSuccess = false,
                StatusCode = 404,
                Message = "Không tìm thấy nhóm pin."
            };
        }

        var relationError = ValidateSiteAndGroup(entity.CustomerId, request.BatteryTypeId, site, group);
        if (relationError is not null)
            return relationError;

        var customerName = await _unitOfWork.CustomerAccounts
            .GetAllAsync()
            .AsNoTracking()
            .Where(account => account.Id == entity.CustomerId && !account.IsDeleted)
            .Select(account => account.FullName)
            .FirstOrDefaultAsync(cancellationToken) ?? string.Empty;

        site ??= group?.Site;

        entity.SerialNumber = serial;
        entity.BatteryTypeId = request.BatteryTypeId;
        entity.BatteryType = batteryType;
        entity.SiteId = site?.Id;
        entity.Site = site;
        entity.BatteryGroupId = group?.Id;
        entity.BatteryGroup = group;
        entity.InstallDate = ToUtc(request.InstallDate);
        entity.WarrantyEndDate = ToUtc(request.WarrantyEndDate);
        entity.WarrantyStatus = request.WarrantyStatus;
        entity.Location = request.Location?.Trim();
        entity.Latitude = request.Latitude;
        entity.Longitude = request.Longitude;
        entity.Status = request.Status;
        entity.Notes = request.Notes?.Trim();

        if (oldGroupId != entity.BatteryGroupId)
            await UpdateGroupCountsAsync(oldGroupId, entity.BatteryGroupId, cancellationToken);

        _unitOfWork.BatteryAssets.UpdateAsync(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new CommonResponse<BatteryAssetDto>
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Cập nhật tài sản pin thành công.",
            Data = BatteryMapper.ToDto(entity, customerName)
        };
    }

    private async Task UpdateGroupCountsAsync(Guid? oldGroupId, Guid? newGroupId, CancellationToken cancellationToken)
    {
        if (oldGroupId.HasValue)
        {
            var oldGroup = await _unitOfWork.BatteryGroups
                .GetAllAsync()
                .FirstOrDefaultAsync(group => group.Id == oldGroupId.Value, cancellationToken);

            if (oldGroup is not null)
            {
                oldGroup.BatteryCount = Math.Max(0, oldGroup.BatteryCount - 1);
                _unitOfWork.BatteryGroups.UpdateAsync(oldGroup);
            }
        }

        if (newGroupId.HasValue)
        {
            var newGroup = await _unitOfWork.BatteryGroups
                .GetAllAsync()
                .FirstOrDefaultAsync(group => group.Id == newGroupId.Value, cancellationToken);

            if (newGroup is not null)
            {
                newGroup.BatteryCount += 1;
                _unitOfWork.BatteryGroups.UpdateAsync(newGroup);
            }
        }
    }

    private static CommonResponse<BatteryAssetDto>? ValidateSiteAndGroup(
        Guid customerId,
        Guid batteryTypeId,
        SiteEntity? site,
        BatteryGroupEntity? group)
    {
        if (group is not null && group.BatteryTypeId != batteryTypeId)
        {
            return new CommonResponse<BatteryAssetDto>
            {
                IsSuccess = false,
                StatusCode = 409,
                Message = "Loại pin của tài sản phải trùng với loại pin của nhóm."
            };
        }

        if (site is not null && group is not null && group.SiteId != site.Id)
        {
            return new CommonResponse<BatteryAssetDto>
            {
                IsSuccess = false,
                StatusCode = 409,
                Message = "Nhóm pin không thuộc site đã chọn."
            };
        }

        var ownerSite = site ?? group?.Site;
        if (ownerSite is not null && ownerSite.CustomerId != customerId)
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
