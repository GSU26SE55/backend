using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;

namespace BatteryService.Application.CQRS.Command.BatteryType;

public class DeleteBatteryTypeCommand : IRequest<CommonResponse<object>>, IValidatable<CommonResponse<object>>
{
    public Guid Id { get; set; }

    public Task<CommonResponse<object>> ValidateAsync()
    {
        var response = new CommonResponse<object>();
        if (Id == Guid.Empty)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Id loại pin không hợp lệ.";
            response.ListErrors.Add(new Errors { Field = nameof(Id), Detail = "Id loại pin là bắt buộc." });
        }

        return Task.FromResult(response);
    }
}
