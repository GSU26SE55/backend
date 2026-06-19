using Microsoft.EntityFrameworkCore;
using TicketService.Application.DTOs.Response.Saga;
using TicketService.Application.Interfaces.Services;
using TicketService.Infrastructure.Persistence;

namespace TicketService.Infrastructure.Implements.Services;

/// <summary>
/// Read-only + reset-state cho <c>AlertTicketSagaState</c>. Truy cập DbContext
/// trực tiếp (entity ở Infrastructure layer vì coupling MassTransit).
///
/// Sprint 5B #239.
/// </summary>
public class AlertTicketSagaQueryService : IAlertTicketSagaQueryService
{
    private readonly TicketDbContext _db;

    public AlertTicketSagaQueryService(TicketDbContext db)
    {
        _db = db;
    }

    public async Task<AlertTicketSagaDTO?> GetByAlertIdAsync(Guid alertId, CancellationToken cancellationToken)
    {
        var saga = await _db.AlertTicketSagaStates
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.AlertId == alertId, cancellationToken);

        return saga is null ? null : Map(saga);
    }

    public async Task<(IReadOnlyList<AlertTicketSagaDTO> Items, int Total)> QueryAsync(
        string? state, Guid? alertId, Guid? batteryAssetId, Guid? customerId,
        DateTime? startedFrom, DateTime? startedTo, bool? isFailed,
        int pageNumber, int pageSize, bool isDescending,
        CancellationToken cancellationToken)
    {
        var query = _db.AlertTicketSagaStates.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(state))
            query = query.Where(s => s.CurrentState == state);

        if (alertId.HasValue)
            query = query.Where(s => s.AlertId == alertId.Value);

        if (batteryAssetId.HasValue)
            query = query.Where(s => s.BatteryAssetId == batteryAssetId.Value);

        if (customerId.HasValue)
            query = query.Where(s => s.CustomerId == customerId.Value);

        if (startedFrom.HasValue)
            query = query.Where(s => s.StartedAt >= startedFrom.Value);

        if (startedTo.HasValue)
            query = query.Where(s => s.StartedAt <= startedTo.Value);

        if (isFailed == true)
            query = query.Where(s => s.CurrentState == "Failed");
        else if (isFailed == false)
            query = query.Where(s => s.CurrentState != "Failed");

        var total = await query.CountAsync(cancellationToken);

        query = isDescending
            ? query.OrderByDescending(s => s.StartedAt)
            : query.OrderBy(s => s.StartedAt);

        var rows = await query
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (rows.Select(Map).ToList(), total);
    }

    public async Task<bool> ResetFailedStateAsync(Guid alertId, CancellationToken cancellationToken)
    {
        var saga = await _db.AlertTicketSagaStates
            .FirstOrDefaultAsync(s => s.AlertId == alertId, cancellationToken);

        if (saga is null || saga.CurrentState != "Failed")
            return false;

        // Reset to Initial — state machine sẽ pick up trên redelivery của anomaly event.
        // Note: Saga reprocess workflow (xem overall.md §53.11) thường KHÔNG reset state
        // mà republish anomaly event với same AlertId để Saga handle from DuringAny.
        // Đây là escape hatch khi cần force reset state machine.
        saga.CurrentState = "Initial";
        saga.RetryCount = 0;
        saga.FailedAt = null;
        saga.FailedAtStage = null;
        saga.FailureReason = null;
        saga.FailureErrorCode = null;

        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static AlertTicketSagaDTO Map(Sagas.AlertTicketSagaState s) => new()
    {
        CorrelationId = s.CorrelationId.ToString(),
        CurrentState = s.CurrentState,
        AlertId = s.AlertId.ToString(),
        BatteryAssetId = s.BatteryAssetId?.ToString(),
        CustomerId = s.CustomerId.ToString(),
        AssetSerialNumber = s.AssetSerialNumber,
        AnomalyType = s.AnomalyType,
        Severity = s.Severity,
        TicketId = s.TicketId?.ToString(),
        TicketCode = s.TicketCode,
        TicketIsReused = s.TicketIsReused,
        FailedAtStage = s.FailedAtStage,
        FailureReason = s.FailureReason,
        FailureErrorCode = s.FailureErrorCode,
        FailedAt = s.FailedAt,
        RetryCount = s.RetryCount,
        StartedAt = s.StartedAt,
        CompletedAt = s.CompletedAt
    };
}
