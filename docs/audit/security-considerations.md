# Audit — Security Considerations (#AUDIT-45)

PII handling, retention, GDPR, tamper-evidence của Hybrid Audit Architecture (ADR-0007).

## 1. Tamper-evidence (append-only)
- Mọi bảng `{service}_audit_logs` có **trigger append-only soft mode** (#AUDIT-10/21/25/29/34):
  - **CHẶN DELETE** (raise exception).
  - **CHẶN UPDATE business fields**: `event_id, action_code, actor_account_id, target_id, occurred_at`.
  - Cho phép UPDATE field khác (vd GDPR redact display/ip).
- AuthService nâng cấp từ hard trigger AUTH-29 → soft mode (#AUDIT-10).
- `event_id` unique (idempotency + chống insert trùng).

## 2. PII trong audit
| Field | Chứa PII? | Xử lý |
|-------|-----------|-------|
| actor_display / target_display | ✅ (tên, email) | Redactable qua GDPR endpoint |
| actor_ip | ✅ | Redactable; dùng cho geo + forensic |
| metadata_json | có thể | Sanitize PII trước khi set ở handler |
| reason | có thể | Sanitize trước khi set |
| action_code / event_id / timestamp | ❌ | Giữ vĩnh viễn (audit integrity) |

## 3. GDPR right-to-be-forgotten (#AUDIT-42)
- `POST /api/admin/audit/redact?accountId={id}` (Admin only).
- Redact `actor_display, target_display, actor_ip` → `[REDACTED]` ở `audit_aggregate` (read-store).
- **KHÔNG xóa row** — giữ `event_id, action_code, occurred_at` cho audit integrity.
- **Source tables (`{service}_audit_logs`) KHÔNG redact** — legal hold (regulator yêu cầu trace).
- Hành động redact ghi meta-audit `AccountDataRedacted` (severity=Security).

## 4. Retention asymmetric (#AUDIT-41, D15)
- **Source-of-truth** (`{service}_audit_logs`): retain **1 năm** (compliance/legal hold).
- **Read-store** (`audit_aggregate`): retain **6 tháng** (storage cost), drop partition cũ.
- **EXCEPT severity ∈ {Critical, Security}** → giữ **vĩnh viễn** ở cả 2 tầng.
- Retention chạy daily 03:00 UTC (`AuditRetentionBackgroundService`).

## 5. Access control (D13)
- Aggregator API + GDPR redact: **Admin only** (role `SecurityOfficer` defer cho capstone — gộp Admin).
- Local endpoints: Admin only.
- Permission claims: `audit.read/export/replay/redact` seed cho Admin.

## 6. Geo IP (#AUDIT-16)
- MaxMind GeoLite2 offline DB (no external API call) → không leak data ra ngoài.
- Fallback null nếu thiếu DB — không chặn pipeline.

## 7. Transport
- Outbox pattern: audit ghi atomic cùng business transaction → không mất event khi broker down.
- At-least-once + idempotent consumer (ON CONFLICT event_id) → không duplicate ở read-store.
- Correlation/causation id xuyên service (forensic trace).
