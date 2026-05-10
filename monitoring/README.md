# Monitoring Stack — Solar Battery Maintenance

## Overview

Pull-based monitoring với Prometheus + Grafana + Alertmanager. Centralized logging với Loki + Promtail. Tất cả config provisioned tự động khi `docker compose up`.

```
                                     METRICS
ASP.NET services /metrics  ─┐
RabbitMQ /15692              ├─→ Prometheus ─→ Grafana ─→ Alertmanager → webhook
Minio /minio/v2/metrics      │
*-exporter (postgres/redis)  │
node-exporter / cAdvisor    ─┘

                                     LOGS
ASP.NET stdout (JSON)       ─→ Promtail (Docker SD) ─→ Loki ─→ Grafana Explore + Logs Dashboard
```

## Quick Start

```bash
docker compose up -d --build
```

Truy cập:

| URL | Service | Login |
|---|---|---|
| http://localhost:3001 | Grafana | admin / admin |
| http://localhost:9094 | Prometheus | — |
| http://localhost:9095 | Alertmanager | — |
| http://localhost:8081 | cAdvisor | — |
| http://localhost:3100/ready | Loki health | — |
| http://localhost:9080 | Promtail UI | — |

## Dashboards

4 dashboards auto-loaded:

| Dashboard | UID | Mục đích |
|---|---|---|
| Services Overview | `services-overview` | RPS, latency p95, 5xx error rate, status code distribution |
| Messaging & Reliability | `messaging-reliability` | Outbox pending/published/failures, Inbox processed/duplicate, Idempotency replay/conflict |
| Infrastructure | `infrastructure` | Host CPU/RAM/disk, container resources, Postgres TPS, Redis/RabbitMQ stats |
| **Logs Overview** | `logs-overview` | **Log volume by service, error count, error rate, recent errors, trace by Correlation ID** |

## Alert Rules (10)

Critical (page oncall):
1. **ServiceDown** — `/metrics` không scrape được trong 1 phút
2. **HighErrorRate** — > 5% requests trả 5xx trong 5 phút
3. **OutboxPublishFailures** — > 1 fail/sec (RabbitMQ outage)

Warning (Slack):
4. **HighLatencyP95** — p95 > 1s
5. **OutboxBacklog** — > 100 messages pending
6. **OutboxPoisonMessages** — message reach MaxRetries
7. **HighInboxDuplicateRate** — > 50% messages bị skip duplicate
8. **IdempotencyConflictSpike** — > 0.5 conflicts/sec
9. **ContainerMemoryHigh** — container > 90% memory limit
10. **PostgresConnectionsHigh** — > 80% max_connections

## Custom Application Metrics

Defined in `shared/src/SharedInfrastructure/Metrics/AppMetrics.cs`:

| Metric | Type | Labels | Where Inc'd |
|---|---|---|---|
| `outbox_messages_processed_total` | Counter | event_type | OutboxRelayBackgroundService |
| `outbox_messages_failures_total` | Counter | reason | OutboxRelayBackgroundService |
| `outbox_messages_skipped_total` | Counter | — | OutboxRelayBackgroundService (poison) |
| `outbox_messages_pending_count` | Gauge | — | OutboxRelayBackgroundService (each tick) |
| `inbox_messages_processed_total` | Counter | consumer | IdempotentConsumerExtensions |
| `inbox_messages_skipped_duplicate_total` | Counter | consumer | IdempotentConsumerExtensions |
| `idempotency_key_replay_hits_total` | Counter | — | IdempotencyKeyMiddleware |
| `idempotency_key_conflicts_total` | Counter | — | IdempotencyKeyMiddleware |
| `idempotency_key_reservations_total` | Counter | — | IdempotencyKeyMiddleware |

Plus auto-collected from `prometheus-net.AspNetCore`:
- `http_requests_received_total{code, method, controller, action}`
- `http_request_duration_seconds_bucket{le, method, controller, action}`
- `http_requests_in_progress`

## Centralized Logging (Loki + Promtail)

### Cách hoạt động

5 service ASP.NET ghi log dạng **JSON structured** ra stdout (cấu hình qua env var `Logging__Console__FormatterName=json` trong `.env` / `.env.Docker` — không sửa appsettings.json hay code). Promtail đọc Docker logs qua Docker socket → parse JSON → ship Loki. Loki lưu local filesystem 7 ngày.

### LogQL examples

Mở Grafana → **Explore** → đổi datasource từ Prometheus sang **Loki**:

```logql
# Tất cả log của 1 service
{container="solar-authservice"}

# Chỉ ERROR — `level` là label promoted từ pipeline_stages
{container=~"solar-.*", level="Error"}

# Filter regex trên message
{container=~"solar-.*"} | json | Message =~ "(?i)timeout"

# Count log per service
sum(rate({container=~"solar-.*"}[1m])) by (container)

# Trace 1 request đi qua nhiều service bằng Correlation ID
# (CorrelationIdMiddleware tự BeginScope với key "CorrelationId")
{container=~"solar-.*"} |= "abc-correlation-id-xyz"

# Tìm tất cả lần Outbox publish failed
{container="solar-authservice"} | json | Category =~ "OutboxRelay" | LogLevel="Warning"

# Error rate cross-service trong 5 phút
sum(rate({container=~"solar-.*", level=~"Error|Critical"}[5m])) by (container)
```

### Schema log JSON

`JsonConsoleFormatter` của .NET 8 xuất:

```json
{
  "Timestamp": "2026-04-29T10:32:11.234Z",
  "EventId": 0,
  "LogLevel": "Information",
  "Category": "AuthService.Application.Handlers.LoginHandler",
  "Message": "User abc@x.com logged in.",
  "Scopes": [
    { "CorrelationId": "req-abc-123", "RequestId": "0HN..." },
    { "RequestPath": "/api/auth/login" }
  ]
}
```

Promtail parse:
- `LogLevel` → label `level` (filter nhanh)
- `Category` → field `category` (filter chậm hơn nhưng không cardinality bomb)
- `Message` → output line chính

### Tweak retention

Sửa `monitoring/loki/loki-config.yml`:

```yaml
limits_config:
  retention_period: 720h    # 30 ngày
```

Production khuyến nghị thay `filesystem` bằng S3:

```yaml
common:
  storage:
    s3:
      endpoint: s3.amazonaws.com
      bucketnames: my-loki-logs
      access_key_id: ...
      secret_access_key: ...
```

### Skip một container không muốn scrape

Sửa regex `keep` trong `monitoring/promtail/promtail-config.yml`:

```yaml
- source_labels: ["__meta_docker_container_name"]
  regex: "/solar-(authservice|emailservice|smsservice|filestorageservice|apigateway|postgres|redis|rabbitmq|minio)"
  action: keep
```

Bỏ tên container ra khỏi alternation `(...)` để Promtail skip.

## Production Hardening

Khi deploy production, đổi:

1. **Grafana password** — `GF_SECURITY_ADMIN_PASSWORD` từ `admin` → giá trị strong
2. **Alertmanager receivers** — uncomment `webhook_configs` trong `alertmanager/config.yml`, set Slack/PagerDuty URL từ secret
3. **Minio metrics auth** — bỏ `MINIO_PROMETHEUS_AUTH_TYPE: public`, dùng JWT token
4. **Prometheus retention** — tăng `--storage.tsdb.retention.time` từ 15d nếu cần
5. **Network exposure** — không expose 9094/9095/3001 ra public, đặt sau VPN hoặc IAP

## Reload Prometheus Config

Sau khi sửa `prometheus.yml` hoặc `alert-rules.yml`:

```bash
curl -X POST http://localhost:9094/-/reload
```

## Tham khảo grafana dashboards 3rd-party (import qua UI)

Có thể import thêm các dashboard có sẵn (Grafana → Dashboards → New → Import → ID):

- ASP.NET Core: ID **10915**
- Postgres exporter: ID **9628**
- Redis exporter: ID **11835**
- RabbitMQ Prometheus: ID **10991**
- Node exporter Full: ID **1860**
- cAdvisor: ID **14282**
