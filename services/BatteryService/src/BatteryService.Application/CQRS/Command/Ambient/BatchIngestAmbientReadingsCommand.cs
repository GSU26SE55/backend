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
    public List<AmbientReadingItem> Items { get; set; } = new();

    public Task<CommonResponse<int>> ValidateAsync()
    {
        var response = new CommonResponse<int>();

        if (Items.Count == 0)
            response.ListErrors.Add(new Errors { Field = nameof(Items), Detail = "Items không được rỗng." });
        else if (Items.Count > 100)
            response.ListErrors.Add(new Errors { Field = nameof(Items), Detail = "Tối đa 100 row mỗi batch." });

        for (int i = 0; i < Items.Count; i++)
        {
            var x = Items[i];
            if (x.SiteId == Guid.Empty)
                response.ListErrors.Add(new Errors { Field = $"Items[{i}].SiteId", Detail = "SiteId bắt buộc." });
            if (x.Humidity < 0 || x.Humidity > 100)
                response.ListErrors.Add(new Errors { Field = $"Items[{i}].Humidity", Detail = "Humidity phải nằm [0, 100]." });
            if (x.AmbientTemperature < -90 || x.AmbientTemperature > 90)
                response.ListErrors.Add(new Errors { Field = $"Items[{i}].AmbientTemperature", Detail = "Temperature phải nằm [-90, 90]." });
        }

        if (response.ListErrors.Count > 0)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Dữ liệu ingest không hợp lệ.";
        }
        return Task.FromResult(response);
    }
}

public class AmbientReadingItem
{
    public Guid SiteId { get; set; }
    public DateTime Time { get; set; }
    public decimal AmbientTemperature { get; set; }
    public decimal? Humidity { get; set; }
    public decimal? SolarIrradiance { get; set; }
    public AmbientReadingSourceEnum Source { get; set; } = AmbientReadingSourceEnum.IotSensor;
    public string? SourceDeviceId { get; set; }
}
