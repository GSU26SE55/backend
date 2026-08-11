using System.Text.Json.Serialization;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;

namespace BatteryService.Application.CQRS.Command.BatteryAsset;

public class RestoreBatteryAssetCommand : IRequest<CommonResponse<object>>, IValidatable<CommonResponse<object>>
{
    /// <summary>Định danh resource.</summary>
    [JsonIgnore]
    public Guid Id { get; set; }

    public Task<CommonResponse<object>> ValidateAsync()
    {
        var response = new CommonResponse<object>();
        if (Id == Guid.Empty)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Invalid battery asset Id.";
            response.ListErrors.Add(new Errors { Field = nameof(Id), Detail = "Battery asset Id is required." });
        }

        return Task.FromResult(response);
    }
}
