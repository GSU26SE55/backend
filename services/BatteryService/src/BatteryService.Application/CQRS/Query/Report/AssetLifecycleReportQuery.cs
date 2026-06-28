using BatteryService.Application.DTOs.Reports;
using MediatR;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.CQRS.Query.Report;

/// <summary>Sprint 7 #114 (§5.2) — vòng đời asset: tuổi (ngày), cycle count (BMS), tổng alert.</summary>
public class AssetLifecycleReportQuery : IRequest<CommonResponse<List<AssetLifecycleRow>>>
{
}
