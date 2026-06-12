# ADR 0018: Orchestrated Alert–Ticket Saga

Status: Accepted (Sprint 5B — 2026-07-22).

## Context

Sprint 4 implement direct `BatteryAnomalyDetectedConsumer` ở TicketService consume event
`BatteryAnomalyDetectedEvent` từ BatteryService → tạo Ticket → publish `TicketCreatedIntegrationEvent`.
BatteryService không có consumer ngược lại để update `Alert.TicketId` → tồn tại data drift:
**Alert chưa link sang Ticket** dù Ticket đã tồn tại.

Production scenarios gây drift:
- Ticket create thành công nhưng `Alert.TicketId` vẫn null.
- Redelivery (RabbitMQ at-least-once) → 2 Ticket cho 1 Alert.
- TicketService restart giữa transaction → state lost.

## Decision

Triển khai **Orchestrated Saga** dùng MassTransit state machine với persistent state
(EF + PostgreSQL) + Quartz scheduler cho timeout.

State machine: `Initial → TicketRequested → TicketProvisioned → AlertLinkRequested → Completed`.
Terminal: `Failed` với reason + failedAtStage.

### Lý do chọn Orchestrated (vs Choreographed)

| Tiêu chí | Orchestrated | Choreographed |
|----------|--------------|---------------|
| Workflow rõ ràng | ✅ State machine 1 file | ❌ Scatter qua consumers |
| Timeout / retry | ✅ Quartz scheduler | ❌ Per-consumer ad-hoc |
| Admin reprocess | ✅ State + transition explicit | ❌ Khó retry chính xác |
| Observability | ✅ 8 metric + 1 dashboard | ❌ Cross-service log correlation phức tạp |

### Lý do chọn MassTransit state machine (vs custom)

- Saga + EF + Quartz integration sẵn có (MassTransit.EntityFrameworkCore, MassTransit.Quartz).
- PostgreSQL `xmin` optimistic concurrency built-in.
- TestHarness cho unit test state machine.
- Battle-tested ở production scale.

### Cấu phần

- **Contracts** (8 records) — `SharedContracts/Saga/AlertTicket/`.
- **State entity + EF config** — `TicketService.Infrastructure/Sagas/` + `Persistence/Configurations/`.
- **Migration** `AddAlertTicketSagaFoundation` + `AddQuartzPersistenceSchema` + `AddAlertTicketLinkIndex`.
- **Activities** — Send/Publish wrappers cho compensating actions.
- **Consumers** — `CreateTicketFromAlertConsumer` (TicketService) + `LinkAlertToTicketConsumer` (BatteryService).
- **Admin endpoints** — `GET /api/v1/admin/sagas/alert-ticket{,/{alertId}}` + `POST .../{alertId}/reprocess`.
- **Metrics** — `saga_alert_ticket_started/completed/failed/active/duration/...`.
- **Runbook** — `08-saga-failed.md`, `09-saga-stuck.md`, `10-saga-duplicate-canonical.md`.

## Consequences

**Positive:**
- Workflow rõ ràng, audit được, replayable.
- Forward recovery (Saga reprocess) không cần manual DB hack.
- Idempotency built-in qua `OriginAlertId` unique filtered index + Saga state tombstone.

**Negative / accepted trade-offs:**
- Bus factor (Saga code phức tạp, Sprint 5B chỉ Thắng nắm) — mitigated bằng walkthrough video + runbook.
- Thêm Quartz scheduler dependency (11 bảng `qrtz_*`).
- Cutover risk: direct consumer + Saga chạy song song → mitigated bằng feature flag `AlertTicketSagaEnabled`.

## Implementation refs

- Sprint 5B `#236` — Contracts + migration.
- Sprint 5B `#237` — State machine.
- Sprint 5B `#238` — Participants + NotificationService delta.
- Sprint 5B `#239` — Verification + ops + ADR-018.

## References

- `overall.md` §8.1–§8.3, §53.4–§53.12, §18.2bis (PR review checklist).
- ADR-017 (Remove Energy/CO2) — shares Sprint 5B release gate.
- MassTransit Sagas docs: https://masstransit.io/documentation/patterns/saga
- Quartz.NET PostgreSQL setup: https://github.com/quartznet/quartznet/blob/main/database/tables/tables_postgres.sql
