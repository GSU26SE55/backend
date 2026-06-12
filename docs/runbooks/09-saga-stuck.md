# Runbook 09 — Alert–Ticket Saga Stuck

> Sprint 5B #239. Liên kết §40.3, §53.11.

## Định nghĩa "Stuck"

Saga ở non-terminal state (`TicketRequested` / `TicketProvisioned` / `AlertLinkRequested`)
nhưng > 15 phút mà chưa transit / chưa Failed.

## Trigger

- Prometheus alert: `saga_alert_ticket_active_count > 50` hoặc
  `time() - saga_started_timestamp > 900` (15min).
- `/health/saga` endpoint trả `stuckOver15min > 10`.

## Khả năng nguyên nhân

| Khả năng | Khu vực | Cách phát hiện |
|----------|---------|-----------------|
| Quartz timer KHÔNG fire | Ticket DB | `SELECT * FROM qrtz_triggers WHERE next_fire_time < extract(epoch FROM now())*1000` ra rows tồn đọng |
| Quartz schema chưa apply | Ticket DB | `\d qrtz_triggers` báo "does not exist" → migration `AddQuartzPersistenceSchema` chưa chạy |
| Quartz cluster mode misconfig | App config | 2 TicketService instance double-fire hoặc cùng skip (verify `quartz.scheduler.instanceId=AUTO`) |
| RabbitMQ queue đầy | RabbitMQ Mgmt UI | `saga-alert-ticket` queue depth > 1000 |
| Consumer DI crash | Logs | TicketService logs unhandled exception trong startup hoặc consumer pipeline |
| `xmin` concurrency conflict loop | Saga rows | `version` field giữ nguyên dù state đã transit, kèm exception "Database operation expected to affect 1 row(s)" |

## Diagnostic commands

```bash
# Active sagas còn pending
psql "$TICKET_DB" -c "
SELECT correlation_id, current_state, started_at, retry_count
FROM alert_ticket_saga_states
WHERE current_state NOT IN ('Completed', 'Failed')
  AND started_at < now() - interval '15 minutes'
ORDER BY started_at;"

# Quartz triggers pending
psql "$TICKET_DB" -c "
SELECT trigger_name, trigger_state, next_fire_time
FROM qrtz_triggers
WHERE trigger_state != 'COMPLETE'
ORDER BY next_fire_time;"

# RabbitMQ queue depth
rabbitmqctl list_queues name messages consumers | grep saga
```

## Recovery

### Quartz triggers misconfig
1. Verify schema: `\d qrtz_triggers` exists, có data.
2. Restart 1 TicketService instance để re-checkin cluster.
3. Theo dõi `qrtz_scheduler_state` table: instance phải update `last_checkin_time` mỗi 10s.

### Saga rows orphan (Quartz timer mất)
- Manual reprocess qua admin endpoint (xem runbook `08-saga-failed.md`) — coerce vào `Failed` state trước nếu cần.
- Bulk fix:
  ```sql
  UPDATE alert_ticket_saga_states
  SET current_state = 'Failed',
      failure_reason = 'Quartz timer lost (manual mark)',
      failed_at_stage = current_state,
      failed_at = timezone('utc', now())
  WHERE current_state NOT IN ('Completed', 'Failed')
    AND started_at < now() - interval '1 hour';
  ```
- Sau đó reprocess hàng loạt.

### RabbitMQ down
1. Restart RabbitMQ.
2. Quartz timers vẫn persistent → sẽ refire khi consumer back up.
3. Verify queue empty sau 5 phút.

## Verification

- `/health/saga` trả status `Healthy` sau 10 phút.
- `saga_alert_ticket_active_count` trở về < 10.

## Reference

- `overall.md` §53.11 Saga ops.
- Runbook `08-saga-failed.md` cho stage rejection cases.
- Runbook `10-saga-duplicate-canonical.md` cho data conflict.
