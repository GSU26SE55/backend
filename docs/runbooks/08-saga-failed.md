# Runbook 08 — Alert–Ticket Saga Failed

> Sprint 5B #239. Liên kết §40.3, §53.11.

## Trigger

- Prometheus alert `saga_alert_ticket_failed_total` tăng > 5 trong 5 phút.
- AlertManager → page Thắng + Leader (SEV2).
- NotificationService gửi email/push cho Admin với template `alert-ticket-saga-failed.hbs`.

## Triage (Investigator — 5 phút đầu)

1. Mở Admin portal → Sagas → Alert-Ticket → filter `state = Failed`.
2. Lấy `alertId`, `failedAtStage`, `failureReason`, `errorCode` từ saga detail.
3. Cross-check log:
   ```bash
   kubectl logs -n prod -l app=ticket-service --tail=200 | grep <alertId>
   kubectl logs -n prod -l app=battery-service --tail=200 | grep <alertId>
   ```

## Decision tree theo `failedAtStage`

### Stage = `TicketRequested`

`reason = "Bounded retry exhausted (timeout)"`:
- BatteryService → Saga handler có response không? Kiểm tra `outbox_messages` ở TicketService DB.
- Nếu Saga timeout do RabbitMQ down → fix infra trước, reprocess sau.
- Nếu Saga timeout do Ticket handler exception → check log handler exception, fix bug, deploy, reprocess.

`reason = "Customer/Asset validation failed"`:
- Check `errorCode`. Vd `CUSTOMER_NOT_FOUND`: data sync issue → trigger AccountSyncConsumer retry, then reprocess.
- Manual cleanup nếu cần (xem runbook `10-saga-duplicate-canonical.md`).

### Stage = `AlertLinkRequested`

`reason = "Alert not found"`:
- Alert đã bị soft-delete giữa Saga. Reconcile manual qua admin reconcile command.

`reason = "Alert already linked to different Ticket"`:
- **Data conflict.** Không reprocess — chuyển sang runbook `10-saga-duplicate-canonical.md`.

## Reprocess procedure

```bash
# Get Idempotency-Key (UUID v4)
KEY=$(uuidgen)

curl -X POST \
  -H "Authorization: Bearer $ADMIN_JWT" \
  -H "Idempotency-Key: $KEY" \
  -H "Content-Type: application/json" \
  https://api.example.com/api/v1/admin/sagas/alert-ticket/<alertId>/reprocess
```

Response 202: state reset → republish anomaly event để Saga pick up.
Response 409: state không phải Failed → check filter trước.

## Verification

- Re-check `saga_alert_ticket_started_total` increment.
- Sau 2 phút, state phải transit qua `TicketRequested` → `TicketProvisioned` → `AlertLinkRequested` → `Completed`.
- Nếu lại fail sau reprocess: KHÔNG retry vô hạn — escalate Leader, viết postmortem.

## Postmortem

- Bug fix root cause (handler exception, data sync, infra).
- Update `docs/runbooks/08-saga-failed.md` nếu có pattern mới chưa cover.
- Increment `saga_alert_ticket_failed_total{reason}` reason cardinality nếu có category mới.
