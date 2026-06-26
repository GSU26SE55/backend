using System.Text.Json.Serialization;
using BatteryService.Application.DTOs;
using MediatR;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.CQRS.Query.CascadeRisk;

/// <summary>Sprint 7 B4 (§31.7) — heat map cascade risk cho toàn bộ asset trong 1 site.</summary>
public class GetSiteCascadeRiskSummaryQuery : IRequest<CommonResponse<SiteCascadeRiskSummaryDto>>
{
    [JsonIgnore]
    [BindNever]
    public Guid SiteId { get; set; }
}
