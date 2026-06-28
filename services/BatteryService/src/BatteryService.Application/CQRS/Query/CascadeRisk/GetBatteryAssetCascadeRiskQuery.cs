using System.Text.Json.Serialization;
using BatteryService.Application.DTOs;
using MediatR;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.CQRS.Query.CascadeRisk;

/// <summary>Sprint 7 B4 (§31.7) — cascade risk hiện tại của 1 asset.</summary>
public class GetBatteryAssetCascadeRiskQuery : IRequest<CommonResponse<CascadeRiskDto>>
{
    [JsonIgnore]
    [BindNever]
    public Guid Id { get; set; }
}
