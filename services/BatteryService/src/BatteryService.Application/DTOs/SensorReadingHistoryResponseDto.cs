namespace BatteryService.Application.DTOs;

public class SensorReadingHistoryResponseDto
{
    /// <summary>Danh sách items.</summary>
    public List<SensorReadingDto> Items { get; set; } = new();

    /// <summary>Cursor cho trang kế tiếp.</summary>
    public DateTime? NextCursor { get; set; }

    /// <summary>Còn trang sau (cursor pagination).</summary>
    public bool HasMore { get; set; }
}
