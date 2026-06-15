# Runbook 10 — Alert–Ticket Saga Duplicate Canonical Cleanup

> Sprint 5B #239. Liên kết §40.3, §53.6.
>
> **Preflight (chạy 1 lần trước migration `AddAlertTicketSagaFoundation`):**
> dùng để identify và resolve duplicate data → migration mới apply được unique index.

## Vấn đề

Trước Sprint 5B chưa có unique constraint trên `tickets.origin_alert_id`. Có thể tồn tại:
- 1 Alert có ≥ 2 Ticket (duplicate auto-creation, redelivery cũ).
- 1 cặp (BatteryAssetId, Category) có ≥ 2 auto-Ticket active đồng thời.

Migration `AddAlertTicketSagaFoundation` tạo unique filtered index, **sẽ fail** nếu duplicate vẫn tồn tại.

## Preflight queries

### Q1 — Duplicate `OriginAlertId`

```sql
SELECT origin_alert_id, COUNT(*) AS dup_count
FROM tickets
WHERE origin_alert_id IS NOT NULL AND is_deleted = false
GROUP BY origin_alert_id
HAVING COUNT(*) > 1;
```

### Q2 — Duplicate (battery_asset_id, category) active auto

```sql
SELECT battery_asset_id, category, COUNT(*) AS dup_count
FROM tickets
WHERE origin_alert_id IS NOT NULL
  AND is_deleted = false
  AND status NOT IN (6, 7, 8, 9)  -- Resolved=6, ClosedPendingRate=7, Closed=8, ClosedRejected=9
GROUP BY battery_asset_id, category
HAVING COUNT(*) > 1;
```

## Resolution — chọn canonical, soft-delete duplicates

**Canonical rule:**
1. Ticket có status mới hơn (vd InProgress > Open > New).
2. Nếu cùng status: Ticket có `created_at` sớm nhất.
3. Nếu cùng status + created_at: Ticket có `id` nhỏ nhất.

### Mark duplicates soft-deleted

```sql
WITH ranked AS (
  SELECT id, origin_alert_id,
         ROW_NUMBER() OVER (
           PARTITION BY origin_alert_id
           ORDER BY status DESC, created_at ASC, id ASC
         ) AS rn
  FROM tickets
  WHERE origin_alert_id IS NOT NULL AND is_deleted = false
)
UPDATE tickets t
SET is_deleted = true,
    deleted_at = timezone('utc', now())
FROM ranked r
WHERE t.id = r.id AND r.rn > 1
RETURNING t.id, t.origin_alert_id;
```

**Audit log** — chèn `ticket_activities` row cho mỗi ticket bị mark:
```sql
INSERT INTO ticket_activities (id, ticket_id, action, performed_by, performed_at, payload)
SELECT
  gen_random_uuid(),
  t.id,
  'DuplicateCanonicalCleanup',
  NULL,
  timezone('utc', now()),
  jsonb_build_object('runbook', '10-saga-duplicate-canonical', 'sprint', '5B')
FROM tickets t
WHERE t.is_deleted = true
  AND t.deleted_at > timezone('utc', now()) - interval '5 minutes';
```

## Verification post-cleanup

Cả Q1 + Q2 phải trả 0 rows:

```sql
SELECT COUNT(*) FROM (
  SELECT origin_alert_id FROM tickets
  WHERE origin_alert_id IS NOT NULL AND is_deleted = false
  GROUP BY origin_alert_id HAVING COUNT(*) > 1
) x;
-- expect 0
```

Sau đó migration `AddAlertTicketSagaFoundation` chạy OK.

## Rollback

Soft-delete reversible — restore:
```sql
UPDATE tickets
SET is_deleted = false, deleted_at = NULL
WHERE deleted_at IS NOT NULL
  AND id IN (
    SELECT (payload->>'ticket_id')::uuid FROM ticket_activities
    WHERE action = 'DuplicateCanonicalCleanup'
  );
```

## Reference

- `overall.md` §53.6, §53.9 migration order.
- Runbook `08-saga-failed.md` cho post-migration Saga conflict.
