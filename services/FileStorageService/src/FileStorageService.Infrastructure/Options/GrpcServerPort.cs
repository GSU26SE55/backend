using Microsoft.Extensions.Configuration;

namespace FileStorageService.Infrastructure.Options;

/// <summary>
/// GH-790 — cổng cho kênh gRPC nội bộ (voice transcription + quét virus đính kèm).
/// </summary>
/// <remarks>
/// <para>
/// Luật này quyết định service có khởi động được hay không, nhưng trước đây nằm thẳng trong
/// <c>Program.cs</c> dạng câu lệnh cấp cao nhất — tức không test nào chạm tới được. Hệ quả thực tế:
/// bản mẫu <c>env.prod.example</c> và Helm chart thiếu biến suốt một thời gian dài mà không có gì
/// báo, và triệu chứng chỉ hiện ra ở môi trường thật dưới dạng "service không lên".
/// </para>
/// <para>
/// Tách ra hàm thuần để chính luật đó có test, thay vì chỉ có một phép so chuỗi trong file cấu hình.
/// </para>
/// </remarks>
public static class GrpcServerPort
{
    /// <summary>Cổng HTTP của service — cổng gRPC không được trùng.</summary>
    public const int HttpPort = 8080;

    public const string PrimaryKey = "FILE_STORAGE_SERVICE_GRPC_SERVER_PORT";
    public const string FallbackKey = "Grpc:Port";

    /// <summary>
    /// Đọc cổng gRPC từ cấu hình, ném lỗi nói rõ nguyên nhân nếu thiếu hoặc không hợp lệ.
    /// </summary>
    public static int Resolve(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var port = configuration.GetValue<int?>(PrimaryKey)
                   ?? configuration.GetValue<int?>(FallbackKey)
                   ?? throw new InvalidOperationException(
                       $"{PrimaryKey} (or {FallbackKey}) must be configured.");

        if (port == HttpPort)
            throw new InvalidOperationException($"{FallbackKey} must differ from HTTP port {HttpPort}.");

        // Cổng 0 nghĩa là "hệ điều hành tự chọn" — service vẫn lên, nhưng địa chỉ mà TicketService
        // được cấu hình để gọi tới sẽ trỏ vào hư không. Hỏng kiểu đó im lặng hơn hẳn việc không lên.
        if (port <= 0 || port > 65535)
            throw new InvalidOperationException(
                $"{PrimaryKey} must be between 1 and 65535 (received {port}).");

        return port;
    }
}
