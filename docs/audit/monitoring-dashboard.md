# Audit — Monitoring & Dashboard (#AUDIT-44/45)

Observability của Hybrid Audit pipeline: metrics → Prometheus → Grafana + alert.

## 1. Metrics (`SharedInfrastructure/Metrics/AppMetrics.cs`)
| Metric | Type | Labels | Ý nghĩa |
|--------|------|--------|---------|
| `audit_events_total` | Counter | service, action, severity | Tổng event đã ingest vào read-store `audit_aggregate` |
| `audit_consumer_lag_seconds` | Histogram | — | Lag từ `occurred_at` → aggregator ingest (exp buckets 0.1s..~410s) |
| `audit_outbox_pending_total` | Gauge | service | Số outbox `Pending` hiện tại / mỗi service |
| `audit_dlq_size_total` | Gauge | — | Kích thước dead-letter queue |

- `audit_events_total` + `audit_consumer_lag_seconds`: set ở `AuditCreatedConsumer` khi insert thành công.
- `audit_outbox_pending_total`: set mỗi tick (2s) trong 6 relay BackgroundService.
- Scrape qua `/metrics` (prometheus-net) đã expose sẵn ở từng service.

## 2. Grafana dashboard
`monitoring/grafana/dashboards/audit-pipeline.json` — uid `audit-pipeline`, 8 panel:
1. Events ingested rate (by service) — `rate(audit_events_total[5m])`
2. Events by severity — `sum by(severity)(rate(audit_events_total[5m]))`
3. Consumer lag p50/p95/p99 — `histogram_quantile(.., audit_consumer_lag_seconds_bucket)`
4. Outbox pending (by service) — `audit_outbox_pending_total`
5. DLQ size — `audit_dlq_size_total`
6. Top actions — `topk(10, sum by(action)(rate(audit_events_total[1h])))`
7. Total events (stat) — `sum(audit_events_total)`
8. Ingest success vs fail.

## 3. Alert rules (`monitoring/prometheus/alert-rules.yml`, group `audit-pipeline`)
| Alert | Expr | For | Severity |
|-------|------|-----|----------|
| AuditOutboxBacklog | `max(audit_outbox_pending_total) > 1000` | 5m | warning |
| AuditConsumerLag | `histogram_quantile(0.99, ...) > 30` | 5m | warning |
| AuditDlqGrowing | `audit_dlq_size_total > 100` | 5m | critical |

## 4. Vận hành
- **Backlog cao** → kiểm tra broker (RabbitMQ) + leader relay còn sống không (Redis key `*_audit_outbox_leader`).
- **Lag cao** → aggregator consumer chậm / DB read-store nghẽn → scale consumer hoặc check partition.
- **DLQ tăng** → message lỗi schema → xem `LastError` trong outbox + replay sau khi fix (`POST /api/admin/audit/replay`).
- Runbook chi tiết: [operations-runbook.md](operations-runbook.md).
