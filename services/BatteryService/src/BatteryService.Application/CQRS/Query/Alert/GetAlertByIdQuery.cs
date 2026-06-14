using System.Text.Json.Serialization;
using BatteryService.Application.DTOs;
using MediatR;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.CQRS.Query.Alert;

public class GetAlertByIdQuery : IRequest<CommonResponse<AlertDto>>
{
    /// <summary>Định danh resource.</summary>
    [JsonIgnore]
    [BindNever]
    public Guid Id { get; set; }
}
