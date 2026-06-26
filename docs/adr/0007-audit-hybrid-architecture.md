# ADR 0007: Hybrid Audit Architecture toàn hệ thống

**Status:** Accepted (Sprint audit Phase 0 — 2026-06-19). **Amended 2026-06-24** (xem Update log dưới).
**Tham chiếu:** `issue-authservice.md` Phụ lục A (§A.1..A.12) + Phụ lục B (§B.0..B.19). Sprint audit `#AUDIT-01..45` (GitHub `#447..#491`).
**Author:** Thắng (`@Alexdev257`) — sole BE developer Sprint audit.

> **📌 Update log 2026-06-24 (owner Thắng `@Alexdev257`) — 6 quyết định gỡ block, đồng bộ với `overall.md` §17 Decision Log + `issue-authservice.md` A.9 D11–D16:**
> - **D11 Geo IP** = MaxMind GeoLite2 free *(ADR đã chọn sẵn — confirm)*.
> - **D12 OutboxRelay** = **Redis leader election** (thay `replicas: 1` ban đầu trong R-35).
> - **D13 SecurityOfficer** = **GỘP vào `Admin`** (KHÔNG tạo role mới cho capstone) — đã cập nhật §Access control + GDPR + diagram + schema comment.
> - **D14 AlertAuditLog** = **host trong BatteryService** (route `batteryCluster`), không tách Alert service riêng.
> - **D15 Retention** = source 1 năm / aggregate 6 tháng / Critical+Security vĩnh viễn *(ADR đã ghi — confirm)*.
> - **D16 Owner** = Thắng; gate "ổn định ≥ 2 tuần" waived (sole-dev, hard-blocker code đã merge).

---

## Context

### Trạng thái hiện tại (trước Sprint audit)

Hệ thống GSU26SE55 có **3 audit pattern fragmented**:

1. **AuthService** — `auth_audit_logs` table (chi tiết action code int enum, append-only trigger qua `#AUTH-29`, outbox publish event qua `#AUTH-15`).
2. **TicketService** — `ticket_activities` table (UI timeline user-facing, không enforce append-only).
3. **BatteryService / FileStorageService / AlertService / EmailService / NotificationService / SmsService / AI Module / Gateway** — chỉ có `CreatedBy`/`UpdatedBy`/`UpdatedAt` từ `AuditableEntity` base class. **KHÔNG có audit log dedicated cho forensic.**

### Vấn đề

| # | Vấn đề | Hệ quả |
|---|---|---|
| 1 | **Cross-service forensic không trace được** — khi anomaly auto-tạo ticket, không có cách link `BatteryAnomalyDetectedEvent` → `Ticket.AutoCreatedFromAnomaly` qua audit | Compliance investigation phải SSH vào từng service log → tốn 30+ phút mỗi case |
| 2 | **GDPR right-to-be-forgotten** không thể thực thi cross-service | Legal expose: PII của 1 user nằm rải 8 service, redact 1 chỗ không reach 7 chỗ kia |
| 3 | **Security investigation** (suspicious login, brute force, account takeover) chỉ thấy AuthService perspective, không thấy Battery/Ticket activity của attacker post-compromise | Detection delay, attacker có thể leverage privileged action trước khi bị phát hiện |
| 4 | **Schema không nhất quán** — AuthService dùng int enum + display column, các service khác sẽ tự design → drift catalog | Aggregator API không thể dedupe / correlate cross-service |
| 5 | **Performance read-heavy** — query audit toàn org cần join 8 DB → impossible | Admin Web UI Audit Explorer không build được |

---

## Decision

Triển khai **Hybrid Audit Architecture** — **decentralized write** (mỗi service own audit log table) + **Outbox pattern** + **centralized read** qua `AuditAggregatorService` microservice mới.

### Kiến trúc tổng quan

```
┌─────────────────┐  ┌─────────────────┐  ┌─────────────────┐
│  AuthService    │  │  BatteryService │  │  TicketService  │  ... (10 service)
│  ┌───────────┐  │  │  ┌───────────┐  │  │  ┌───────────┐  │
│  │ Handler   │  │  │  │ Handler   │  │  │  │ Handler   │  │
│  └─────┬─────┘  │  │  └─────┬─────┘  │  │  └─────┬─────┘  │
│        ▼        │  │        ▼        │  │        ▼        │
│  ┌───────────┐  │  │  ┌───────────┐  │  │  ┌───────────┐  │
│  │ audit_log │  │  │  │ audit_log │  │  │  │ audit_log │  │   ← source of truth
│  │ + outbox  │  │  │  │ + outbox  │  │  │  │ + outbox  │  │     (per service DB)
│  └─────┬─────┘  │  │  └─────┬─────┘  │  │  └─────┬─────┘  │
│        ▼        │  │        ▼        │  │        ▼        │
│  ┌───────────┐  │  │  ┌───────────┐  │  │  ┌───────────┐  │
│  │ Relay     │  │  │  │ Relay     │  │  │  │ Relay     │  │   ← Background service
│  │ Background│  │  │  │ Background│  │  │  │ Background│  │     publish to RabbitMQ
│  └─────┬─────┘  │  │  └─────┬─────┘  │  │  └─────┬─────┘  │
└────────┼────────┘  └────────┼────────┘  └────────┼────────┘
         │                    │                    │
         └────────────────────┼────────────────────┘
                              ▼
                  ┌──────────────────────────┐
                  │  RabbitMQ                │
                  │  audit.events (topic)    │   ← Routing key: audit.{service}.{cat}.{sev}
                  │  → aggregator.audit.events│
                  └──────────┬───────────────┘
                             │
                             ▼
                  ┌──────────────────────────┐
                  │  AuditAggregatorService  │
                  │  ┌────────────────────┐  │
                  │  │ AuditCreated       │  │   ← Idempotent INSERT
                  │  │ Consumer (idempot) │  │     ON CONFLICT (event_id) DO NOTHING
                  │  └────────┬───────────┘  │
                  │           ▼              │
                  │  ┌────────────────────┐  │
                  │  │ audit_aggregate    │  │   ← Read store
                  │  │ (partitioned/mo)   │  │     pg_partman auto-create
                  │  └────────────────────┘  │
                  │           ▲              │
                  │  ┌────────┴───────────┐  │
                  │  │ Search / Stats /   │  │   ← Admin Web UI (Phase 6)
                  │  │ Correlation API    │  │     Admin access (D13)
                  │  └────────────────────┘  │
                  └──────────────────────────┘
```

### 4 nguyên tắc cốt lõi

1. **Source of truth = mỗi service local table.** Aggregator chỉ là **materialized view** (read-store), KHÔNG phải nguồn duy nhất. Nếu aggregator hỏng/mất data → replay từ source (`POST /api/admin/audit/replay`).
2. **Write = atomic per service.** Handler insert vào `auth_audit_logs` + `audit_outbox` **CÙNG transaction** với business write. Relay background service publish event **SAU commit** (đảm bảo no orphan event).
3. **Read = centralized, partitioned.** `audit_aggregate` partitioned by month qua `pg_partman` (auto-create 3 tháng trước). Retention: drop partition cũ hơn 6 tháng EXCEPT `severity ∈ {Critical, Security}` (vĩnh viễn).
4. **Idempotency = `event_id` Guid v7.** Consumer `INSERT ON CONFLICT (event_id) DO NOTHING` → duplicate event (RabbitMQ at-least-once) chỉ 1 row insert.

---

## Lý do chọn Hybrid (vs alternatives)

### So sánh 3 kiến trúc

| Tiêu chí | **Hybrid (chọn)** | Centralized | Fully Decentralized |
|---|---|---|---|
| **Write latency** | Local DB only (~5ms) | Sync write to aggregator (~50ms cross-DC) | Local DB only (~5ms) |
| **Write fail mode** | Local DB fail → handler 500, nothing lost | Aggregator down → whole system stuck | Local DB fail → handler 500 |
| **Read API** | Aggregator (1 query 8 service) | Aggregator (built-in) | Per-service join (impossible) |
| **GDPR redaction** | Redact aggregate + audit metadata, source giữ raw cho legal hold | Redact 1 chỗ | Redact 8 chỗ — không dedupe |
| **Replay khi aggregator hỏng** | ✅ Source-of-truth ở service local | ❌ Data mất luôn | N/A (không có aggregator) |
| **Cross-service correlation** | `correlation_id` + `causation_id` chain | Built-in | ❌ Manual join |
| **Operational complexity** | Trung bình (1 microservice mới) | Cao (aggregator = SPOF, cần HA) | Thấp |
| **Cost** | Trung bình (1 DB partitioned) | Cao (aggregator DB scale với org size) | Thấp |

### Tại sao KHÔNG Centralized

- **SPOF risk** — aggregator down → tất cả 10 service block write audit. R-31 ở §23 overall.md đánh giá **High×High Critical**.
- **Cross-DC latency** — capstone deploy đa cluster (k8s), sync write từ service → aggregator qua mạng = blocking call ~50ms p99.
- **GDPR legal hold** — source giữ raw để compliance, aggregator redact display. Centralized không có 2-tier này.

### Tại sao KHÔNG Fully Decentralized

- **Cross-service forensic impossible** — Admin Web UI Audit Explorer cần search "all action của user X qua 30 ngày" → phải query 8 DB → join client-side = 8 round-trip + bandwidth.
- **`correlation_id` chain break** — không có aggregator để follow `event_id → causation_id` xuyên service.
- **GDPR redaction cross-service** — phải redact 8 DB manual, dễ miss.

---

## Option C policy (per Phụ lục A §A.5.1.bis)

Mỗi service quyết định **có expose local audit endpoint** hay KHÔNG, dựa trên 3 criteria:

| Criteria | Yes → có local endpoint | No → chỉ Aggregator |
|---|---|---|
| Business-specific filter cần | Vd `ticketId`, `batteryId`, `fileId` | Generic action code đủ |
| Fallback resilience cần | Service vẫn admin được khi aggregator hỏng | Acceptable nếu aggregator down |
| Audit volume cao | > 1k row/day | Volume thấp, batch query đủ |

### Phân loại 10 service

| Service | Local endpoint | Path | Lý do |
|---|---|---|---|
| **AuthService** | ✅ (giữ nguyên 2 endpoint hiện tại) | `/api/admin/accounts/{id}/audit-logs` + `/api/me/login-history` | Security-critical + đã có sẵn |
| **BatteryService** | ✅ build mới (`#AUDIT-23`) | `/api/admin/battery/audit-logs` | Filter `batteryId` + `assignmentId` + threshold change |
| **TicketService** | ✅ build mới (`#AUDIT-28`) | `/api/admin/ticket/audit-logs` | Filter `ticketId` + SLA breach history + state transition |
| **FileStorageService** | ✅ build mới (`#AUDIT-30`) | `/api/admin/files/audit-logs` | GDPR file access investigation, filter `fileId`/`bucketName` |
| **AlertService** → host trong **BatteryService** (D14) | ✅ build mới (`#AUDIT-32`, route `batteryCluster`) | `/api/admin/alerts/audit-logs` | Acknowledge/suppress history, filter `alertId`. Chốt 2026-06-24: KHÔNG tách Alert service riêng cho capstone |
| EmailService | ❌ qua Aggregator | — | Volume thấp, generic filter đủ |
| NotificationService | ❌ qua Aggregator | — | Volume thấp |
| SmsService | ❌ qua Aggregator | — | Volume thấp |
| AI Module | ❌ qua Aggregator | — | Inference call volume cao nhưng không cần per-call filter |
| Gateway | ❌ qua Aggregator | — | Rate limit log, không cần local detail |

---

## Schema chuẩn — 14 cột bắt buộc

Mọi audit table (`auth_audit_logs`, `battery_audit_logs`, `ticket_audit_logs`, `file_audit_logs`, `alert_audit_logs`, `email_audit_logs`, `notification_audit_logs`, `sms_audit_logs`, `ai_audit_logs`, `gateway_audit_logs`) PHẢI có 14 column này:

```sql
-- Identity + classification
event_id          UUID         NOT NULL UNIQUE   -- Guid v7 (time-sortable)
service_name      VARCHAR(50)  NOT NULL          -- "AuthService", "BatteryService", ...
action_code       VARCHAR(100) NOT NULL          -- "AccountLocked", "BatteryAssigned", ...
action_category   VARCHAR(50)  NOT NULL          -- 9 fixed: Authentication, Authorization, AccountLifecycle, ResourceLifecycle, Configuration, Communication, Inference, Saga, Audit
severity          VARCHAR(20)  NOT NULL          -- 4 fixed: Info, Warning, Critical, Security

-- Target
target_type       VARCHAR(50)                    -- "Account", "Battery", "Ticket", "File", ...
target_id         UUID                           -- Id của entity bị action
target_display    VARCHAR(255)                   -- Email hoặc display name (PII redacted khi search)

-- Actor
actor_account_id  UUID                           -- Null nếu anonymous (login fail) hoặc system action
actor_role        VARCHAR(50)                    -- "Admin", "Manager", "Staff", "Customer", "System" (role "SecurityOfficer" defer — gộp Admin, D13)
actor_display     VARCHAR(255)                   -- Email hoặc display name
actor_ip          VARCHAR(45)                    -- IPv4/IPv6, normalized
actor_user_agent  VARCHAR(512)

-- Result + context
is_success        BOOLEAN      NOT NULL
error_code        VARCHAR(50)                    -- Null nếu success
reason            VARCHAR(500)
metadata_json     JSONB                          -- Flexible per-action data (GIN index)

-- Correlation
correlation_id    UUID                           -- Request trace id (từ CorrelationIdMiddleware AUTH-77)
causation_id      UUID                           -- Parent event_id (vd ticket auto-tạo từ anomaly → causation_id = battery audit event_id)

-- Time
occurred_at       TIMESTAMPTZ  NOT NULL          -- Thời điểm action xảy ra (handler set)
recorded_at       TIMESTAMPTZ  NOT NULL          -- Thời điểm row insert (DB DEFAULT now())

-- Audit
created_at        TIMESTAMPTZ  NOT NULL          -- AuditableEntity base
is_deleted        BOOLEAN      NOT NULL DEFAULT false
deleted_at        TIMESTAMPTZ
```

### Append-only protection

Migration tạo PG trigger `BEFORE UPDATE/DELETE ON {service}_audit_logs FOR EACH ROW RAISE EXCEPTION` (đã có ở `#AUTH-29` cho AuthService, upgrade sang **soft mode** ở `#AUDIT-10` cho phép update outbox-related fields, chặn business fields).

### `audit_aggregate` table (centralized read-store)

Schema giống 14 cột + thêm:
- `service_audit_id` (UUID NOT NULL) — Id row ở source table (cho replay)
- `geo_country` (VARCHAR(2)) — Từ MaxMind GeoIP enrichment
- `geo_city` (VARCHAR(120))
- `ingested_at` (TIMESTAMPTZ) — Thời điểm consumer insert

Partition by month (`PARTITION BY RANGE (occurred_at)`), pg_partman auto-create 3 tháng trước.

---

## Migration strategy — Zero-downtime

Theo Phụ lục B §B.9 — pattern 5 bước cho mỗi service:

```
Step 1 — Add nullable columns + outbox table + relay BackgroundService
         (KHÔNG break read/write hiện tại)
   ↓
Step 2 — Deploy → relay chạy nhưng outbox empty, no event published
   ↓
Step 3 — Update handler: insert vào table HIỆN TẠI + outbox CÙNG transaction
         (audit log row vẫn ghi như cũ + bonus outbox)
   ↓
Step 4 — Backfill SQL row cũ (set event_id=Guid v7, action_code=enum→string)
         Batch 10k row mỗi loop, off-peak (02:00 UTC), monitor pg lock < 1s/batch
   ↓
Step 5 — Set NOT NULL constraint + unique index event_id (sau backfill 100%)
         Upgrade trigger từ strict → soft mode
```

**Rollback test:** Mỗi migration phải có `Down()` method tested PASS ở staging trước khi merge.

---

## Retention policy

### Source-of-truth tables (per service)

- **Retain 1 năm** mọi row.
- Background service per service auto-drop row cũ hơn 365 ngày (daily 03:00 UTC).
- EXCEPT `severity ∈ {Critical, Security}` → vĩnh viễn (legal hold).

### Aggregator read-store (`audit_aggregate`)

- **Retain 6 tháng** mọi row (drop partition pg_partman).
- EXCEPT `severity ∈ {Critical, Security}` → vĩnh viễn (move sang `audit_aggregate_archive` partition khác).
- `AuditRetentionBackgroundService` daily 03:00 UTC ở Aggregator (`#AUDIT-41`).

### Lý do retention asymmetric

- Source giữ 1 năm cho **compliance audit + legal hold** (regulator yêu cầu trace 12 tháng).
- Aggregate giữ 6 tháng vì **storage cost** — partition by month × 10 service × ~10k row/day = ~1.5M row/month. 6 tháng = ~9M row, query với GIN index < 200ms p95 với 1M row.
- Critical/Security keep forever — incident investigation cần lookup nhiều năm về sau.

---

## Security & PII

### 1. Append-only enforcement

- DB trigger ở mỗi `{service}_audit_logs` (đã có AUTH-29, sẽ replicate qua `#AUDIT-21, 25, 29, 31, 33, 34, 35`).
- Trigger **soft mode** (sau `#AUDIT-10`): cho phép UPDATE `outbox_status`/`processed_at`/`retry_count`/`last_error`, CHẶN UPDATE `action_code`/`actor_account_id`/`target_id`/`occurred_at`/`event_id`.
- Application code KHÔNG được call `_unitOfWork.{service}AuditLogs.UpdateAsync(...)` — chỉ Add.

### 2. PII handling

| Field | Stored where | Redaction policy |
|---|---|---|
| Email | source + aggregate | Plain text (cần cho search); GDPR redact `email='[REDACTED]'` qua `POST /api/admin/audit/redact` (Admin only — D13) |
| Phone | source + aggregate | Plain text; GDPR redactable |
| Full name | source + aggregate | Plain text; GDPR redactable |
| IP address | source + aggregate | Plain text (Critical for forensic); GDPR redactable |
| Password | NEVER stored | — |
| OTP/Secret | NEVER stored | — |
| Geo (country/city) | aggregate only | From MaxMind GeoIP (no PII raw) |

### 3. GDPR right-to-be-forgotten

- Endpoint `POST /api/admin/audit/redact?accountId={id}` — chỉ `Admin` role (role `SecurityOfficer` gộp Admin — D13, 2026-06-24).
- Action: UPDATE `audit_aggregate` SET `target_display='[REDACTED]'`, `actor_display='[REDACTED]'`, `actor_ip='[REDACTED]'` WHERE involves `accountId`.
- **KHÔNG xóa row** — giữ `event_id` + `action_code` + `timestamp` cho audit trail.
- Source tables **KHÔNG redact** — giữ raw cho legal hold (yêu cầu của regulator).
- Meta-audit: action `AccountDataRedacted` (severity=Security) ghi vào aggregate cho audit "ai redact, khi nào".

### 4. Access control

> **📌 UPDATE 2026-06-24 (D13):** KHÔNG tạo role `SecurityOfficer` cho capstone scope — **gộp toàn bộ quyền vào `Admin`**. Bản gốc dưới đây giữ làm thiết kế production (separation-of-duties); nếu lên production thì tách `SecurityOfficer` ra + di chuyển `audit.replay`/`audit.redact` sang.

- **Admin** (capstone) — full access aggregator API: `audit.read`, `audit.export`, `audit.replay`, `audit.redact`. Rate limit 200 req/min.
- ~~**SecurityOfficer** (role mới, seed qua `#AUDIT-18` migration) — full access; Admin chỉ read+export~~ → **DEFER cho production (D13)**.

### 5. Idempotency-Key chống replay attack

- `POST /api/admin/audit/replay?service=&from=&to=` (admin tool replay khi aggregator hỏng) — yêu cầu `Idempotency-Key` header để chống double-replay.

---

## Performance SLO

| Metric | Target |
|---|---|
| Outbox lag p99 (write → published) | < 5s |
| Consumer lag p99 (published → aggregate inserted) | < 10s |
| `GET /api/admin/audit/search` p95 với 1M row | < 200ms |
| `GET /api/admin/audit/correlation/{id}` p95 | < 100ms |
| Sustained throughput | 1000 event/sec sustained 5 phút, no DLQ entry (`#AUDIT-43` perf test) |

---

## Risk register

(Đầy đủ ở `overall.md` §23 R-30..R-35)

| Risk | Likelihood × Impact | Mitigation |
|---|---|---|
| R-30 Migration backfill chậm > 5p prod | Med × High | Batch 10k, off-peak, rollback plan §B.9 |
| R-31 Aggregator SPOF | High × High **Critical** | Source-of-truth ở service; aggregator down chỉ ảnh hưởng Admin Audit Explorer, KHÔNG block business |
| R-32 Causation chain break | Low × Med | E2E test `#AUDIT-27` verify chain anomaly → ticket |
| R-33 Schema event versioning | Med × Med | `AuditCreatedEventV1` (versioned record), backward-compat khi V2 |
| R-34 GeoIP rate limit | Low × Low | MaxMind GeoLite2 free offline DB → no API limit |
| R-35 Multi-instance OutboxRelay duplicate | Med × High | **Redis leader election** (`IDistributedCache` lease key `audit_outbox_leader`, renew 30s, non-leader skip) — chốt 2026-06-24 (D12, §B.10 option 1), thay cho `replicas: 1` ban đầu; idempotent consumer là last-line defense |

---

## Alternatives considered

### A. ELK Stack (Elasticsearch + Logstash + Kibana)

**Rejected.** Lý do:
- ELK = log aggregation chung, KHÔNG schema-enforced cho audit (chỉ document store).
- Không enforce idempotency `event_id` → duplicate khi replay.
- Capstone scope không cần full-text search nâng cao (đủ với Postgres GIN + B-tree).
- Operational cost (cluster 3+ node) > custom Postgres aggregator.

### B. AWS CloudTrail / GCP Audit Logs

**Rejected.** Lý do:
- Vendor lock-in.
- Capstone deploy on-prem / self-hosted Kubernetes.
- Schema không match domain model (target_type = entity của hệ thống, không phải AWS resource).

### C. Event Sourcing (Kafka + KSQL)

**Rejected.** Lý do:
- Over-engineering cho capstone scope.
- Đã có Outbox pattern + RabbitMQ — không cần Kafka.
- Team chưa familiar với event sourcing pattern → risk delay.

---

## Sign-off

| Vai trò | Người | Ngày | Note |
|---|---|---|---|
| BE Developer | Thắng (`@Alexdev257`) | 2026-06-19 | Sole developer Sprint audit |
| Reviewer 1 | _Tự ký với vai trò sole dev_ | 2026-06-19 | Capstone scope — single developer responsibility |
| Reviewer 2 | _Tự ký với vai trò sole dev_ | 2026-06-19 | — |
| GVHD review | Trương Long (longt5@fe.edu.vn) | _pending_ | Khi báo cáo capstone |

**Note:** Sign-off 3 thành viên ở Phụ lục A là requirement production-grade. Capstone scope = single sole developer (Thắng) → Thắng tự sign-off + chịu trách nhiệm. GVHD review khi báo cáo.

---

## Consequences

### Positive

- Cross-service forensic khả thi (`correlation_id` chain).
- GDPR compliance đạt (redaction endpoint).
- Admin Web UI Audit Explorer build được (5 view: search/timeline/correlation/export/stats).
- 1 audit pipeline cho toàn org → onboard service mới chỉ cần follow `Contributor Guide` (`#AUDIT-45`).
- Prometheus metric + Grafana dashboard cho ops visibility.

### Negative

- 1 microservice mới (`AuditAggregatorService`) → tăng operational complexity.
- Eventual consistency aggregator (lag < 10s p99) → KHÔNG dùng aggregator cho real-time security decision (vd block login đang attack).
- Storage cost tăng: source 1 năm × 10 service + aggregate 6 tháng.

### Mitigation

- AuditAggregator có health check `/live`/`/ready` k8s + Prometheus alert rule (`#AUDIT-44`).
- Real-time security decision dùng AuthService local audit + Redis (vẫn đủ — đã có AUTH-15 TRL).
- pg_partman auto drop partition cũ → storage cost bounded.

---

## Implementation roadmap

7 phase / 44 dev-day / 45 task `#AUDIT-01..45` — chi tiết `overall.md` §17 Sprint audit.

| Phase | Tasks | Effort | Dependency |
|---|---|---|---|
| 0 | Chuẩn bị + ADR + SharedContracts + analyzer + RabbitMQ topology | 3 ngày | Không |
| 1 | Refactor AuthService audit (14 cột + outbox + 22 handler) | 7 ngày | ADR sign-off + Phase 0 |
| 2 | AuditAggregatorService scaffold + 7 REST API | 8.5 ngày | Phase 1 |
| 3 | BatteryService onboard | 3.5 ngày | Phase 2 |
| 4 | TicketService onboard + causation chain | 5.5 ngày | Phase 2 |
| 5 | FileStorage/Alert/Email/Notification/Sms/AI/Gateway onboard | 5 ngày | Phase 2 |
| 6 | FE Admin Web UI Audit Explorer (5 view) | 6 ngày | Phase 2 API ready |
| 7 | Hardening (retention + GDPR + perf + monitoring + docs) | 5 ngày | Phase 1-6 |

---

## References

- `issue-authservice.md` Phụ lục A §A.1..A.12 (kiến trúc tổng quan + 7 phase roadmap + Option C policy)
- `issue-authservice.md` Phụ lục B §B.0..B.19 (10 nguyên tắc + 30 pitfalls + zero-downtime migration + acceptance criteria + effort breakdown)
- `overall.md` §17 Sprint audit (45 task chi tiết)
- `overall.md` §23 Risk register (R-30..R-35)
- `overall.md` §40.1 ADR-020 entry (registered)
- `overall.md` §69.11 Sprint audit overview
- Liu et al. ICDM 2008 — Isolation Forest (anomaly detection precedent từ AI module — not used here directly nhưng pattern tham khảo)
- NIST SP 800-92 — Guide to Computer Security Log Management
- GDPR Article 17 — Right to erasure ("right to be forgotten")
- ISO/IEC 27001 A.12.4 — Logging and monitoring
