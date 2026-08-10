namespace SharedInfrastructure.RateLimiting;

/// <summary>
/// Hạn mức request chuẩn áp cho MỌI service (section <c>RateLimiting</c>).
/// </summary>
/// <remarks>
/// Chia hai bậc theo danh tính, không theo endpoint:
/// <list type="bullet">
///   <item><description><b>Chưa đăng nhập</b> — <see cref="AnonymousPermitLimit"/> request mỗi <see cref="WindowSeconds"/> giây, tính theo IP client.</description></item>
///   <item><description><b>Đã đăng nhập</b> — <see cref="AuthenticatedPermitLimit"/> request mỗi <see cref="WindowSeconds"/> giây, tính theo từng người dùng/thiết bị.</description></item>
/// </list>
///
/// Token sai hoặc hết hạn KHÔNG được tính là đã đăng nhập — nếu chỉ nhìn header <c>Authorization</c>
/// thì ai cũng gắn một chuỗi bất kỳ vào là nhảy lên bậc cao. Căn cứ duy nhất là
/// <c>HttpContext.User.Identity.IsAuthenticated</c>, tức là chữ ký token đã được xác thực.
///
/// Hạn mức này là lớp nền. Các policy chặt hơn theo endpoint (login, OTP, chat…) vẫn giữ nguyên và
/// chạy chồng lên: request phải qua được CẢ hai thì mới vào handler.
/// </remarks>
public class StandardRateLimitOptions
{
    public const string SectionName = "RateLimiting";

    /// <summary>Tắt hoàn toàn hạn mức nền. Chỉ dùng khi cần cô lập sự cố.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Hạn mức cho request chưa đăng nhập, tính theo IP client.</summary>
    public int AnonymousPermitLimit { get; set; } = 60;

    /// <summary>Hạn mức cho request đã đăng nhập, tính theo từng người dùng/thiết bị.</summary>
    public int AuthenticatedPermitLimit { get; set; } = 500;

    /// <summary>Độ dài cửa sổ tính hạn mức, đơn vị giây.</summary>
    public int WindowSeconds { get; set; } = 30;

    /// <summary>
    /// Đường dẫn được miễn hạn mức, so khớp phần đuôi của path (không phân biệt hoa thường).
    /// </summary>
    /// <remarks>
    /// Health check và metrics PHẢI được miễn. Docker healthcheck gọi <c>/health</c> mỗi 10 giây và
    /// Prometheus scrape <c>/metrics</c> đều đặn — cả hai đều là request chưa đăng nhập đi từ cùng một
    /// địa chỉ. Để chúng dùng chung hạn mức với traffic ẩn danh nghĩa là một đợt truy cập bình thường
    /// có thể làm health check trả 429, container bị đánh dấu unhealthy rồi khởi động lại — tự gây sự cố
    /// bằng chính cơ chế bảo vệ.
    /// </remarks>
    public string[] ExemptPathSuffixes { get; set; } = ["/health", "/live", "/ready", "/metrics"];

    /// <summary>Đường dẫn được miễn hạn mức khi chứa đoạn này (không phân biệt hoa thường).</summary>
    public string[] ExemptPathFragments { get; set; } = ["/swagger"];

    /// <summary>Miễn hạn mức cho lời gọi gRPC nội bộ giữa các service.</summary>
    /// <remarks>
    /// <c>BatteryInternalService</c> và <c>FileInternalGrpcService</c> đều KHÔNG gắn <c>[Authorize]</c> —
    /// chúng chỉ lắng nghe trên cổng nội bộ (8081), không expose ra host. Nếu tính chúng vào hạn mức,
    /// mọi lời gọi service-to-service sẽ rơi vào bậc ẩn danh và bị bóp ở 60 request mỗi 30 giây theo IP
    /// container gọi tới — tức là TicketService dựng một trang danh sách có thể tự làm nghẽn chính nó.
    /// Đây là kênh nội bộ, không phải bề mặt tấn công từ ngoài.
    /// </remarks>
    public bool ExemptGrpc { get; set; } = true;
}
