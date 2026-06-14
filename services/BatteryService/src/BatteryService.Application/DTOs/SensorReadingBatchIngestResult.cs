namespace BatteryService.Application.DTOs;

public class SensorReadingBatchIngestResult
{
    /// <summary>Tổng số reading nhận trong batch.</summary>
    public int TotalReceived { get; set; }

    /// <summary>Số reading đã insert.</summary>
    public int Inserted { get; set; }

    /// <summary>Số reading bị skip (asset không tồn tại / outlier).</summary>
    public int Skipped { get; set; }
}
