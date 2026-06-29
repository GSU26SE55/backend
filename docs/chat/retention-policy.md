# Chat Retention & GDPR Policy

## Retention Policy

| Field | Value | Config key |
|-------|-------|-----------|
| Active retention | 2 years | `Chat:Retention:ArchiveAfterYears` |
| Archive schedule | Daily 03:00 UTC | `ChatRetentionService` |
| Archive action | `IsDeleted = true` (soft delete) | Row stays in DB |

Chats older than `ArchiveAfterYears` sẽ được soft-delete (không xóa row). Row vẫn còn để audit trail nhưng không trả về trong các API thông thường (mọi query đều filter `.Where(x => !x.IsDeleted)`).

## GDPR Right-to-Erasure

Endpoint: `POST /api/chats/erase-my-data` (Authenticated)

Hành vi:
- Tìm tất cả `TicketChat` có `AuthorUserId == requestingUserId`, chưa bị xóa, chưa bị redact.
- Bulk-update: `Body = "[REDACTED — GDPR erasure]"`, `BodyHtml = null`, `IsRedacted = true`, `RedactedAt = UtcNow`.
- Row **không bị xóa** — `IsDeleted` vẫn `false` để giữ audit trail (ticket activity logs tham chiếu ChatId).
- Idempotent: gọi lại trả 200 "Không có dữ liệu chat cần xóa."

## Redaction vs Archive

| Mechanism | Trigger | Effect | Row deleted? |
|-----------|---------|--------|-------------|
| GDPR Erasure | User request | Body → `[REDACTED]` | No |
| Retention Archive | Daily job, age > 2yr | `IsDeleted = true` | No (soft delete) |

> KHÔNG bao giờ hard-delete chat rows — giữ foreign key integrity với `ticket_activities`, `ticket_chat_mentions`, `ticket_chat_reads`, etc.
