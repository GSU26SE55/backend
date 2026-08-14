using System.Text.Json.Serialization;
using BatteryService.Application.DTOs;
using MediatR;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;

namespace BatteryService.Application.CQRS.Query.SensorReading;

/// <summary>
/// Sprint Bonus NS-06 (#650, PA-4) — đọc continuous aggregate 1h (<c>sensor_readings_agg_1h</c>).
/// Dùng cho chart range dài (tháng/năm) mà /aggregate in-memory (NS-02) sẽ chậm/hết RAM.
/// Cùng shape <see cref="SensorReadingAggregateDto"/>, bucket cố định 1h, chỉ source primary.
/// </summary>
public class GetSensorReadingHourlyAggregateQuery
    : IRequest<CommonResponse<List<SensorReadingAggregateDto>>>,
      IValidatable<CommonResponse<List<SensorReadingAggregateDto>>>
{
    /// <summary>ID BatteryAsset (Guid) — lấy từ route, không bind query/body để tránh client override.</summary>
    [JsonIgnore]
    [BindNever]
    public Guid BatteryAssetId { get; set; }

    /// <summary>Filter bucket bắt đầu (UTC inclusive).</summary>
    public DateTime? From { get; set; }

    /// <summary>Filter bucket kết thúc (UTC inclusive).</summary>
    public DateTime? To { get; set; }

    public Task<CommonResponse<List<SensorReadingAggregateDto>>> ValidateAsync()
    {
        var response = new CommonResponse<List<SensorReadingAggregateDto>>();

        if (BatteryAssetId == Guid.Empty)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Invalid data.";
            response.ListErrors.Add(new Errors { Field = nameof(BatteryAssetId), Detail = "Battery asset ID is required." });
        }

        if (From.HasValue && To.HasValue && ToUtc(From.Value) > ToUtc(To.Value))
        {
            response.IsSuccess = false;
            if (response.StatusCode != 400)
                response.StatusCode = 422;
            response.Message = "Invalid data.";
            response.ListErrors.Add(new Errors { Field = nameof(To), Detail = "The end time must be greater than or equal to the start time." });
        }

        return Task.FromResult(response);
    }

    private static DateTime ToUtc(DateTime value) =>
        value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
            : value.ToUniversalTime();
}
