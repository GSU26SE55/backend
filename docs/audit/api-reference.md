# Audit API Reference (#AUDIT-45)

REST API của `AuditAggregatorService` (cross-service Audit Explorer) + local endpoint Option C.
Auth: tất cả **`[Authorize(Roles = "Admin")]`** (SecurityOfficer gộp Admin — D13). Aggregator rate-limit 200 req/min.

## AuditAggregatorService — `/api/admin/audit/*`

| Method | Path | Query / Body | Response |
|--------|------|--------------|----------|
| GET | `/api/admin/audit/search` | `service, action, category, severity, actorId, targetId, correlationId, isSuccess, from, to, pageNumber, pageSize(≤100)` | `CommonResponse<PaginationResponse<AuditAggregateDto>>` |
| GET | `/api/admin/audit/{eventId}` | path `eventId` | `CommonResponse<AuditAggregateDto>` |
| GET | `/api/admin/audit/correlation/{correlationId}` | path | `CommonResponse<List<AuditAggregateDto>>` (ordered occurred_at) |
| GET | `/api/admin/audit/account/{accountId}/timeline` | `limit` (≤500, default 100) | `CommonResponse<List<AuditAggregateDto>>` (actor OR target = accountId) |
| GET | `/api/admin/audit/stats` | `from, to, groupBy=service\|action\|severity` | `CommonResponse<List<AuditStatsItemDto>>` |
| GET | `/api/admin/audit/export` | `format=csv\|json` + search filters | Streaming download (no-OOM, IAsyncEnumerable) |
| POST | `/api/admin/audit/redact` | `accountId` | `CommonResponse<object>` — GDPR redact PII (#AUDIT-42), KHÔNG xóa row |
| POST | `/api/admin/audit/replay` | `service, from, to` | `202 Accepted` — re-ingestion từ source-of-truth |

### Health (k8s — #AUDIT-18)
`GET /live` · `GET /ready` · `GET /health`

## Local endpoints (Option C — 5 service, fallback resilience)

| Service | Path | Filter |
|---------|------|--------|
| AuthService | `/api/admin/audit-logs` (2 endpoint sẵn) | — |
| BatteryService | `/api/admin/battery/audit-logs` | action, batteryId, from, to, page |
| BatteryService (Alert, D14) | `/api/admin/alerts/audit-logs` | action, alertId, from, to, page |
| TicketService | `/api/admin/ticket/audit-logs` | action, ticketId, from, to, page |
| FileStorageService | `/api/admin/files/audit-logs` | action, fileId, from, to, page |

> 5 service KHÔNG có local endpoint (Email/Notification/Sms/AI/Gateway) — query qua aggregator.

## DTO `AuditAggregateDto` (rút gọn)
`id, eventId, serviceName, actionCode, actionCategory, severity, targetType, targetId, targetDisplay, actorAccountId, actorRole, actorDisplay, actorIp, isSuccess, errorCode, reason, metadataJson, correlationId, causationId, occurredAt, recordedAt, geoCountry, geoCity`

> Guid → string trong DTO (convention repo). PII redacted → `[REDACTED]` sau khi gọi `/redact`.
