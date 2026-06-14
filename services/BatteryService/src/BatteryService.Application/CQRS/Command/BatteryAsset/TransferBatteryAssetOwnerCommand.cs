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
            AddError(response, nameof(Id), "Id tài sản pin là bắt buộc.");

        if (NewCustomerId == Guid.Empty)
            AddError(response, nameof(NewCustomerId), "Id khách hàng mới là bắt buộc.");

        if (Reason?.Length > 500)
            AddError(response, nameof(Reason), "Lý do chuyển chủ sở hữu tối đa 500 ký tự.");

        return Task.FromResult(response);
    }

    private static void AddError(CommonResponse<object> response, string field, string detail)
    {
        response.IsSuccess = false;
        response.StatusCode = 400;
        response.Message = "Dữ liệu chuyển chủ sở hữu pin không hợp lệ.";
        response.ListErrors.Add(new Errors { Field = field, Detail = detail });
    }
}
