using System.Text.Json.Serialization;
using MediatR;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.CQRS.Command.Site;

public class DeleteSiteCommand : IRequest<CommonResponse<object>>
{
    /// <summary>Định danh resource.</summary>
    [JsonIgnore]
    public Guid Id { get; set; }
}
