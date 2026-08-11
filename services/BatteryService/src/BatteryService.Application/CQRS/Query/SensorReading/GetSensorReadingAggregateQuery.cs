using BatteryService.Application.DTOs;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;

namespace BatteryService.Application.CQRS.Query.SensorReading;

public class GetSensorReadingAggregateQuery : IRequest<CommonResponse<List<SensorReadingAggregateDto>>>, IValidatable<CommonResponse<List<SensorReadingAggregateDto>>>
{
    private static readonly HashSet<string> ValidIntervals =
        new(StringComparer.OrdinalIgnoreCase) { "1m", "5m", "15m", "1h", "1d" };

    /// <summary>ID BatteryAsset (Guid).</summary>
    public Guid BatteryAssetId { get; set; }

    /// <summary>Filter timestamp bắt đầu (UTC inclusive).</summary>
    public DateTime? From { get; set; }

    /// <summary>Filter timestamp kết thúc (UTC inclusive).</summary>
    public DateTime? To { get; set; }

    /// <summary>Bucket size aggregate (1m | 5m | 15m | 1h | 1d).</summary>
    public string Interval { get; set; } = "1h";

    public Task<CommonResponse<List<SensorReadingAggregateDto>>> ValidateAsync()
    {
        var response = new CommonResponse<List<SensorReadingAggregateDto>>();

        if (BatteryAssetId == Guid.Empty)
            AddError(response, nameof(BatteryAssetId), "Battery asset Id is required.");

        if (!ValidIntervals.Contains(Interval))
            AddError(response, nameof(Interval), $"Invalid interval. Accepted: {string.Join(", ", ValidIntervals)}.");

        if (From.HasValue && To.HasValue && ToUtc(From.Value) > ToUtc(To.Value))
            AddCrossFieldError(response, nameof(To), "The end time must be greater than or equal to the start time.");

        return Task.FromResult(response);
    }

    private static void AddError(CommonResponse<List<SensorReadingAggregateDto>> response, string field, string detail)
    {
        response.IsSuccess = false;
        response.StatusCode = 400;
        response.Message = "Invalid data.";
        response.ListErrors.Add(new Errors { Field = field, Detail = detail });
    }

    private static void AddCrossFieldError(CommonResponse<List<SensorReadingAggregateDto>> response, string field, string detail)
    {
        response.IsSuccess = false;
        // Cross-field business rule violation → 422.
        // Do not overwrite 400 (field-level format errors take precedence).
        if (response.StatusCode != 400)
            response.StatusCode = 422;
        response.Message = "Invalid data.";
        response.ListErrors.Add(new Errors { Field = field, Detail = detail });
    }

    private static DateTime ToUtc(DateTime value)
    {
        return value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
            : value.ToUniversalTime();
    }
}
