using System.Text.Json.Serialization;
using BatteryService.Application.DTOs;
using MediatR;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.CQRS.Query.BatteryAsset;

public class GetBatteryAssetByIdQuery : IRequest<CommonResponse<BatteryAssetDto>>
{
    [JsonIgnore]
    [BindNever]
    public Guid Id { get; set; }
}
