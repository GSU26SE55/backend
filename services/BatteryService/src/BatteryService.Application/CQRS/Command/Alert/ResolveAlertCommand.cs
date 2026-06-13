using System.Text.Json.Serialization;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;

namespace BatteryService.Application.CQRS.Command.Alert;

public class ResolveAlertCommand : IRequest<CommonResponse<object>>, IValidatable<CommonResponse<object>>
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
            response.Message = "Id cảnh báo không hợp lệ.";
            response.ListErrors.Add(new Errors { Field = nameof(Id), Detail = "Id cảnh báo là bắt buộc." });
        }

        return Task.FromResult(response);
    }
}
