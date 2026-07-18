using BatteryService.Application.DTOs;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;

namespace BatteryService.Application.CQRS.Query.SensorReading;

public class GetSensorReadingHistoryQuery : IRequest<CommonResponse<SensorReadingHistoryResponseDto>>, IValidatable<CommonResponse<SensorReadingHistoryResponseDto>>
{
    public const int MaxLimit = 1000;

    /// <summary>ID BatteryAsset (Guid).</summary>
    public Guid BatteryAssetId { get; set; }

    /// <summary>Filter timestamp bắt đầu (UTC inclusive).</summary>
    public DateTime? From { get; set; }

    /// <summary>Filter timestamp kết thúc (UTC inclusive).</summary>
    public DateTime? To { get; set; }

    /// <summary>Giới hạn số bản ghi trả về.</summary>
    public int Limit { get; set; } = 100;

    /// <summary>Cursor pagination — timestamp record cuối trang trước.</summary>
    public DateTime? Cursor { get; set; }

    /// <summary>
    /// Cột sort. Whitelist: time (mặc định — cursor path) | voltage | current | temperature | socPercent.
    /// Khi sort theo cột value (khác time) BẮT BUỘC truyền cả from+to (Hướng B — giới hạn khoảng quét, không cursor).
    /// </summary>
    public string? SortBy { get; set; }

    /// <summary>Hướng sort: asc | desc. Mặc định desc.</summary>
    public string? SortDir { get; set; }

    /// <summary>
    /// Chuẩn hoá SortBy → cột value ("voltage"/"current"/"temperature"/"socpercent");
    /// trả null nếu sort theo time (cursor path) hoặc giá trị ngoài whitelist.
    /// </summary>
    public string? NormalizedValueSort() => (SortBy?.Trim().ToLowerInvariant()) switch
    {
        "voltage" => "voltage",
        "current" => "current",
        "temperature" => "temperature",
        "socpercent" => "socpercent",
        _ => null,
    };

    public Task<CommonResponse<SensorReadingHistoryResponseDto>> ValidateAsync()
    {
        var response = new CommonResponse<SensorReadingHistoryResponseDto>();

        if (BatteryAssetId == Guid.Empty)
            AddError(response, nameof(BatteryAssetId), "Id tài sản pin là bắt buộc.");

        if (Limit < 1 || Limit > MaxLimit)
            AddError(response, nameof(Limit), $"Limit phải nằm trong khoảng 1-{MaxLimit}.");

        if (From.HasValue && To.HasValue && ToUtc(From.Value) > ToUtc(To.Value))
            AddCrossFieldError(response, nameof(To), "Thời điểm kết thúc phải lớn hơn hoặc bằng thời điểm bắt đầu.");

        // Hướng B: sort theo cột value (khác time) yêu cầu khoảng [from, to] để giới hạn scan (tránh full-scan hypertable).
        if (NormalizedValueSort() != null && (!From.HasValue || !To.HasValue))
            AddError(response, nameof(SortBy), "Khi sort theo cột khác 'time' phải truyền cả 'from' và 'to'.");

        return Task.FromResult(response);
    }

    private static void AddError(CommonResponse<SensorReadingHistoryResponseDto> response, string field, string detail)
    {
        response.IsSuccess = false;
        response.StatusCode = 400;
        response.Message = "Dữ liệu không hợp lệ.";
        response.ListErrors.Add(new Errors { Field = field, Detail = detail });
    }

    private static void AddCrossFieldError(CommonResponse<SensorReadingHistoryResponseDto> response, string field, string detail)
    {
        response.IsSuccess = false;
        // Cross-field business rule violation → 422.
        // Do not overwrite 400 (field-level format errors take precedence).
        if (response.StatusCode != 400)
            response.StatusCode = 422;
        response.Message = "Dữ liệu không hợp lệ.";
        response.ListErrors.Add(new Errors { Field = field, Detail = detail });
    }

    private static DateTime ToUtc(DateTime value)
    {
        return value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
            : value.ToUniversalTime();
    }
}
