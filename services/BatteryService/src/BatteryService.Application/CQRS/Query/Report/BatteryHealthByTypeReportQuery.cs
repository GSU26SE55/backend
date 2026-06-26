using BatteryService.Application.DTOs.Reports;
using MediatR;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.CQRS.Query.Report;

/// <summary>Sprint 7 #114 (§5.2) — sức khỏe pin theo loại: tổng asset, số có alert active, health score.</summary>
public class BatteryHealthByTypeReportQuery : IRequest<CommonResponse<List<BatteryHealthByTypeRow>>>
{
}
