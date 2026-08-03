using SharedContracts.Common.Responses;
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
    Task<AlertTicketSagaDTO?> GetByAlertIdAsync(Guid alertId, CancellationToken cancellationToken);

    /// <summary>
    /// Trả thẳng <see cref="PaginationResponse{T}"/> thay vì tuple (Items, Total): handler gọi nó chỉ
    /// việc gán vào <c>Data</c>, không phải dựng lại khối phân trang bằng tay — mỗi lần dựng tay là một
    /// cơ hội gán nhầm TotalItems hoặc quên PageNumber đã kẹp.
    /// </summary>
    Task<PaginationResponse<AlertTicketSagaDTO>> QueryAsync(
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
