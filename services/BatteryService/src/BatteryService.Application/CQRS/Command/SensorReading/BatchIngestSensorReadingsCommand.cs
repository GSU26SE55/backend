using BatteryService.Application.DTOs;
using BatteryService.Domain.Enums;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;

namespace BatteryService.Application.CQRS.Command.SensorReading;

public class BatchIngestSensorReadingsCommand : IRequest<CommonResponse<SensorReadingBatchIngestResult>>, IValidatable<CommonResponse<SensorReadingBatchIngestResult>>
{
    public List<SensorReadingItem> Items { get; set; } = new();

    public Task<CommonResponse<SensorReadingBatchIngestResult>> ValidateAsync()
    {
        var response = new CommonResponse<SensorReadingBatchIngestResult>();

        if (Items.Count == 0)
            AddError(response, nameof(Items), "Danh sách readings là bắt buộc.");

        for (var i = 0; i < Items.Count; i++)
        {
            var item = Items[i];
            var prefix = $"{nameof(Items)}[{i}]";

            if (item.BatteryAssetId == Guid.Empty)
                AddError(response, $"{prefix}.{nameof(item.BatteryAssetId)}", "Id tài sản pin là bắt buộc.");

            if (item.Time == default)
                AddError(response, $"{prefix}.{nameof(item.Time)}", "Thời điểm reading là bắt buộc.");
            else if (item.Time.ToUniversalTime() > DateTime.UtcNow.AddMinutes(5))
                AddError(response, $"{prefix}.{nameof(item.Time)}", "Thời điểm reading không được nằm quá xa trong tương lai.");

            if (item.Voltage < 0)
                AddError(response, $"{prefix}.{nameof(item.Voltage)}", "Điện áp không được âm.");

            if (item.Temperature is < -50 or > 120)
                AddError(response, $"{prefix}.{nameof(item.Temperature)}", "Nhiệt độ phải nằm trong khoảng -50 đến 120 độ C.");

            if (item.SocPercent is < 0 or > 100)
                AddError(response, $"{prefix}.{nameof(item.SocPercent)}", "SOC phải nằm trong khoảng 0-100.");

            if (item.CycleCount is < 0)
                AddError(response, $"{prefix}.{nameof(item.CycleCount)}", "Số chu kỳ không được âm.");

            if (item.SourceDeviceId?.Length > 64)
                AddError(response, $"{prefix}.{nameof(item.SourceDeviceId)}", "Id thiết bị nguồn tối đa 64 ký tự.");

            if (item.SohPercent is < 0 or > 100)
                AddError(response, $"{prefix}.{nameof(item.SohPercent)}", "SOH phải nằm trong khoảng 0-100.");

            if (item.ChargingState.HasValue && !Enum.IsDefined(typeof(ChargingStateEnum), item.ChargingState.Value))
                AddError(response, $"{prefix}.{nameof(item.ChargingState)}", "ChargingState không hợp lệ.");
        }

        return Task.FromResult(response);
    }

    private static void AddError(CommonResponse<SensorReadingBatchIngestResult> response, string field, string detail)
    {
        response.IsSuccess = false;
        response.StatusCode = 400;
        response.Message = "Dữ liệu sensor reading không hợp lệ.";
        response.ListErrors.Add(new Errors { Field = field, Detail = detail });
    }
}

public class SensorReadingItem
{
    public DateTime Time { get; set; }

    public Guid BatteryAssetId { get; set; }

    public decimal Voltage { get; set; }

    public decimal Current { get; set; }

    public decimal Temperature { get; set; }

    public decimal SocPercent { get; set; }

    public int? CycleCount { get; set; }

    public decimal? SohPercent { get; set; }

    public ChargingStateEnum? ChargingState { get; set; }

    public string? SourceDeviceId { get; set; }
}
