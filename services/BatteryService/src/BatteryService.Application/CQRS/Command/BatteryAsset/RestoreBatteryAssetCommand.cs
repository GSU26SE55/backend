using System.Text.Json.Serialization;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;

namespace BatteryService.Application.CQRS.Command.BatteryAsset;

public class RestoreBatteryAssetCommand : IRequest<CommonResponse<object>>, IValidatable<CommonResponse<object>>
{
    [JsonIgnore]
    public Guid Id { get; set; }

    public Task<CommonResponse<object>> ValidateAsync()
    {
        var response = new CommonResponse<object>();
        if (Id == Guid.Empty)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Id tài sản pin không hợp lệ.";
            response.ListErrors.Add(new Errors { Field = nameof(Id), Detail = "Id tài sản pin là bắt buộc." });
        }

        return Task.FromResult(response);
    }
}
