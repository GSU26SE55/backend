namespace BatteryService.Application.DTOs.Reports;

/// <summary>Sprint 7 #114 (§5.2) — xu hướng môi trường theo thời gian.</summary>
public class AmbientTrendPoint
{
    public DateTime Date { get; set; }
    public decimal AvgTemp { get; set; }
    public decimal MaxTemp { get; set; }
    public decimal MinTemp { get; set; }
    public decimal? HumidityAvg { get; set; }
    public decimal? IrradianceAvg { get; set; }
}
