namespace BatteryService.Application.DTOs;

public class SensorReadingHistoryResponseDto
{
    public List<SensorReadingDto> Items { get; set; } = new();

    public DateTime? NextCursor { get; set; }

    public bool HasMore { get; set; }
}
