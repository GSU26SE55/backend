# Chat Hub — Operations Runbook

## Common Issues

### 1. Outbox Relay Not Publishing
**Symptom:** Chat events sent but SignalR clients not receiving; `chat_outbox_pending_total` climbing.

**Check:**
```bash
# Check RabbitMQ connection
docker logs rabbitmq | grep -i "error\|disconn"

# Check outbox messages in DB
psql -c "SELECT COUNT(*), min(created_at) FROM outbox_messages WHERE processed_at IS NULL ORDER BY 1;"
```

**Fix:** Restart `OutboxRelayBackgroundService` via pod restart or force a health-check trigger.

---

### 2. ChatEscalationReview Saga Stuck in Pending
**Symptom:** Manager was mentioned on P1 ticket, no escalation after 30 min.

**Check:**
```sql
SELECT * FROM chat_escalation_review_saga_states WHERE current_state = 'Pending';
```

**Fix:** Check Quartz job store (`qrtz_job_details`). If Quartz scheduler is down, restart service — Quartz will re-trigger on reconnect.

---

### 3. ~~PDF Export Fails (500)~~ — endpoint đã gỡ

> 🔴 **Không còn áp dụng (rà 2026-08-02).** `GET /api/tickets/{id}/chats/export-pdf` **đã bị xoá**
> cùng toàn bộ dependency QuestPDF (GH-866) — grep repo không còn tham chiếu `QuestPDF` nào.
> Gọi endpoint này giờ trả **`404`**, không phải `500`. Nếu ai báo lỗi ở đây, câu trả lời là
> **client đang gọi endpoint đã gỡ**, cần bỏ khỏi FE.

### 3b. `429` khi gửi chat dồn dập

**Symptom:** User báo "gửi tin bị chặn" sau vài chục tin trong 1 phút.

**Nguyên nhân:** `ChatWritePolicy` — fixed window 1 phút, `QueueLimit = 0` nên vượt hạn là `429` ngay
(status trần, không bọc `CommonResponse`).

| Role | Hạn mức | Phạm vi |
|---|---|---|
| Admin | không giới hạn | — |
| Manager | 90/phút | theo user |
| Staff | 60/phút | theo user |
| Customer | 30/phút | **theo từng ticket** |

**Lưu ý:** doc-comment trong `ChatRateLimitingExtensions.cs` ghi 10/30/60 — **số cũ**, lấy theo `PermitLimit`.

### 3c. SignalR: client không nhận event khi chạy nhiều replica

**Symptom:** Chat mới hiện với người này nhưng không hiện với người kia; refresh thì thấy.

**Nguyên nhân thường gặp:** thiếu `ConnectionStrings:Redis` ⇒ SignalR chạy **single-instance, không có
backplane**. Client nối instance A không nhận event phát từ instance B. **Fallback im lặng — không log lỗi.**

**Check:** xác nhận `ConnectionStrings:Redis` có giá trị ở mọi replica của TicketService.

---

### 4. GDPR Erase Returns "0 chats"
**Symptom:** User has chats but `POST /api/chats/erase-my-data` says no data.

**Likely cause:** Chats were already redacted (`IsRedacted = true`) or `AuthorUserId` in JWT doesn't match DB.

**Check:**
```sql
SELECT COUNT(*) FROM ticket_chats WHERE author_user_id = '<userId>' AND NOT is_deleted AND NOT is_redacted;
```

---

## Key Metrics

| Metric | Alert Threshold | Dashboard |
|--------|----------------|-----------|
| `chat_outbox_pending_total` | > 500 for 5m | chat-hub.json panel 3 |
| `signalr_connected_users_total` | disconnect storm | chat-hub.json panel 2 |
| `chat_ai_suggest_latency_seconds` p99 | > 2s | chat-hub.json panel 4 |

## Useful Queries

```promql
# Chat events per minute by type
sum(rate(chat_events_total[1m])) by (event_type)

# P1 escalation saga states
# (from DB, not Prometheus)
SELECT current_state, COUNT(*) FROM chat_escalation_review_saga_states GROUP BY 1;

# Pending chat outbox messages
SELECT COUNT(*) FROM outbox_messages WHERE processed_at IS NULL AND event_type LIKE 'Chat%';
```
