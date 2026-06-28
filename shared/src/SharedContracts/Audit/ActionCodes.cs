namespace SharedContracts.Audit;

/// <summary>
/// Catalog tập trung <b>TẤT CẢ</b> action code của hệ thống audit (Sprint audit <c>#AUDIT-02</c>).
///
/// <para><b>Quy ước (Phụ lục B §B.2.1):</b> action code mới phải PR vào file này trước khi dùng ở handler
/// (single source of truth — auto-gen registry <c>action-code-registry.md</c> ở <c>#AUDIT-45</c>). Đặt tên
/// <c>PascalCase</c> ở thì quá khứ (đã xảy ra), vd <c>BatteryCreated</c>, <c>LoginSucceeded</c>.</para>
///
/// Nhóm theo service. Giá trị string = tên action (KHÔNG prefix service — service đã có ở cột <c>service_name</c>).
/// </summary>
public static class ActionCodes
{
    /// <summary>AuthService — account/role/permission/session/OTP/2FA/invite (<c>#AUDIT-11</c> fix 22 handler).</summary>
    public static class Auth
    {
        public const string LoginSucceeded = "LoginSucceeded";
        public const string LoginFailedInvalidCredentials = "LoginFailedInvalidCredentials";
        public const string LoginFailedLocked = "LoginFailedLocked";
        public const string LogoutSucceeded = "LogoutSucceeded";
        public const string TokenRefreshed = "TokenRefreshed";
        public const string RefreshTokenRevoked = "RefreshTokenRevoked";
        public const string AccountRegistered = "AccountRegistered";
        public const string AccountActivated = "AccountActivated";
        public const string AccountLocked = "AccountLocked";
        public const string AccountUnlocked = "AccountUnlocked";
        public const string AccountStatusChanged = "AccountStatusChanged";
        public const string AccountMerged = "AccountMerged";
        public const string AccountDeleted = "AccountDeleted";
        public const string PasswordChanged = "PasswordChanged";
        public const string PasswordResetRequested = "PasswordResetRequested";
        public const string PasswordResetCompleted = "PasswordResetCompleted";
        public const string OtpSent = "OtpSent";
        public const string OtpVerified = "OtpVerified";
        public const string OtpVerificationFailed = "OtpVerificationFailed";
        public const string TwoFactorEnabled = "TwoFactorEnabled";
        public const string TwoFactorDisabled = "TwoFactorDisabled";
        public const string TwoFactorReset = "TwoFactorReset";
        public const string RoleAssigned = "RoleAssigned";
        public const string RoleUnassigned = "RoleUnassigned";
        public const string RoleCreated = "RoleCreated";
        public const string RoleUpdated = "RoleUpdated";
        public const string PermissionGranted = "PermissionGranted";
        public const string PermissionRevoked = "PermissionRevoked";
        public const string InviteSent = "InviteSent";
        public const string InviteAccepted = "InviteAccepted";
    }

    /// <summary>BatteryService — 12 action (<c>#AUDIT-20</c>).</summary>
    public static class Battery
    {
        public const string BatteryCreated = "BatteryCreated";
        public const string BatteryUpdated = "BatteryUpdated";
        public const string BatteryDeleted = "BatteryDeleted";
        public const string AssignedToCustomer = "AssignedToCustomer";
        public const string UnassignedFromCustomer = "UnassignedFromCustomer";
        public const string ThresholdConfigChanged = "ThresholdConfigChanged";
        public const string SensorReadingEdited = "SensorReadingEdited";
        public const string AlertAcknowledged = "AlertAcknowledged";
        public const string AlertSuppressed = "AlertSuppressed";
        public const string StatusChanged = "StatusChanged";
        public const string MaintenanceLogged = "MaintenanceLogged";
        public const string CalibrationApplied = "CalibrationApplied";
    }

    /// <summary>Alert (host trong BatteryService — D14) — 5 action (<c>#AUDIT-31</c>).</summary>
    public static class Alert
    {
        public const string AlertAcknowledged = "AlertAcknowledged";
        public const string AlertSuppressed = "AlertSuppressed";
        public const string AlertRuleChanged = "AlertRuleChanged";
        public const string AlertSeverityOverridden = "AlertSeverityOverridden";
        public const string AlertManuallyResolved = "AlertManuallyResolved";
    }

    /// <summary>TicketService — 21 action (<c>#AUDIT-24</c>).</summary>
    public static class Ticket
    {
        public const string TicketCreated = "TicketCreated";
        public const string StateTransitioned = "StateTransitioned";
        public const string PriorityChanged = "PriorityChanged";
        public const string AssignedToStaff = "AssignedToStaff";
        public const string UnassignedFromStaff = "UnassignedFromStaff";
        public const string SlaPaused = "SlaPaused";
        public const string SlaResumed = "SlaResumed";
        public const string SlaBreached = "SlaBreached";
        public const string EscalatedToManager = "EscalatedToManager";
        public const string EscalatedToAdmin = "EscalatedToAdmin";
        public const string MaintenanceLogAdded = "MaintenanceLogAdded";
        public const string CommentAdded = "CommentAdded";
        public const string AttachmentUploaded = "AttachmentUploaded";
        public const string AttachmentDeleted = "AttachmentDeleted";
        public const string ResolutionAdded = "ResolutionAdded";
        public const string ClosedByUser = "ClosedByUser";
        public const string ReopenedByAdmin = "ReopenedByAdmin";
        public const string RejectedByManager = "RejectedByManager";
        public const string FalseAlarmMarked = "FalseAlarmMarked";
        public const string CustomerRated = "CustomerRated";
        public const string AutoCreatedFromAnomaly = "AutoCreatedFromAnomaly";
    }

    /// <summary>FileStorageService — 6 action (<c>#AUDIT-29</c>).</summary>
    public static class File
    {
        public const string FileUploaded = "FileUploaded";
        public const string FileDownloaded = "FileDownloaded";
        public const string FileDeleted = "FileDeleted";
        public const string AccessDenied = "AccessDenied";
        public const string PresignedUrlGenerated = "PresignedUrlGenerated";
        public const string PresignedUrlRevoked = "PresignedUrlRevoked";
    }

    /// <summary>EmailService — 5 action (<c>#AUDIT-33</c>).</summary>
    public static class Email
    {
        public const string EmailSent = "EmailSent";
        public const string EmailFailed = "EmailFailed";
        public const string EmailBounced = "EmailBounced";
        public const string EmailDeferred = "EmailDeferred";
        public const string EmailRejected = "EmailRejected";
    }

    /// <summary>NotificationService — 7 action (<c>#AUDIT-34</c>).</summary>
    public static class Notification
    {
        public const string PushSent = "PushSent";
        public const string PushFailed = "PushFailed";
        public const string PushDelivered = "PushDelivered";
        public const string PushOpened = "PushOpened";
        public const string InAppCreated = "InAppCreated";
        public const string InAppRead = "InAppRead";
        public const string InAppDismissed = "InAppDismissed";
    }

    /// <summary>SmsService — 3 action bổ sung (<c>#AUDIT-35</c>).</summary>
    public static class Sms
    {
        public const string SmsForwarded = "SmsForwarded";
        public const string SmsRoutingRuleChanged = "SmsRoutingRuleChanged";
        public const string SmsGatewayHealthCheckFailed = "SmsGatewayHealthCheckFailed";
    }

    /// <summary>AI Module — 5 action (<c>#AUDIT-35</c>).</summary>
    public static class Ai
    {
        public const string ModelInferenceCalled = "ModelInferenceCalled";
        public const string ModelInferenceFailed = "ModelInferenceFailed";
        public const string AnomalyClassified = "AnomalyClassified";
        public const string TrainingDataIngested = "TrainingDataIngested";
        public const string ModelVersionPromoted = "ModelVersionPromoted";
    }

    /// <summary>ApiGateway — 3 action (<c>#AUDIT-35</c>).</summary>
    public static class Gateway
    {
        public const string RequestRouted = "RequestRouted";
        public const string RateLimitHit = "RateLimitHit";
        public const string AuthorizationDenied = "AuthorizationDenied";
    }

    /// <summary>AuditAggregatorService — meta-audit (<c>#AUDIT-42</c>).</summary>
    public static class Audit
    {
        public const string AccountDataRedacted = "AccountDataRedacted";
        public const string AuditExported = "AuditExported";
        public const string AuditReplayed = "AuditReplayed";
    }
}
