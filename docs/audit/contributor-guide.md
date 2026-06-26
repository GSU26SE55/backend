# Audit Contributor Guide (cheatsheet 1-page) — #AUDIT-45

Cách thêm audit cho 1 handler mới trong bất kỳ service nào (Hybrid Audit Architecture, ADR-0007).

## 1. Thêm action code
- Mở `shared/src/SharedContracts/Audit/ActionCodes.cs` → thêm const vào nested class của service.
- Action code: PascalCase, thì quá khứ (`BatteryCreated`, `LoginSucceeded`).

## 2. Publish audit trong command handler
```csharp
// Inject IPublisher (MediatR) vào handler.
await _publisher.Publish(
    XxxAuditTrailNotification.For(XxxAuditActionEnum.YourAction, targetId, targetDisplay: "..."),
    cancellationToken);  // TRƯỚC SaveChangesAsync — atomic với business data.
```
- Handler `XxxAuditTrailNotificationHandler` tự ghi `xxx_audit_logs` + `xxx_audit_outbox` cùng transaction.
- Relay `XxxAuditOutboxRelayBackgroundService` (Redis leader) publish `AuditCreatedEventV1` → exchange `audit.events`.
- `AuditAggregatorService` consume → `audit_aggregate` (idempotent theo event_id).

## 3. Quy tắc bất di bất dịch (Phụ lục B §B.0)
- `event_id` unique (idempotency). `OccurredAt` (lúc xảy ra) ≠ `RecordedAt` (lúc ghi).
- Append-only: trigger soft mode chặn DELETE + business-field UPDATE.
- Audit fail KHÔNG throw (không phá business flow).
- KHÔNG `DateTime.Now` (dùng `UtcNow`), KHÔNG `Console.WriteLine` (analyzer #AUDIT-04 chặn ở CI).

## 4. Option C — local endpoint
5 service có local endpoint (`/api/admin/{service}/audit-logs`, `[Authorize(Roles="Admin")]`): Auth, Battery, Ticket, File, Alert(host trong Battery). 5 service skip (Email/Notif/Sms/AI/Gateway) — chỉ qua Aggregator.

## 5. Cross-service query
`AuditAggregatorService` API (`[Authorize(Roles="Admin")]`, rate 200/min): `/api/admin/audit/search`, `/{eventId}`, `/correlation/{id}`, `/account/{id}/timeline`, `/stats`, `/export`, `/replay`, `/redact` (GDPR).

## 6. Decisions (2026-06-24)
MaxMind GeoLite2 · Redis leader election · SecurityOfficer gộp Admin · Alert host trong Battery · retention source 1y/aggregate 6mo/Critical+Security vĩnh viễn. Xem ADR-0007 Update log.
