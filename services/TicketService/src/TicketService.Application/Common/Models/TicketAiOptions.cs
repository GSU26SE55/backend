namespace TicketService.Application.Common.Models;

/// <summary>
/// Config cho verify + battery lookup của TicketService.
/// - <see cref="BatteryServiceBaseUrl"/>: gọi GET /api/battery-assets/{id} lấy serial (dùng JWT của request).
/// - <see cref="AiGrpcAddress"/>: gRPC AiService.VerifyTicket (insecure, nội bộ docker network).
/// - <see cref="Enabled"/>=false → consumer skip verify (ticket vẫn tạo, AiVerifyStatus=Skipped).
/// </summary>
public class TicketAiOptions
{
    public const string SectionName = "TicketAi";

    /// <summary>Bật/tắt AI verify async. false → consumer set Skipped, không gọi AI.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Base URL BatteryService (nội bộ network). VD "http://batteryservice:8080".</summary>
    public string BatteryServiceBaseUrl { get; set; } = "http://batteryservice:8080";

    /// <summary>gRPC endpoint AI module. VD "http://ai-module-grpc:50051".</summary>
    public string AiGrpcAddress { get; set; } = "http://ai-module-grpc:50051";

    /// <summary>
    /// gRPC endpoint BatteryService (đọc sensor pin verify). VD "http://batteryservice:8081".
    /// Nội bộ solar-net, KHÔNG JWT.
    /// </summary>
    public string BatteryGrpcAddress { get; set; } = "http://batteryservice:8081";

    /// <summary>Timeout mỗi call battery lookup / AI verify (giây).</summary>
    public int TimeoutSeconds { get; set; } = 5;

    /// <summary>Số candidate trùng tối đa gửi cho AI so mô tả.</summary>
    public int MaxDuplicateCandidates { get; set; } = 10;
}
