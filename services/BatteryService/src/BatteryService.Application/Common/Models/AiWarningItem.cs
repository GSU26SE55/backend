namespace BatteryService.Application.Common.Models;

/// <summary>
/// GH-805 — một cảnh báo trong <c>warnings[]</c> của AI /predict (proto <c>WarningItem</c>).
/// Transport-neutral: gRPC và HTTP client cùng map về type này.
///
/// Field map (xem <c>Protos/ai_service.proto:80-84</c>):
///   Code     ← code     — VD "TEMP_CRITICAL", "VOLTAGE_LOW", "SOH_LOW"
///   Severity ← severity — "warning" | "critical"
///   Message  ← message
///
/// ⚠️ <see cref="Severity"/> ở đây là severity của CẢNH BÁO (chuỗi của AI), KHÔNG phải
/// <c>AlertSeverityEnum</c> của domain BE.
/// </summary>
public class AiWarningItem
{
    public AiWarningItem(string? Code, string? Severity, string? Message)
    {
        this.Code = Code;
        this.Severity = Severity;
        this.Message = Message;
    }

    public string? Code { get; }
    public string? Severity { get; }
    public string? Message { get; }
}
