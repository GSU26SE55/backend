using BatteryService.Application.CQRS.Query.BatteryType;
using BatteryService.Application.DTOs;
using BatteryService.Application.Interfaces;
using BatteryService.Application.Mapping;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Requests;
using SharedContracts.Common.Responses;
using SharedInfrastructure.Extensions;

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

        if (request.Chemistry.HasValue)
            query = query.Where(type => type.Chemistry == request.Chemistry.Value);

        var descending = SortHelper.IsDescending(request.SortDir);
        // Whitelist: name | manufacturer | chemistry | nominalCapacityAh | nominalVoltage | maxCycleCount | createdAt (default).
        var ordered = (request.SortBy?.Trim().ToLowerInvariant()) switch
        {
            "name" => descending ? query.OrderByDescending(type => type.Name) : query.OrderBy(type => type.Name),
            "manufacturer" => descending ? query.OrderByDescending(type => type.Manufacturer) : query.OrderBy(type => type.Manufacturer),
            "chemistry" => descending ? query.OrderByDescending(type => type.Chemistry) : query.OrderBy(type => type.Chemistry),
            "nominalcapacityah" => descending ? query.OrderByDescending(type => type.NominalCapacityAh) : query.OrderBy(type => type.NominalCapacityAh),
            "nominalvoltage" => descending ? query.OrderByDescending(type => type.NominalVoltage) : query.OrderBy(type => type.NominalVoltage),
            "maxcyclecount" => descending ? query.OrderByDescending(type => type.MaxCycleCount) : query.OrderBy(type => type.MaxCycleCount),
            _ => descending ? query.OrderByDescending(type => type.CreatedAt) : query.OrderBy(type => type.CreatedAt),
        };

        // Phân trang trên entity rồi mới map: BatteryMapper.ToDto là method call, EF không dịch được
        // sang SQL — chiếu trước khi cắt trang sẽ làm Skip/Take mất khả năng dịch.
        var page = await ordered
            .ThenBy(type => type.Id) // tie-breaker cố định — pagination ổn định
            .ToPagedEntityListAsync(request.PageNumber, request.PageSize, cancellationToken);

        return new CommonResponse<PaginationResponse<BatteryTypeDto>>
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = page.Map(BatteryMapper.ToDto)
        };
    }
}
