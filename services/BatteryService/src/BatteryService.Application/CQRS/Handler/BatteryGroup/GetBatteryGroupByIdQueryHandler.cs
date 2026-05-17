using BatteryService.Application.CQRS.Query.BatteryGroup;
using BatteryService.Application.DTOs;
using BatteryService.Application.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.CQRS.Handler.BatteryGroup;

public class GetBatteryGroupByIdQueryHandler : IRequestHandler<GetBatteryGroupByIdQuery, CommonResponse<BatteryGroupDto>>
{
    private readonly IBatteryUnitOfWork _unitOfWork;

    public GetBatteryGroupByIdQueryHandler(IBatteryUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<CommonResponse<BatteryGroupDto>> Handle(GetBatteryGroupByIdQuery request, CancellationToken cancellationToken)
    {
        var dto = await _unitOfWork.BatteryGroups
            .GetAllAsync()
            .AsNoTracking()
            .Where(group => group.Id == request.Id && !group.IsDeleted)
            .Select(group => new BatteryGroupDto
            {
                Id = group.Id,
                SiteId = group.SiteId,
                SiteName = group.Site.Name,
                Name = group.Name,
                BatteryTypeId = group.BatteryTypeId,
                BatteryTypeName = group.BatteryType.Name,
                BatteryCount = group.BatteryCount,
                CreatedAt = group.CreatedAt
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (dto is null)
        {
            return new CommonResponse<BatteryGroupDto>
            {
                IsSuccess = false,
                StatusCode = 404,
                Message = "Không tìm thấy nhóm pin."
            };
        }

        return new CommonResponse<BatteryGroupDto>
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = dto
        };
    }
}
