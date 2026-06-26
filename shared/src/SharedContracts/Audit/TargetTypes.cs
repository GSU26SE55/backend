namespace SharedContracts.Audit;

/// <summary>
/// Loại đối tượng bị tác động trong audit event (Sprint audit <c>#AUDIT-02</c>, Phụ lục B §B.2.1).
/// Fixed set — dùng cho <c>AuditCreatedEventV1.TargetType</c> + filter ở Audit Explorer.
/// </summary>
public static class TargetTypes
{
    // AuthService
    public const string Account = "Account";
    public const string Role = "Role";
    public const string Permission = "Permission";
    public const string Session = "Session";
    public const string RefreshToken = "RefreshToken";

    // BatteryService (+ Alert host trong BatteryService — D14)
    public const string Battery = "Battery";
    public const string BatteryAsset = "BatteryAsset";
    public const string SensorReading = "SensorReading";
    public const string ThresholdConfig = "ThresholdConfig";
    public const string Alert = "Alert";

    // TicketService
    public const string Ticket = "Ticket";
    public const string MaintenanceLog = "MaintenanceLog";
    public const string Comment = "Comment";
    public const string Attachment = "Attachment";

    // FileStorageService
    public const string File = "File";

    // Communication services
    public const string Email = "Email";
    public const string Notification = "Notification";
    public const string Sms = "Sms";

    // AI / Gateway
    public const string Model = "Model";
    public const string GatewayRoute = "GatewayRoute";

    // Audit itself (meta-audit, vd redact)
    public const string AuditRecord = "AuditRecord";

    /// <summary>Toàn bộ target type hợp lệ.</summary>
    public static readonly IReadOnlyCollection<string> All = new[]
    {
        Account, Role, Permission, Session, RefreshToken,
        Battery, BatteryAsset, SensorReading, ThresholdConfig, Alert,
        Ticket, MaintenanceLog, Comment, Attachment,
        File, Email, Notification, Sms, Model, GatewayRoute, AuditRecord,
    };
}
