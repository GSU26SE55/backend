using System.Text.Json.Serialization;
using BatteryService.Application.DTOs;
using MediatR;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.CQRS.Query.IotDevice;

public class CheckIotFirmwareUpdateQuery : IRequest<CommonResponse<IotFirmwareCheckDto>>
{
    /// <summary>Lấy từ claim "iot:device_id" — không bind query/body.</summary>
    [JsonIgnore]
    [BindNever]
    public Guid DeviceId { get; set; }

    /// <summary>Field CurrentVersion.</summary>
    public string CurrentVersion { get; set; } = string.Empty;
}
