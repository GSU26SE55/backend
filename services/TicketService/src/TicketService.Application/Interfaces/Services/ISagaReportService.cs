using TicketService.Application.DTOs.Response.Reports;

namespace TicketService.Application.Interfaces.Services;

/// <summary>
/// Sprint 7 #114 (§5.2) — đọc thống kê Alert-Ticket Saga cho report.
///
/// Saga state (<c>AlertTicketSagaState</c>) sống ở Infrastructure (MassTransit) nên Application
/// truy cập qua interface này (giữ clean architecture — handler không inject DbContext).
/// </summary>
public interface ISagaReportService
{
    Task<List<SagaFailedRatePoint>> GetFailedRateAsync(
        DateTime? from, DateTime? to, string granularity, CancellationToken cancellationToken = default);
}
