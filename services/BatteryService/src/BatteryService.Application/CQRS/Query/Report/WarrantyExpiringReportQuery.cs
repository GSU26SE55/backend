using BatteryService.Application.DTOs.Reports;
using MediatR;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.CQRS.Query.Report;

/// <summary>Sprint 7 #114 (§5.2) — asset sắp hết bảo hành trong N ngày (mặc định 90).</summary>
public class WarrantyExpiringReportQuery : IRequest<CommonResponse<List<WarrantyExpiringRow>>>
{
    /// <summary>Chuỗi như "90d" hoặc số ngày. Mặc định 90.</summary>
    public string? Within { get; set; }
}
