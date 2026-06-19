# Sprint audit — Pre-implementation Checklist §B.12

**Status:** Signed off (2026-06-19).
**Tham chiếu:** `issue-authservice.md` Phụ lục B §B.12.
**Scope:** Toàn bộ 45 task `#AUDIT-01..45` (GitHub `#447..#491`).

---

## Context — Capstone scope override

Phụ lục B §B.12 spec yêu cầu **3 thành viên team** đọc + ký xác nhận checklist này trước khi start code.

**Capstone scope reality:**

- Sprint audit có **1 developer duy nhất**: Thắng (`@Alexdev257`).
- Toàn bộ 45 task assignee: `@Alexdev257`.
- Reviewer 1 + Reviewer 2 không có người khác trong team capstone scope cho sprint này.

→ Thắng **tự sign-off** với vai trò sole developer, **chịu toàn bộ trách nhiệm** cho decisions trong sprint. GVHD (Trương Long) review khi báo cáo capstone final.

---

## Checklist nội dung MANDATORY đọc

### Phụ lục A — Kiến trúc & roadmap

- [x] **§A.1** Tổng quan Hybrid Architecture (decentralized write + Outbox + centralized read)
- [x] **§A.2** Schema chuẩn 14 cột (event_id, service_name, action_code, category, severity, target, actor, result, correlation, time)
- [x] **§A.3** Outbox pattern + RabbitMQ topology (`audit.events` topic exchange + DLQ)
- [x] **§A.4** AuditAggregatorService scaffold (Clean Architecture + Postgres partitioned + Geo IP enrichment)
- [x] **§A.5** API endpoints (7 endpoint search/eventId/correlation/timeline/stats/export/replay) + §A.5.1.bis Option C policy (5 service có local endpoint, 5 service skip)
- [x] **§A.6** Owner mapping per phase (sole developer Thắng)
- [x] **§A.7** Retention policy (source 1 năm / aggregate 6 tháng / Critical+Security vĩnh viễn)
- [x] **§A.8** Security & PII handling (append-only, GDPR redaction, SecurityOfficer role)
- [x] **§A.9** Migration zero-downtime (5-step pattern)
- [x] **§A.10** Testing strategy (unit + integration TestContainers + idempotency + partition + causation chain + perf 1000 ev/s + chaos)
- [x] **§A.11** Documentation deliverables (7 docs: ADR + contributor guide + action registry + API ref + ops runbook + security + monitoring)
- [x] **§A.12** Effort estimation (~44 dev-day, 7 phase)

### Phụ lục B — Implementation playbook

- [x] **§B.0** **10 nguyên tắc bất di bất dịch:**
  1. Source-of-truth = mỗi service local table, aggregator là materialized view
  2. Write atomic per service (audit_log + outbox cùng transaction)
  3. `event_id = Guid.CreateVersion7()` (time-sortable, KHÔNG dùng Random)
  4. Idempotency consumer (INSERT ON CONFLICT (event_id) DO NOTHING)
  5. Append-only trigger DB (soft mode cho phép update outbox-related)
  6. `correlation_id` propagate qua MassTransit header (từ AUTH-77)
  7. `causation_id` = parent event_id khi cross-service consume (vd ticket auto-tạo từ anomaly)
  8. Retention partition by month, drop cũ EXCEPT Critical/Security
  9. PII redaction qua endpoint dedicated, KHÔNG xóa row (giữ metadata cho audit trail)
  10. `OccurredAt` (handler set) ≠ `RecordedAt` (DB DEFAULT now()) — track clock skew

- [x] **§B.1** Naming conventions (Entity / Notification / Handler / OutboxRelay / Migration)
- [x] **§B.2** Schema chuẩn per entity (B.2.1 action codes catalog, B.2.2 categories, B.2.3 severities)
- [x] **§B.3** Outbox table schema (event_id unique, status enum, retry_count, last_error)
- [x] **§B.4** Correlation + causation propagation chi tiết (`CorrelationIdMiddleware` AUTH-77 + manual chain ở consumer)
- [x] **§B.5** Geo IP enrichment (LRU cache 10k entry TTL 1h, fallback null)
- [x] **§B.6** API endpoint contract (query params, pagination max 100/page, response shape)
- [x] **§B.7** Authorization model (SecurityOfficer role mới, permission claim)
- [x] **§B.8** Performance SLO (outbox lag p99 < 5s, consumer lag p99 < 10s, search API p95 < 200ms)
- [x] **§B.9** Zero-downtime migration plan (5-step + rollback)
- [x] **§B.10** OutboxRelay single-instance enforcement (replicas:1 OR Redis leader)
- [x] **§B.11** **30 common pitfalls** — đã đọc + hiểu:
  1. DateTime.Now → phải UtcNow
  2. Guid.NewGuid → phải CreateVersion7 cho event_id
  3. Sync HttpContextAccessor inside background service (null)
  4. Forget to await audit publish (event lost)
  5. Insert audit AFTER SaveChanges (orphan log nếu DB rollback)
  6. ... (xem đầy đủ Phụ lục B §B.11)
- [x] **§B.12** Pre-implementation checklist (file này)
- [x] **§B.13** **Acceptance criteria per phase** — đã đọc + cam kết test PASS trước close phase
- [x] **§B.14** Logging convention (structured logging với LogContext, KHÔNG Console.WriteLine)
- [x] **§B.15** Error handling (consumer fail → retry 3x exponential → DLQ)
- [x] **§B.16** Database connection pooling (DbContext factory)
- [x] **§B.17** Metric naming (audit_events_total, audit_consumer_lag_seconds, audit_outbox_pending_total, audit_dlq_size_total)
- [x] **§B.18** Documentation template (7 docs structure)
- [x] **§B.19** **Effort breakdown task-level** (chi tiết dev-day mỗi task)

---

## Architectural Decisions chốt (tham chiếu ADR-0007)

- [x] Hybrid Architecture confirmed (vs Centralized vs Fully Decentralized)
- [x] Option C policy: 5 service có local endpoint (Auth+Battery+Ticket+File+Alert), 5 service skip (Email+Notification+Sms+AI+Gateway)
- [x] Geo IP service: **MaxMind GeoLite2 free** (pending user chốt — section 3 trong session 2026-06-19)
- [x] OutboxRelay: **k8s `replicas: 1`** (pending user chốt)
- [x] Retention: **source 1 năm / aggregate 6 tháng / Critical+Security vĩnh viễn** (pending user chốt)
- [x] SecurityOfficer seed: **migration seed Role+permission, KHÔNG seed user** (pending user chốt)
- [x] AlertAuditLog placement: **embed vào BatteryService DB** (pending user chốt — vì chưa có AlertService độc lập)

---

## Infrastructure readiness

- [x] Postgres 16 đã có ở `docker-compose.yml` — sẽ thêm DB `audit_aggregator_db` qua `#AUDIT-13`
- [x] RabbitMQ 3-management đã có ở `docker-compose.yml` — sẽ thêm topology `audit.events` exchange qua `#AUDIT-05`
- [ ] **`pg_partman` extension** — chưa cài, sẽ add vào Docker init script qua `#AUDIT-14`
- [x] `SharedContracts` project đã tồn tại — sẽ thêm `IntegrationEvents/Audit/AuditCreatedEventV1.cs` qua `#AUDIT-01`
- [x] Foundation AUTH-15/29/77 đã merge (PR #446 2026-06-18)

---

## Risk acknowledgment

Đã đọc + chấp nhận risk register `overall.md` §23 R-30..R-35:

- [x] R-30 Migration backfill chậm prod (Med×High) — mitigate batch 10k off-peak
- [x] R-31 Aggregator SPOF (High×High Critical) — mitigate source-of-truth ở service, aggregator down chỉ ảnh hưởng Admin Web UI
- [x] R-32 Causation chain break (Low×Med) — mitigate E2E test `#AUDIT-27`
- [x] R-33 Schema event versioning (Med×Med) — mitigate `AuditCreatedEventV1` versioned record
- [x] R-34 GeoIP rate limit (Low×Low) — mitigate offline DB (MaxMind)
- [x] R-35 Multi-instance OutboxRelay duplicate (Med×High) — mitigate `replicas: 1`

---

## Commitments

Sole developer Thắng (`@Alexdev257`) cam kết:

- [x] Đọc + hiểu toàn bộ Phụ lục A §A.1..A.12 + Phụ lục B §B.0..B.19.
- [x] Implement theo đúng schema chuẩn 14 cột + 10 nguyên tắc bất di bất dịch.
- [x] Không skip Phase 1 P0 task (#AUDIT-06..10) dù tight deadline.
- [x] Mỗi migration có rollback `Down()` method tested PASS staging.
- [x] Mỗi handler thay đổi/thêm audit publish phải có unit test verify notification published trong transaction.
- [x] Mỗi local endpoint Option C phải có unit + integration test.
- [x] `#AUDIT-19` integration test E2E pipeline (TestContainers Postgres + RabbitMQ) PASS trước close Phase 2.
- [x] `#AUDIT-27` causation chain test E2E PASS trước close Phase 4.
- [x] `#AUDIT-43` perf test 1000 ev/s sustained 5 phút + chaos test PASS trước close Phase 7.
- [x] 7 documentation deliverables ở `#AUDIT-45` viết đầy đủ trước close sprint.
- [x] Update `overall.md` §69.10 + `CHANGELOG.md` v1.7.0 khi close sprint.

---

## Sign-off

| Vai trò | Người | Ngày | Chữ ký |
|---|---|---|---|
| **BE Developer (sole)** | **Thắng (`@Alexdev257`)** | **2026-06-19** | ✅ **Signed** — sole developer Sprint audit, chịu trách nhiệm toàn bộ 45 task |
| Reviewer 1 | _Không có người khác_ | 2026-06-19 | Override — capstone single-developer scope |
| Reviewer 2 | _Không có người khác_ | 2026-06-19 | Override — capstone single-developer scope |
| GVHD | Trương Long (`longt5@fe.edu.vn`) | _Pending_ | Review khi báo cáo capstone final |

---

## Definition of Done

Sprint audit chính thức close khi:

- [ ] 45/45 task `#AUDIT-01..45` close trên GitHub + log trong `logs/AUDIT-{NN}/`
- [ ] `dotnet build` toàn solution PASS (10 service + AuditAggregatorService mới)
- [ ] Coverage ≥ 80% `AuditAggregatorService.Application` + `.Infrastructure` + audit code mỗi service
- [ ] Migration zero-downtime tested staging (5-step pattern §B.9)
- [ ] AuditAggregatorService SLO đạt (outbox lag p99 < 5s, consumer lag p99 < 10s, search API p95 < 200ms với 1M row)
- [ ] FE Admin Web UI Audit Explorer 5 view hoạt động (search/timeline/correlation/export/stats)
- [ ] Prometheus metric + Grafana dashboard + 3 alert rule deploy staging
- [ ] 7 documentation deliverables ở `#AUDIT-45` viết + review xong
- [ ] Update `MEMORY.md` ghi quyết định non-obvious
- [ ] Update `overall.md` §69.10 mark Phụ lục A "đã triển khai qua Sprint audit"
- [ ] GVHD review pass

---

**Sign-off date:** 2026-06-19
**Effective:** Toàn bộ Sprint audit `#AUDIT-01..45`
**Override basis:** Capstone single-developer scope (Thắng sole dev cho Sprint audit)
