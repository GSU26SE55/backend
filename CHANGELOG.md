# Changelog

Tuân theo [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) format.
Versions tuân theo [SemVer](https://semver.org/spec/v2.0.0.html).

## [1.5.0] — 2026-07-26 (Sprint 5B)

### Added — Alert–Ticket Saga (BR P0 release gate)

- **#236** Saga contracts (`SharedContracts/Saga/AlertTicket/*`): 7 records + `BatteryAnomalyDetectedV2Event`.
- **#236** TicketService migration `AddAlertTicketSagaFoundation` — bảng `alert_ticket_saga_states`,
  unique filtered index `ux_tickets_origin_alert_id`, partial unique guard
  `ux_tickets_active_auto_per_asset_category`.
- **#236** BatteryService migration `AddAlertTicketLinkIndex` — non-unique filtered
  index `ix_alerts_ticket_id_filtered`.
- **#235** TicketService migration `AddQuartzPersistenceSchema` — 11 bảng `qrtz_*`
  theo official Quartz.NET PostgreSQL DDL.
- **#237** `AlertTicketSagaStateMachine` (MassTransit) — state machine
  Initial→TicketRequested→TicketProvisioned→AlertLinkRequested→Completed/Failed,
  PostgreSQL `xmin` optimistic concurrency, persistent Quartz timeout, bounded retry.
- **#238** Saga participants: `CreateTicketFromAlertConsumer` (TicketService),
  `LinkAlertToTicketConsumer` (BatteryService).
- **#238** `BatteryAlertEscalationRequestedEvent` tách khỏi `BatteryAnomalyDetectedEvent`.
- **#238** NotificationService consumers: `BatteryAlertEscalationRequestedConsumer`,
  `AlertTicketSagaFailedConsumer` + email templates + 2 enum value
  (`BatteryAlertEscalationPending=16`, `AlertTicketSagaFailed=17`).
- **#238** Feature flags `AlertTicketDispatchEnabled` + `AlertTicketSagaEnabled` cho cutover.
- **#239** Admin endpoints `/api/v1/admin/sagas/alert-ticket{,/{alertId}{,/reprocess}}`
  với `Idempotency-Key` requirement cho reprocess.
- **#239** `/api/ticket/health/saga` endpoint.
- **#239** 8 Prometheus metrics: `saga_alert_ticket_started/completed/failed/active/duration/timeout/redelivery/reprocessed`.
- **#239** 3 runbooks (`docs/runbooks/{08-saga-failed,09-saga-stuck,10-saga-duplicate-canonical}.md`).
- **#239** ADR-018: Orchestrated Alert–Ticket Saga.
- **#241** AuthService data migration `SeedSagaPermissions` — seed
  `ticket.saga.view` (Admin + Manager) + `ticket.saga.reprocess` (Admin only).
- **#241** `PermissionsChangedEvent` for cross-service cache invalidation.

### Added — Messaging hardening (#235)

- Tách `IIntegrationEventOutboxWriter` (in-transaction write) khỏi
  `IIntegrationEventTransport` (post-commit publish) — DI split.
- NuGet packages: `MassTransit.EntityFrameworkCore` 8.4.1, `MassTransit.Quartz` 8.4.1,
  `Quartz.AspNetCore` 3.14.0, `Quartz.Extensions.Hosting` 3.14.0, `Quartz.Serialization.Json` 3.14.0.

### Changed

- **#233** Battery scope cleanup — Energy/CO2 analytics loại bỏ permanent. ADR-017 merged.
- **#233** Pre-commit hook `energy-co2-scope-guard` thêm vào `.pre-commit-config.yaml`.
- **#234** BatteryService entity `Site` bỏ field `CapacityKw` + DTO + validation
  + seed + Mapper + Controller XML docs. Migration `RemoveSiteCapacityKw` (Up/Down + rollback).
- **#238** `BatteryAnomalyDetectedConsumer` (TicketService) mark `[Obsolete]` —
  Saga state machine giờ handle anomaly events.

### Deprecated

- `BatteryAnomalyDetectedConsumer` (TicketService) — sẽ remove ở Sprint 6 sau khi
  Saga stable, không có rollback nào trong window cutover.

### Migration order (Sprint 5B — bắt buộc tuần tự)

1. BatteryService `RemoveSiteCapacityKw` (`#234`)
2. BatteryService + TicketService `AddDurableMessagingFoundation` (deferred — pending)
3. TicketService `AddQuartzPersistenceSchema` (`#235`)
4. Preflight data cleanup (runbook `10-saga-duplicate-canonical.md`)
5. TicketService `AddAlertTicketSagaFoundation` (`#236`)
6. BatteryService `AddAlertTicketLinkIndex` (`#236`)
7. AuthService `SeedSagaPermissions` (`#241`) — khác DB, có thể chạy song song.

## [1.4.0] — 2026-07-19 (Sprint 5)

- Ticket SLA timer + escalation flow (`#150`/`#151`).

## [1.0.0] — 2026-05-24 (Sprint 1)

- Khởi tạo monorepo + base services.

[1.5.0]: https://github.com/GSU26SE55/backend/releases/tag/v1.5.0
[1.4.0]: https://github.com/GSU26SE55/backend/releases/tag/v1.4.0
[1.0.0]: https://github.com/GSU26SE55/backend/releases/tag/v1.0.0
