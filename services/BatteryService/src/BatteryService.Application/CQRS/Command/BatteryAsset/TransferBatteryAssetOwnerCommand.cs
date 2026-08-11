using System.Text.Json.Serialization;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;

namespace BatteryService.Application.CQRS.Command.BatteryAsset;

public class TransferBatteryAssetOwnerCommand : IRequest<CommonResponse<object>>, IValidatable<CommonResponse<object>>
{
    /// <summary>Định danh resource.</summary>
    [JsonIgnore]
    public Guid Id { get; set; }

    /// <summary>ID customer nhận chuyển nhượng.</summary>
    public Guid NewCustomerId { get; set; }

    /// <summary>Lý do/ghi chú.</summary>
    public string? Reason { get; set; }

    /// <summary>Sprint 5B B11 — user thực hiện transfer (set bởi controller từ JWT).</summary>
    [JsonIgnore]
    public Guid PerformedByUserId { get; set; }

    public Task<CommonResponse<object>> ValidateAsync()
    {
        var response = new CommonResponse<object>();

        if (Id == Guid.Empty)
            AddError(response, nameof(Id), "Battery asset Id is required.");

        if (NewCustomerId == Guid.Empty)
            AddError(response, nameof(NewCustomerId), "New customer Id is required.");

        if (Reason?.Length > 500)
            AddError(response, nameof(Reason), "Ownership transfer reason must not exceed 500 characters.");

        return Task.FromResult(response);
    }

    private static void AddError(CommonResponse<object> response, string field, string detail)
    {
        response.IsSuccess = false;
        response.StatusCode = 400;
        response.Message = "Invalid battery ownership transfer data.";
        response.ListErrors.Add(new Errors { Field = field, Detail = detail });
    }
}
