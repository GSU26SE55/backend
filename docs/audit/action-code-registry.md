# Audit Action Code Registry (#AUDIT-45)

Danh sách action code chuẩn của Hybrid Audit. Source-of-truth: `shared/src/SharedContracts/Audit/ActionCodes.cs`.
Action mới phải PR vào file đó trước khi dùng ở handler.

## Quy ước
- PascalCase, thì quá khứ (đã xảy ra): `BatteryCreated`, `LoginSucceeded`.
- 9 category: Authentication, Authorization, AccountManagement, DataModification, DataAccess, Configuration, Security, Communication, System.
- 4 severity: Info, Warning, Critical, Security.

## AuthService (auth_audit_logs)
LoginSucceeded · LoginFailedInvalidCredentials · LoginFailedLocked · LogoutSucceeded · TokenRefreshed · RefreshTokenRevoked · AccountRegistered · AccountActivated · AccountLocked · AccountUnlocked · AccountStatusChanged · AccountMerged · AccountDeleted · AccountCreatedByAdmin · AccountUpdated · AccountDeactivated · PasswordChanged · PasswordReset* · Otp* · TwoFactorEnabled/Disabled/Reset · RoleCreated/Updated/Deleted/Assigned/Unassigned · RoleStatusChanged · PermissionGranted/Revoked · SessionRevoked · AllSessionsRevoked · GoogleLinked/Unlinked · EmailChangeRequested/Confirmed · PhoneVerified · InviteSent/Accepted
> Wire qua `AuditTrailNotification` (#AUDIT-09/11) — 19 handler.

## BatteryService (battery_audit_logs) — `BatteryAuditActionEnum`
BatteryCreated · BatteryUpdated · BatteryDeleted · AssignedToCustomer · UnassignedFromCustomer · ThresholdConfigChanged · SensorReadingEdited · AlertAcknowledged · AlertSuppressed · StatusChanged · MaintenanceLogged · CalibrationApplied

## Alert (host trong BatteryService — D14) — `AlertAuditActionEnum`
AlertAcknowledged · AlertSuppressed · AlertRuleChanged · AlertSeverityOverridden · AlertManuallyResolved

## TicketService (ticket_audit_logs) — `TicketAuditActionEnum` (21)
TicketCreated · StateTransitioned · PriorityChanged · AssignedToStaff · UnassignedFromStaff · SlaPaused · SlaResumed · SlaBreached · EscalatedToManager · EscalatedToAdmin · MaintenanceLogAdded · CommentAdded · AttachmentUploaded · AttachmentDeleted · ResolutionAdded · ClosedByUser · ReopenedByAdmin · RejectedByManager · FalseAlarmMarked · CustomerRated · AutoCreatedFromAnomaly
> AutoCreatedFromAnomaly có `causation_id = OriginAlertId` (#AUDIT-27).

## FileStorageService (file_audit_logs) — `FileAuditActionEnum`
FileUploaded · FileDownloaded · FileDeleted · AccessDenied · PresignedUrlGenerated · PresignedUrlRevoked

## NotificationService (notification_audit_logs) — `NotificationAuditActionEnum`
PushSent · PushFailed · PushDelivered · PushOpened · InAppCreated · InAppRead · InAppDismissed

## SmsService (sms_audit_logs + sms_audit_outbox) — `SmsAuditEvent` (+3 #AUDIT-35)
SmsForwarded · SmsRoutingRuleChanged · SmsGatewayHealthCheckFailed *(+ Queued/Picked/Sent/Failed/Retry/Cancelled/Reaped/Redacted có sẵn)*

## AuditAggregatorService (meta-audit)
AccountDataRedacted · AuditExported · AuditReplayed

> **Auto-gen note:** registry này có thể regenerate từ `ActionCodes.cs` + các `*AuditActionEnum` bằng reflection/script (Phase 7 enhancement).
