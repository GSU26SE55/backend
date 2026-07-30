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

## NotificationService (notification_audit_logs) — `NotificationAuditActionEnum` (7 + 1)
PushSent · PushFailed · PushDelivered · PushOpened · InAppCreated · InAppRead · InAppDismissed · **TemplateTestSent** *(Sprint 6.3 NOTI3-12 / #712)*

> **Sprint 6.2 NOTI-13 (#684) — 7 action gốc NAY MỚI THẬT SỰ ĐƯỢC GHI.** Trước đó hạ tầng đã dựng đủ
> (bảng 14 cột + `notification_audit_outbox` + relay leader-election) nhưng **không dòng code nào tạo
> record**: enum chưa từng được dùng và relay poll bảng rỗng 2 giây/lần vĩnh viễn. `INotificationAuditWriter`
> khép lại khoảng trống đó.
>
> Nơi ghi thật sự — 6/8 action có caller:
>
> | Action | Ghi bởi | Severity |
> |---|---|---|
> | `PushSent` | `NotificationDispatcher` sau khi giao push | `Info` (thành công) / `Warning` |
> | `PushFailed` | `NotificationDispatcher` khi push thất bại vĩnh viễn | `Warning` |
> | `PushDelivered` | `ExpoReceiptReconcileBackgroundService` khi receipt Expo trả `ok` *(6.3 NOTI3-02/14)* | `Info` |
> | `PushOpened` | `PATCH /api/notifications/{id}/opened` *(6.3 NOTI3-14)* | `Info` |
> | `InAppCreated` | `NotificationDispatcher` sau khi giao record InApp | `Info` |
> | `InAppRead` | `PATCH /api/notifications/{id}/read` | `Info` |
> | `InAppDismissed` | *(khai báo, chưa có caller)* | `Info` |
> | `TemplateTestSent` | `POST /api/admin/notification-templates/{id}/test-send` | **`Warning`** — gửi email thật từ domain hệ thống là hành động cần nổi lên trong bộ lọc audit, không nên lẫn vào nhiễu `Info` |
>
> Toàn bộ dùng category `Communication`, `TargetType = Notification`, `TargetId = NotificationId`.
> `ActorAccountId` = **user nhận** (notification do hệ thống phát, không có người thao tác) —
> `null` nếu `Guid.Empty`. Kênh **Email/Sms KHÔNG ghi audit** (không nằm trong 7 action của #AUDIT-34).
> Lỗi ghi audit **không bao giờ** làm hỏng luồng gửi (writer nuốt exception).

## SmsService (sms_audit_logs + sms_audit_outbox) — `SmsAuditEvent` (+3 #AUDIT-35)
SmsForwarded · SmsRoutingRuleChanged · SmsGatewayHealthCheckFailed *(+ Queued/Picked/Sent/Failed/Retry/Cancelled/Reaped/Redacted có sẵn)*

## AuditAggregatorService (meta-audit)
AccountDataRedacted · AuditExported · AuditReplayed

> **Auto-gen note:** registry này có thể regenerate từ `ActionCodes.cs` + các `*AuditActionEnum` bằng reflection/script (Phase 7 enhancement).
