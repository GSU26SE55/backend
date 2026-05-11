namespace AuthService.Application.Configuration;

/// <summary>
/// Config cho session policy. Bind từ appsettings <c>Session</c> section.
/// </summary>
public class SessionOptions
{
    public const string SectionName = "Session";

    /// <summary>
    /// Số session active tối đa mà 1 account có thể giữ cùng lúc. Khi vượt, các session
    /// CŨ NHẤT bị revoke theo FIFO. Default 5. Đặt 0 hoặc giá trị âm để tắt limit.
    /// </summary>
    public int MaxConcurrentSessions { get; set; } = 5;
}
