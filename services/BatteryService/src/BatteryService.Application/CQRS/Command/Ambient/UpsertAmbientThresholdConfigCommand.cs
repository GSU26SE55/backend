using BatteryService.Application.DTOs;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;

namespace BatteryService.Application.CQRS.Command.Ambient;

/// <summary>
/// Sprint 5B #92 — upsert AmbientThresholdConfig per site.
/// </summary>
public class UpsertAmbientThresholdConfigCommand
    : IRequest<AmbientThresholdConfigResponse>, IValidatable<AmbientThresholdConfigResponse>
{
    public Guid SiteId { get; set; }
    public decimal? HighAmbientTempWarning { get; set; }
    public decimal? HighAmbientTempCritical { get; set; }
    public decimal? HighHumidityWarning { get; set; }
    public decimal? HighHumidityCritical { get; set; }
    public decimal? ComboTempThreshold { get; set; }
    public decimal? ComboHumidityThreshold { get; set; }
    public bool Enabled { get; set; } = true;

    public Task<AmbientThresholdConfigResponse> ValidateAsync()
    {
        var response = new AmbientThresholdConfigResponse();

        if (SiteId == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = nameof(SiteId), Detail = "SiteId bắt buộc." });

        if (HighAmbientTempWarning.HasValue && HighAmbientTempCritical.HasValue
            && HighAmbientTempCritical.Value < HighAmbientTempWarning.Value)
        {
            response.ListErrors.Add(new Errors
            {
                Field = nameof(HighAmbientTempCritical),
                Detail = "Critical phải >= Warning."
            });
        }

        if (HighHumidityWarning.HasValue && HighHumidityCritical.HasValue
            && HighHumidityCritical.Value < HighHumidityWarning.Value)
        {
            response.ListErrors.Add(new Errors
            {
                Field = nameof(HighHumidityCritical),
                Detail = "Critical phải >= Warning."
            });
        }

        if (response.ListErrors.Count > 0)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Threshold config không hợp lệ.";
        }
        return Task.FromResult(response);
    }
}
