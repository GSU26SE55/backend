using BatteryService.Application.CQRS.Command.BatteryGroup;
using BatteryService.Application.DTOs;
using BatteryService.Application.Interfaces;
using BatteryService.Application.Mapping;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using BatteryGroupEntity = BatteryService.Domain.Entities.BatteryGroup;

namespace BatteryService.Application.CQRS.Handler.BatteryGroup;

public class CreateBatteryGroupCommandHandler : IRequestHandler<CreateBatteryGroupCommand, CommonResponse<BatteryGroupDto>>
{
    private readonly IBatteryUnitOfWork _unitOfWork;

    public CreateBatteryGroupCommandHandler(IBatteryUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<CommonResponse<BatteryGroupDto>> Handle(CreateBatteryGroupCommand request, CancellationToken cancellationToken)
    {
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

        var entity = new BatteryGroupEntity
        {
            Id = Guid.NewGuid(),
            SiteId = site.Id,
            Site = site,
            Name = name,
            BatteryTypeId = batteryType.Id,
            BatteryType = batteryType,
            BatteryCount = 0
        };

        await _unitOfWork.BatteryGroups.AddAsync(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new CommonResponse<BatteryGroupDto>
        {
            IsSuccess = true,
            StatusCode = 201,
            Message = "Tạo nhóm pin thành công.",
            Data = BatteryMapper.ToDto(entity)
        };
    }
}
