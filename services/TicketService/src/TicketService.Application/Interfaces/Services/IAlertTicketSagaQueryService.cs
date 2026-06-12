using TicketService.Application.DTOs.Response.Saga;

namespace TicketService.Application.Interfaces.Services;

/// <summary>
/// Read-only query service cho <c>AlertTicketSagaState</c>. Implementation ở
/// Infrastructure (truy cập DbContext trực tiếp — entity nằm ở Infrastructure
/// vì phụ thuộc MassTransit ISagaVersion).
///
/// Sprint 5B #239 (xem overall.md §53.11).
/// </summary>
public interface IAlertTicketSagaQueryService
{
    Task<AlertTicketSagaDto?> GetByAlertIdAsync(Guid alertId, CancellationToken cancellationToken);

    Task<(IReadOnlyList<AlertTicketSagaDto> Items, int Total)> QueryAsync(
        string? state,
        Guid? alertId,
        Guid? batteryAssetId,
        Guid? customerId,
        DateTime? startedFrom,
        DateTime? startedTo,
        bool? isFailed,
        int pageNumber,
        int pageSize,
        bool isDescending,
        CancellationToken cancellationToken);

    Task<bool> ResetFailedStateAsync(Guid alertId, CancellationToken cancellationToken);
}
