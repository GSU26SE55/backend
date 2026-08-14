# Audit Pipeline — Operations Runbook (#AUDIT-45)

## Symptoms & mitigations

| Symptom | Nguyên nhân | Mitigation |
|---------|-------------|-----------|
| `audit_outbox_pending_total > 1000` (5 min) | Relay stuck / RabbitMQ down / leader chết | Check RabbitMQ healthy; restart relay; kiểm tra Redis leader key `*_audit_outbox_leader` |
| `audit_consumer_lag_seconds` p99 > 30s | Aggregator quá tải / DB chậm | Scale aggregator pod (MassTransit fan-out); check `audit_aggregate` index |
| DLQ `aggregator.audit.events.dlq` tăng | Event poison / schema mismatch | Inspect DLQ message; fix consumer; shovel DLQ → main queue sau khi fix |
| Aggregator API chậm (>200ms p95) | Thiếu index / partition lớn | Verify GIN + B-tree index; check pg_partman partition pruning |

## Replay từ source-of-truth
Khi `audit_aggregate` hỏng/mất: `POST /api/admin/audit/replay?service=&from=&to=` → trả `202` kèm `jobId` sau khi đã **lưu job bền vững**.
Mỗi service nguồn đọc bảng **`{service}_audit_outbox`** của mình (cột `payload` chính là `AuditCreatedEventV1` đã serialize) rồi phát lại, **giữ nguyên `EventId`**; consumer idempotent theo `EventId` nên chạy lại nhiều lần không sinh bản ghi trùng.

Theo dõi: `GET /api/admin/audit/replay/{jobId}`.

- `pendingServices` cho biết đang chờ service nào — dùng khi job không kết thúc.
- `truncated = true` ⇒ **dữ liệu CHƯA đầy đủ** dù trạng thái đã kết thúc: có service chạm trần an toàn 50.000 bản ghi/lần, hoặc gặp payload hỏng. Chạy lại với khoảng thời gian hẹp hơn.
- Trạng thái `CompletedWithErrors` = đủ service phản hồi nhưng có lỗi hoặc bị cắt ngắn.

> Vì sao đọc outbox chứ không đọc `{service}_audit_logs`: sáu bảng audit-log **không đồng nhất** (`AuthService.AuditLog` dùng `IpAddress`/`UserAgent`; `SmsService.SmsAuditLog` thậm chí không phải bảng audit-event). Outbox thì giống nhau ở cả 6 service và giữ đúng payload cần phát lại.

## GDPR redaction
`POST /api/admin/audit/redact?accountId={id}` (Admin) → PII ở `audit_aggregate` thành `[REDACTED]`. Source tables KHÔNG redact (legal hold). Ghi meta-audit `AccountDataRedacted`.

## Retention
`AuditRetentionBackgroundService` daily ~03:00 UTC: xóa row aggregate > 6 tháng TRỪ Critical/Security. Source tables retain 1 năm (per-service job).

## Migration ops (#AUDIT-06/14)
- AuthService: `dotnet ef database update -p AuthService.Infrastructure -s AuthService.Api` → migration `AddAuditStandardColumnsAndOutbox` (backfill + trigger soft mode swap).
- BatteryService: migration `AddBatteryAudit`.
- AuditAggregator: migration `InitialAuditAggregate` (partitioned + pg_partman fallback) — auto-migrate on startup.

## Load-test & chaos (#AUDIT-43)
**Tầng read-store (đã automate):** `AuditThroughputChaosTests` (TestContainers Postgres thật):
- `SustainedIngest_MeetsThroughputTarget_1000EventsPerSecond` — 5000 event @ concurrency 16, assert ≥ 1000 ev/s.
- `DuplicateStorm_UnderConcurrency_StaysIdempotent` — 50 event × 64 worker đồng thời (cùng `event_id`+`occurred_at`) → assert đúng 50 row, 0 exception leak.
- Chạy: `dotnet test --filter FullyQualifiedName~AuditThroughputChaosTests` (cần Docker).

**Full-stack load (manual, cần stack chạy):** đo end-to-end qua broker + relay + aggregator:
1. `docker compose up` (Postgres + RabbitMQ + Redis + các service).
2. Bắn tải sinh audit (vd k6 hoặc script POST login/battery-update) ở ~1000 req/s.
3. Quan sát Grafana `audit-pipeline`: `rate(audit_events_total[1m])` ≥ 1000, `audit_consumer_lag_seconds` p99 < 30s, `audit_outbox_pending_total` không tăng tuyến tính.
4. Chaos: `docker compose stop rabbitmq` 30s → outbox dồn (Pending tăng) → start lại → relay drain hết, lag hồi phục, không mất/duplicate event (idempotency).
