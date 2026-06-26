# Audit pipeline — RabbitMQ Topology (#AUDIT-05)

**Sprint audit Phase 0.** Định nghĩa topology cho audit event pipeline: mỗi service publish `AuditCreatedEventV1` → `AuditAggregatorService` consume → read-store `audit_aggregate`.

> Tham chiếu: ADR-0007 §3 (Hybrid Architecture) + Phụ lục A §A.3. Event contract: `SharedContracts.Events.Audit.AuditCreatedEventV1` (#AUDIT-01).

---

## 1. Sơ đồ

```
[AuthService]    ─┐
[BatteryService] ─┤  publish AuditCreatedEventV1
[TicketService]  ─┤  routing key: audit.{service}.{category}.{severity}
[FileStorage]    ─┤
[Email/Notif/Sms]─┘
        │
        ▼
  exchange  audit.events  (type: topic, durable)
        │  binding: audit.#
        ▼
  queue  aggregator.audit.events  (durable, x-max-length=1,000,000, x-message-ttl=7d, x-dead-letter→DLX)
        │
        ▼
  [AuditAggregatorService]  AuditCreatedConsumer (#AUDIT-15)
        │  fail sau retry → 
        ▼
  exchange  audit.events.dlx  (fanout, durable) → queue  aggregator.audit.events.dlq  (durable)
```

## 2. Thông số

| Thành phần | Tên | Loại | Tham số |
|------------|-----|------|---------|
| Exchange chính | `audit.events` | topic, durable | — |
| Queue consumer | `aggregator.audit.events` | durable | `x-max-length=1000000`, `x-message-ttl=604800000` (7d), `x-overflow=reject-publish`, `x-dead-letter-exchange=audit.events.dlx` |
| DLX | `audit.events.dlx` | fanout, durable | — |
| DLQ | `aggregator.audit.events.dlq` | durable | — |
| Binding | `audit.events` → queue | — | routing key pattern `audit.#` |

## 3. Routing key convention

```
audit.{service}.{category}.{severity}
```

- `service`: lowercase service name — `auth`, `battery`, `ticket`, `file`, `email`, `notification`, `sms`, `ai`, `gateway`.
- `category`: lowercase của `AuditCategories` (#AUDIT-02) — `authentication`, `authorization`, `accountmanagement`, `datamodification`, `dataaccess`, `configuration`, `security`, `communication`, `system`.
- `severity`: lowercase của `Severities` — `info`, `warning`, `critical`, `security`.

Ví dụ: `audit.auth.authentication.warning` (login fail), `audit.battery.configuration.critical` (threshold change nguy hiểm).

> Topic exchange cho phép thêm consumer chuyên biệt sau này KHÔNG cần sửa producer — vd `SecurityAlertService` bind `audit.#.security.*` hoặc `audit.#.#.security` để chỉ nhận event Security (Phụ lục B §B.5).

## 4. Cấu hình MassTransit (tham khảo — wiring thực ở #AUDIT-08 producer + #AUDIT-15 consumer)

- **Producer** (mỗi service, `*AuditOutboxRelayBackgroundService`): publish `AuditCreatedEventV1` lên exchange `audit.events` với routing key build theo convention §3.
- **Consumer** (`AuditAggregatorService`): bind `aggregator.audit.events` vào `audit.events` với pattern `audit.#`. Retry policy: 3 lần exponential (1s/5s/15s) → DLQ. Idempotent INSERT ON CONFLICT (event_id) DO NOTHING (#AUDIT-15).
- **DLQ replay**: ops runbook `docs/audit/operations-runbook.md` (#AUDIT-45) — shovel DLQ → main queue sau khi fix.

## 5. Monitoring (#AUDIT-44)

- `audit_outbox_pending_total{service}` — gauge, alert nếu > 1000 trong 5 phút (relay stuck / RabbitMQ down).
- `audit_consumer_lag_seconds` — histogram, SLO p99 < 10s.
- `audit_dlq_size_total` — gauge, alert nếu DLQ > 100.
