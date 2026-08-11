using BatteryService.Application.DTOs;
using BatteryService.Domain.Enums;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;

namespace BatteryService.Application.CQRS.Command.Ambient;

/// <summary>
/// Sprint 5B #91 — batch ingest ambient readings (max 100 row / request).
/// Authorize bằng ApiKey scope <c>EnvironmentalIngest</c>.
/// </summary>
public class BatchIngestAmbientReadingsCommand : IRequest<CommonResponse<int>>, IValidatable<CommonResponse<int>>
{
    /// <summary>Danh sách items.</summary>
    public List<AmbientReadingItem> Items { get; set; } = new();

    /// <summary>
    /// GH-806 — site của thiết bị đã xác thực, lấy từ claim <c>iot:site_id</c>.
    /// </summary>
    /// <remarks>
    /// <c>[JsonIgnore][BindNever]</c>: client KHÔNG được đặt trường này qua body. Thiếu hai attribute
    /// đó thì thiết bị chỉ cần tự khai site của mình là đi vòng qua toàn bộ hàng rào.
    /// </remarks>
    [System.Text.Json.Serialization.JsonIgnore]
    [Microsoft.AspNetCore.Mvc.ModelBinding.BindNever]
    public Guid? AuthenticatedDeviceSiteId { get; set; }

    public Task<CommonResponse<int>> ValidateAsync()
    {
        var response = new CommonResponse<int>();

        if (Items.Count == 0)
            response.ListErrors.Add(new Errors { Field = nameof(Items), Detail = "Items must not be empty." });
        else if (Items.Count > 100)
            response.ListErrors.Add(new Errors { Field = nameof(Items), Detail = "Maximum 100 rows per batch." });

        for (int i = 0; i < Items.Count; i++)
        {
            var x = Items[i];
            if (x.SiteId == Guid.Empty)
                response.ListErrors.Add(new Errors { Field = $"Items[{i}].SiteId", Detail = "SiteId is required." });
            if (x.Humidity < 0 || x.Humidity > 100)
                response.ListErrors.Add(new Errors { Field = $"Items[{i}].Humidity", Detail = "Humidity must be within [0, 100]." });
            if (x.AmbientTemperature < -90 || x.AmbientTemperature > 90)
                response.ListErrors.Add(new Errors { Field = $"Items[{i}].AmbientTemperature", Detail = "Temperature must be within [-90, 90]." });
        }

        if (response.ListErrors.Count > 0)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Invalid ingest data.";
        }
        return Task.FromResult(response);
    }
}

public class AmbientReadingItem
{
    /// <summary>ID Site (Guid).</summary>
    public Guid SiteId { get; set; }
    /// <summary>Timestamp của reading (UTC).</summary>
    public DateTime Time { get; set; }
    /// <summary>Nhiệt độ môi trường (°C).</summary>
    public decimal AmbientTemperature { get; set; }
    /// <summary>Độ ẩm tương đối (%).</summary>
    public decimal? Humidity { get; set; }
    /// <summary>Bức xạ mặt trời (W/m²).</summary>
    public decimal? SolarIrradiance { get; set; }
    /// <summary>Nguồn dữ liệu (IotSensor | Manual | External).</summary>
    public AmbientReadingSourceEnum Source { get; set; } = AmbientReadingSourceEnum.IotSensor;
    /// <summary>ID thiết bị nguồn (≤ 64 ký tự).</summary>
    public string? SourceDeviceId { get; set; }
}
