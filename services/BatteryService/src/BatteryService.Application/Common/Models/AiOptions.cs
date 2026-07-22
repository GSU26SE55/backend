namespace BatteryService.Application.Common.Models;

/// <summary>
/// BE-AI — config cho AI bridge (SohPredictionBackgroundService gọi AI module).
/// Transport: gRPC primary (<see cref="GrpcAddress"/>) → HTTP fallback (<see cref="HttpBaseUrl"/>).
/// <see cref="Enabled"/>=false → job no-op hoàn toàn (threshold rule vẫn chạy).
/// </summary>
public class AiOptions
{
    public const string SectionName = "Ai";

    /// <summary>Bật/tắt toàn bộ AI bridge. false → job không gọi AI, không insert prediction.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>gRPC endpoint (primary). VD "http://ai-module-grpc:50051" — insecure, nội bộ network.</summary>
    public string GrpcAddress { get; set; } = "http://localhost:50051";

    /// <summary>HTTP/FastAPI endpoint (fallback). VD "http://ai-module-http:8000".</summary>
    public string HttpBaseUrl { get; set; } = "http://localhost:8000";

    /// <summary>Timeout mỗi call AI (giây). SLA inference &lt; 100ms nên 5s là dư cho cả cold start.</summary>
    public int TimeoutSeconds { get; set; } = 5;

    /// <summary>Chu kỳ job gọi AI (phút). KHÔNG gọi mỗi reading (5s) — gom lại mỗi N phút.</summary>
    public int IntervalMinutes { get; set; } = 5;

    /// <summary>Số reading tối thiểu/pin để chạy 1 prediction (window model = 30).</summary>
    public int MinReadings { get; set; } = 30;

    /// <summary>Bật gọi /prescribe (enrich=true) khi Alert P1/P2. false → chỉ Predict.</summary>
    public bool PrescriptionEnabled { get; set; } = true;
}
