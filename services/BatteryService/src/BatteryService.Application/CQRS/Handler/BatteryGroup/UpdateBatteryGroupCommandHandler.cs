using BatteryService.Application.CQRS.Command.BatteryGroup;
using BatteryService.Application.DTOs;
using BatteryService.Application.Interfaces;
using BatteryService.Application.Mapping;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.CQRS.Handler.BatteryGroup;

public class UpdateBatteryGroupCommandHandler : IRequestHandler<UpdateBatteryGroupCommand, CommonResponse<BatteryGroupDto>>
{
    private readonly IBatteryUnitOfWork _unitOfWork;

    public UpdateBatteryGroupCommandHandler(IBatteryUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<CommonResponse<BatteryGroupDto>> Handle(UpdateBatteryGroupCommand request, CancellationToken cancellationToken)
    {
        var entity = await _unitOfWork.BatteryGroups
            .GetAllAsync()
            .Include(group => group.Site)
            .Include(group => group.BatteryType)
            .FirstOrDefaultAsync(group => group.Id == request.Id && !group.IsDeleted, cancellationToken);

        if (entity is null)
            return NotFound();

        var assetCount = await _unitOfWork.BatteryAssets
            .GetAllAsync()
            .CountAsync(asset => asset.BatteryGroupId == request.Id && !asset.IsDeleted, cancellationToken);

        if (assetCount > 0 && (request.SiteId != entity.SiteId || request.BatteryTypeId != entity.BatteryTypeId))
        {
            return new CommonResponse<BatteryGroupDto>
            {
                IsSuccess = false,
                StatusCode = 409,
                Message = "Không thể đổi site hoặc loại pin của nhóm đang có tài sản pin."
            };
        }

        var site = await _unitOfWork.Sites
            .GetAllAsync()
            .FirstOrDefaultAsync(item => item.Id == request.SiteId && !item.IsDeleted, cancellationToken);

        if (site is null)
        {
            return new CommonResponse<BatteryGroupDto>
            {
                IsSuccess = false,
                StatusCode = 404,
                Message = "Không tìm thấy site."
            };
        }

        var batteryType = await _unitOfWork.BatteryTypes
            .GetAllAsync()
            .FirstOrDefaultAsync(type => type.Id == request.BatteryTypeId && !type.IsDeleted, cancellationToken);

        if (batteryType is null)
        {
            return new CommonResponse<BatteryGroupDto>
            {
                IsSuccess = false,
                StatusCode = 404,
                Message = "Không tìm thấy loại pin."
            };
        }

        var name = request.Name.Trim();
        var duplicate = await _unitOfWork.BatteryGroups
            .GetAllAsync()
            .AnyAsync(group =>
                group.Id != request.Id &&
                group.SiteId == request.SiteId &&
                !group.IsDeleted &&
                group.Name.ToLower() == name.ToLower(), cancellationToken);

        if (duplicate)
        {
            return new CommonResponse<BatteryGroupDto>
            {
                IsSuccess = false,
                StatusCode = 409,
                Message = "Tên nhóm pin đã tồn tại trong site.",
                ListErrors = { new Errors { Field = nameof(request.Name), Detail = "Tên nhóm pin đã tồn tại trong site." } }
            };
        }

        entity.SiteId = site.Id;
        entity.Site = site;
        entity.Name = name;
        entity.BatteryTypeId = batteryType.Id;
        entity.BatteryType = batteryType;

        _unitOfWork.BatteryGroups.UpdateAsync(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new CommonResponse<BatteryGroupDto>
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Cập nhật nhóm pin thành công.",
            Data = BatteryMapper.ToDto(entity)
        };
    }

    private static CommonResponse<BatteryGroupDto> NotFound()
    {
        return new CommonResponse<BatteryGroupDto>
        {
            IsSuccess = false,
            StatusCode = 404,
            Message = "Không tìm thấy nhóm pin."
        };
    }
}
