using BatteryService.Application.CQRS.Query.BatteryType;
using BatteryService.Application.DTOs;
using BatteryService.Application.Interfaces;
using BatteryService.Application.Mapping;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.CQRS.Handler.BatteryType;

public class GetBatteryTypesQueryHandler : IRequestHandler<GetBatteryTypesQuery, CommonResponse<PaginationResponse<BatteryTypeDto>>>
{
    private readonly IBatteryUnitOfWork _unitOfWork;

    public GetBatteryTypesQueryHandler(IBatteryUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<CommonResponse<PaginationResponse<BatteryTypeDto>>> Handle(GetBatteryTypesQuery request, CancellationToken cancellationToken)
    {
        var query = _unitOfWork.BatteryTypes.GetAllAsync().AsNoTracking();

        if (!request.IncludeDeleted)
            query = query.Where(type => !type.IsDeleted);

        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var keyword = request.Keyword.Trim().ToLower();
            query = query.Where(type =>
                type.Name.ToLower().Contains(keyword) ||
                (type.Manufacturer != null && type.Manufacturer.ToLower().Contains(keyword)));
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(type => type.CreatedAt)
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(type => BatteryMapper.ToDto(type))
            .ToListAsync(cancellationToken);

        return new CommonResponse<PaginationResponse<BatteryTypeDto>>
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = new PaginationResponse<BatteryTypeDto>
            {
                Items = items,
                TotalItems = total,
                PageNumber = request.PageNumber,
                PageSize = request.PageSize
            }
        };
    }
}
