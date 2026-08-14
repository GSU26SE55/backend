using BatteryService.Application.CQRS.Command.Ambient;
using BatteryService.Application.CQRS.Query.Ambient;
using BatteryService.Application.DTOs;
using BatteryService.Application.Interfaces;
using BatteryService.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using SharedInfrastructure.Extensions;

namespace BatteryService.Application.CQRS.Handler.Ambient;

public class UpsertAmbientThresholdConfigCommandHandler
    : IRequestHandler<UpsertAmbientThresholdConfigCommand, AmbientThresholdConfigResponse>
{
    private readonly IBatteryUnitOfWork _uow;
    public UpsertAmbientThresholdConfigCommandHandler(IBatteryUnitOfWork uow) { _uow = uow; }

    public async Task<AmbientThresholdConfigResponse> Handle(UpsertAmbientThresholdConfigCommand request, CancellationToken cancellationToken)
    {
        var existing = await _uow.AmbientThresholdConfigs.GetAllAsync()
            .Where(c => !c.IsDeleted && c.SiteId == request.SiteId)
            .FirstOrDefaultAsync(cancellationToken);

        if (existing is null)
        {
            existing = new AmbientThresholdConfig { Id = Guid.NewGuid(), SiteId = request.SiteId };
            ApplyFields(existing, request);
            await _uow.AmbientThresholdConfigs.AddAsync(existing);
        }
        else
        {
            ApplyFields(existing, request);
            _uow.AmbientThresholdConfigs.UpdateAsync(existing);
        }

        await _uow.SaveChangesAsync(cancellationToken);

        return new AmbientThresholdConfigResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = Map(existing)
        };
    }

    private static void ApplyFields(AmbientThresholdConfig entity, UpsertAmbientThresholdConfigCommand cmd)
    {
        entity.HighAmbientTempWarning = cmd.HighAmbientTempWarning;
        entity.HighAmbientTempCritical = cmd.HighAmbientTempCritical;
        entity.HighHumidityWarning = cmd.HighHumidityWarning;
        entity.HighHumidityCritical = cmd.HighHumidityCritical;
        entity.ComboTempThreshold = cmd.ComboTempThreshold;
        entity.ComboHumidityThreshold = cmd.ComboHumidityThreshold;
        entity.Enabled = cmd.Enabled;
    }

    internal static AmbientThresholdConfigDto Map(AmbientThresholdConfig c) => new()
    {
        Id = c.Id.ToString(),
        SiteId = c.SiteId.ToString(),
        HighAmbientTempWarning = c.HighAmbientTempWarning,
        HighAmbientTempCritical = c.HighAmbientTempCritical,
        HighHumidityWarning = c.HighHumidityWarning,
        HighHumidityCritical = c.HighHumidityCritical,
        ComboTempThreshold = c.ComboTempThreshold,
        ComboHumidityThreshold = c.ComboHumidityThreshold,
        Enabled = c.Enabled,
        CreatedAt = c.CreatedAt
    };
}

public class GetAmbientThresholdBySiteQuery : IRequest<AmbientThresholdConfigResponse>
{
    public Guid SiteId { get; set; }
}

public class GetAmbientThresholdBySiteQueryHandler
    : IRequestHandler<GetAmbientThresholdBySiteQuery, AmbientThresholdConfigResponse>
{
    private readonly IBatteryUnitOfWork _uow;
    public GetAmbientThresholdBySiteQueryHandler(IBatteryUnitOfWork uow) { _uow = uow; }

    public async Task<AmbientThresholdConfigResponse> Handle(GetAmbientThresholdBySiteQuery request, CancellationToken cancellationToken)
    {
        var row = await _uow.AmbientThresholdConfigs.GetAllAsync()
            .Where(c => !c.IsDeleted && c.SiteId == request.SiteId)
            .FirstOrDefaultAsync(cancellationToken);

        if (row is null)
            return new AmbientThresholdConfigResponse { IsSuccess = false, StatusCode = 404, Message = "No threshold config." };

        return new AmbientThresholdConfigResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = UpsertAmbientThresholdConfigCommandHandler.Map(row)
        };
    }
}

public class ListAmbientThresholdsQuery : IRequest<AmbientThresholdConfigListResponse>
{
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}

public class ListAmbientThresholdsQueryHandler
    : IRequestHandler<ListAmbientThresholdsQuery, AmbientThresholdConfigListResponse>
{
    private readonly IBatteryUnitOfWork _uow;
    public ListAmbientThresholdsQueryHandler(IBatteryUnitOfWork uow) { _uow = uow; }

    public async Task<AmbientThresholdConfigListResponse> Handle(ListAmbientThresholdsQuery request, CancellationToken cancellationToken)
    {
        var pageSize = request.PageSize is < 1 or > 200 ? 50 : request.PageSize;
        var pageNumber = request.PageNumber < 1 ? 1 : request.PageNumber;

        var query = _uow.AmbientThresholdConfigs.GetAllAsync().Where(c => !c.IsDeleted);

        // Map là method call → EF không dịch sang SQL được, nên phân trang trên entity rồi mới đổi kiểu.
        var page = await query
            .OrderByDescending(c => c.CreatedAt)
            .ThenBy(c => c.Id) // tie-breaker cố định — pagination ổn định
            .ToPagedEntityListAsync(pageNumber, pageSize, cancellationToken);

        return new AmbientThresholdConfigListResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = page.Map(UpsertAmbientThresholdConfigCommandHandler.Map)
        };
    }
}
