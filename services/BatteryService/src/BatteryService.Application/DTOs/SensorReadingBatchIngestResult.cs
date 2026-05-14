namespace BatteryService.Application.DTOs;

public class SensorReadingBatchIngestResult
{
    public int TotalReceived { get; set; }

    public int Inserted { get; set; }

    public int Skipped { get; set; }
}
