# Chat Hub — Permission Matrix

> **Rà lại với code 2026-08-02** (`ChatAuthorizationService`, `ChatDeleteCommandHandler`,
> `ChatPermissionCodes`, `[Authorize]` trên controller). Bản trước sai ở *Delete any chat*, *Edit*,
> *Restore*, và còn liệt kê PDF Export đã bị gỡ.

## Chat Actions

| Action | Admin | Manager | Staff | Customer | Cơ chế kiểm tra |
|--------|-------|---------|-------|----------|---|
| Read public chats | ✅ | ✅ | ✅ | ✅ | `CanAccessTicket` (chủ ticket / PrimaryHandler / participant / Manager–Admin) |
| Read internal chats | ✅ | ✅ | ✅ | ❌ | permission `chat.view.internal` **hoặc** participant có `canViewInternal` |
| Create public chat | ✅ | ✅ | ✅ | ✅ | permission `chat.create.public` |
| Create internal chat | ✅ | ✅ | ✅ | ❌ | permission `chat.create.internal` |
| **Sửa chat của mình** | ✅¹ | ✅¹ | ✅¹ | ✅¹ | **chỉ tác giả**, trong `Chat:EditWindowMinutes` (mặc định 15') |
| **Sửa chat người khác** | ❌ | ❌ | ❌ | ❌ | `CanEditChat` không có nhánh override → `403` |
| **Xoá chat của mình** | ✅ | ✅ | ✅ | ✅ | **chỉ tác giả** — soft-delete thật, không giới hạn thời gian |
| **Xoá chat người khác** | ⚠️² | ⚠️² | ⚠️² | ⚠️² | Không xoá được — chuyển thành **ẩn-cho-riêng-mình** |
| **Khôi phục chat đã xoá** | ✅ | ❌ | ❌ | ❌ | `PATCH /api/admin/tickets/.../restore` — `[Authorize(Roles="Admin")]` |
| Pin/unpin chat | ✅ | ✅ | ✅ | ❌ | permission `chat.pin` + `[Authorize(Roles="Staff,Manager,Admin")]` |
| React to chat | ✅ | ✅ | ✅ | ✅ | chỉ cần access ticket |

¹ Hết window → `400` `EditWindowExpired`, **kể cả tác giả**.

² ⚠️ **Hành vi dễ hiểu nhầm nhất của module Chat.** Gọi `DELETE .../chats/{id}` trên chat của người
khác **KHÔNG trả `403`** mà trả **`200` + message "Đã ẩn bình luận."**. BE ghi một bản ghi
`TicketChatHide` ⇒ chat chỉ **biến mất với riêng người gọi**, mọi người khác vẫn thấy bình thường.
Chat **không** bị soft-delete. Áp dụng cho **mọi role, kể cả Admin** — không ai xoá được chat của
người khác qua endpoint này.

> ⚠️ **Sửa quan trọng — Manager/Admin KHÔNG sửa/xoá chat của người khác.**
> `CanEditChat`/`CanDeleteChat` chỉ có đúng một nhánh cho phép: `chat.AuthorUserId == actorUserId`.
> Hai hàm này **nhận `actorPermissions` nhưng không dùng tới**. Bản matrix cũ ghi
> "Delete any chat: Admin ✅ Manager ✅ / Restore: Manager ✅" đều **sai**.
>
> Đường duy nhất để Admin can thiệp nội dung người khác là **Admin override**
> (`/api/admin/tickets/{ticketId}/chats/...closed-override`) — nhưng nhóm đó chỉ dành cho **ticket đã
> Closed** và bắt buộc `overrideReason`.
>
> Hệ quả: mô tả `editReason` "bắt buộc khi Manager/Admin sửa chat người khác" **không có đường đi thực
> tế** — nhánh đó bị chặn từ tầng authorization trước khi tới chỗ dùng `editReason`.

## Rate limit ghi chat — `ChatWritePolicy`

| Role | Hạn mức (fixed window 1 phút) | Phạm vi đếm |
|---|---|---|
| Admin | không giới hạn | — |
| Manager | 90 | theo user |
| Staff | 60 | theo user |
| Customer | 30 | **theo từng ticket** |

Vượt hạn → `429` ngay (`QueueLimit = 0`, status trần không bọc `CommonResponse`).
Áp cho 8 endpoint ghi: create · edit · delete · bulk-delete · pin · unpin · voice · voice-retry.

> Doc-comment trong `ChatRateLimitingExtensions.cs` ghi 10/30/60 — **số cũ, không khớp code**.

## KB Integration (#564)

| Action | Admin | Manager | Staff | Customer |
|--------|-------|---------|-------|----------|
| Attach KB article | ✅ | ✅ | ✅ | ❌ |
| Convert to KB draft | ✅ | ✅ | ✅ | ❌ |
| Get KB suggestions | ✅ | ✅ | ✅ | ❌ |

`[Authorize(Roles = "Staff,Manager,Admin")]` trên cả 3 endpoint.

## Escalation Saga (#566)

| Action | Admin | Manager | Staff | Customer |
|--------|-------|---------|-------|----------|
| Trigger saga (qua mention P1) | ✅ | ✅ | ✅ | ❌ |
| ACK escalation review | ✅ | ✅ | ❌ | ❌ |

## GDPR (#569)

| Action | Any Auth | Ghi chú |
|--------|---------|---------|
| Erase own chat data | ✅ | Chỉ chat của chính mình. Response **`data` luôn `null`** — số lượng nằm trong `message` |
| Admin erase user khác | ❌ | Không hỗ trợ |

## Cross-ticket

| Action | Admin | Manager | Staff | Customer |
|--------|-------|---------|-------|----------|
| `GET /api/chats/me` | ✅ | ✅ | ✅ | ✅ |
| `GET /api/chats/mentions/me` | ✅ | ✅ | ✅ | ✅ |
| `GET /api/chats/unread-count` | ✅ | ✅ | ✅ | ✅ |
| `GET /api/chats/search` | ✅ | ✅ | ❌ | ❌ |
| `GET .../chats/{id}/readers` | ✅ | ✅ | ✅ | ❌ |

## SignalR Hub

| Action | Admin | Manager | Staff | Customer |
|--------|-------|---------|-------|----------|
| JoinTicket | ✅ | ✅ | ✅ | ✅ (ticket của mình) |
| LeaveTicket | ✅ | ✅ | ✅ | ✅ |
| Nhận event public | ✅ | ✅ | ✅ | ✅ |
| Nhận event internal | ✅ | ✅ | ✅ | ❌ |
| Typing indicator | ✅ | ✅ | ✅ | ✅ |

> `MentionReceived` gửi qua `Clients.User(...)` — user được mention nhận được **mà không cần `JoinTicket`**.

## Đã gỡ — không còn endpoint

| Action | Ghi chú |
|---|---|
| Export PDF | `GET .../chats/export-pdf` xoá cùng dependency QuestPDF (GH-866) |
| Sentiment check | `POST .../chats/sentiment-check` xoá (GH-866) |
| ACK mention | `PATCH /api/chats/mentions/{id}/acknowledge` xoá (GH-866) |
| Chat từ template | Toàn bộ Chat Template API xoá vĩnh viễn (GH-866) |
