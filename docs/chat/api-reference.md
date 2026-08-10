# Chat Hub — API Reference

> ⚠️ **Bản tra nhanh.** Hợp đồng đầy đủ (request body, response shape, mã lỗi) ở
> [`docs/api-ticket.md`](../api-ticket.md) — mục **Nhóm — Ticket Chats**.
>
> **Đã rà lại với code 2026-08-02.** Bản trước sai nhiều đường dẫn (`/react`, `/mark-as-read`,
> `GET /api/chats`, `restore`) và còn liệt kê endpoint đã xoá — xem mục *Endpoint đã gỡ* cuối file.

Trừ khi ghi rõ, mọi endpoint nằm dưới `/api/tickets/{ticketId}/chats`.
Auth: Bearer JWT. Controller `[Authorize]` — mọi role đã đăng nhập; quyền hẹp hơn ghi theo từng dòng.

## Chat CRUD

| Method | Path | Auth | Mô tả |
|--------|------|------|-------|
| GET | `/api/tickets/{ticketId}/chats` | Mọi role | Danh sách (offset pagination) |
| GET | `/api/tickets/{ticketId}/chats/cursor` | Mọi role | Danh sách (cursor — infinite scroll) |
| GET | `/api/tickets/{ticketId}/chats/{id}` | Mọi role | Chi tiết 1 chat |
| POST | `/api/tickets/{ticketId}/chats` | Mọi role | Tạo chat — **rate limited** |
| PUT | `/api/tickets/{ticketId}/chats/{id}` | Author (15') / Manager–Admin (kèm `editReason`) | Sửa — **rate limited** |
| DELETE | `/api/tickets/{ticketId}/chats/{id}` | Author / Manager / Admin | Soft delete — **rate limited** |
| DELETE | `/api/tickets/{ticketId}/chats/bulk` | Mọi role | Xoá hàng loạt, **tối đa 50 id** — **rate limited** |
| GET | `/api/tickets/{ticketId}/chats/{id}/history` | Mọi role | Lịch sử sửa |
| POST | `/api/tickets/{ticketId}/chats/{id}/replies` | Mọi role | Trả lời (thread 1 cấp) |
| GET | `/api/tickets/{ticketId}/chats/{id}/replies` | Mọi role | Danh sách reply |
| PATCH | `/api/admin/tickets/{ticketId}/chats/{id}/restore` | **Admin** | Khôi phục chat đã xoá mềm |

> ⚠️ `restore` nằm dưới **`/api/admin/tickets/...`** và là **Admin only** — bản cũ ghi
> `/api/tickets/.../restore` + Manager/Admin, cả hai đều sai.

## Reaction & Read receipt

| Method | Path | Auth | Mô tả |
|--------|------|------|-------|
| POST | `.../chats/{id}/reactions` | Mọi role | Thêm reaction — body `{ "reactionType": "ThumbsUp" }` |
| DELETE | `.../chats/{id}/reactions?type=ThumbsUp` | Mọi role | Gỡ reaction — loại truyền qua **query `type`** |
| GET | `.../chats/{id}/reactions` | Mọi role | Tổng hợp reaction |
| POST | `.../chats/mark-read` | Mọi role | Mark-read hàng loạt — body `{ "chatIds": [...] }` |
| GET | `.../chats/{id}/readers` | Staff/Manager/Admin | Ai đã đọc chat |
| GET | `.../chats/unread-count` | Mọi role | Số chưa đọc **của 1 ticket** |
| GET | `/api/chats/unread-count` | Mọi role | Số chưa đọc **toàn bộ ticket** |

> ⚠️ Đường dẫn đúng là **`reactions`** (số nhiều) và **`mark-read`**. Bản cũ ghi `/react`,
> `/react/{emoji}`, `/mark-as-read` — **cả ba đều không tồn tại**. Reaction cũng không dùng emoji
> trên path mà dùng `ReactionTypeEnum` (`ThumbsUp`/`Acknowledged`/`Resolved`/`NeedMoreInfo`/`Disagree`).
>
> ⚠️ **Mark-read ghi bất đồng bộ**: handler chỉ enqueue, `ChatReadReceiptBulkWriter` ghi DB theo batch
> 100 record / 1 giây. Gọi `unread-count` ngay sau đó có thể còn thấy số cũ.

## Attachment

| Method | Path | Auth | Mô tả |
|--------|------|------|-------|
| POST | `.../chats/{id}/attachments` | Mọi role | Thêm 1 attachment |
| POST | `.../chats/{id}/attachments/batch` | Mọi role | Thêm nhiều |
| DELETE | `.../chats/{id}/attachments/{attachmentId}` | Mọi role | Gỡ attachment |
| GET | `.../chats/{id}/attachments` | Mọi role | Danh sách của 1 chat |
| GET | `.../chats/files` | Mọi role | **Toàn bộ file** của ticket |
| GET | `.../chats/{id}/attachments/{attachmentId}/download` | Mọi role | URL tải — `200` sạch · `202` đang quét · `451` nhiễm virus |

> Virus scan **tắt mặc định** (`Chat:Features:EnableVirusScan`); khi tắt thì luôn trả `200`.

## KB Integration (#564)

| Method | Path | Auth | Mô tả |
|--------|------|------|-------|
| POST | `.../chats/{id}/attach-kb` | Staff/Manager/Admin | Gắn bài KB vào chat |
| POST | `.../chats/{id}/to-kb-draft` | Staff/Manager/Admin | Chuyển chat thành KB Draft |
| GET | `.../chats/{id}/kb-suggestions?topN=3` | Staff/Manager/Admin | Gợi ý bài KB (`topN` mặc định **3**) |

### KB Suggestion response — `CommonResponse<KbArticleSuggestDTO[]>`

```json
{
  "isSuccess": true,
  "data": [
    {
      "id": "...",
      "code": "KB-2606-0001",
      "title": "Pin không sạc được khi nhiệt độ thấp",
      "content": "...",
      "helpfulCount": 15,
      "viewCount": 132
    }
  ]
}
```

> ⚠️ `KbArticleSuggestDTO` **không có** field `category` (mẫu cũ ghi sai). Field thật:
> `id`, `code`, `title`, `content`, `helpfulCount`, `viewCount`.

## AI (Gemini / DeepSeek)

| Method | Path | Auth | Mô tả |
|--------|------|------|-------|
| POST | `.../chats/suggest` | Staff/Manager/Admin | Gợi ý 3 nội dung theo `intent` |
| POST | `.../chats/summarize` | Staff/Manager/Admin | Tóm tắt thread |
| POST | `.../chats/{id}/translate?to=en` | Mọi role | Dịch chat |
| POST | `.../chats/voice` | Mọi role | Tạo chat từ audio (**JSON**, trả `202`) — **rate limited** |
| POST | `.../chats/{id}/voice/retry` | Mọi role | Retry transcribe khi `Failed` — **rate limited** |

## Pin

| Method | Path | Auth | Mô tả |
|--------|------|------|-------|
| POST | `.../chats/{id}/pin` | Staff/Manager/Admin | Pin (tối đa 3/ticket) — **rate limited** |
| DELETE | `.../chats/{id}/pin` | Staff/Manager/Admin | Unpin — **rate limited** |

## Escalation Saga (#566)

| Method | Path | Auth | Mô tả |
|--------|------|------|-------|
| POST | `.../chats/{id}/escalation-review/ack` | Manager/Admin | ACK review trong 30 phút |

## Admin override — ticket đã Closed

Base: `/api/admin/tickets/{ticketId}/chats` · **Admin only** · mọi thao tác bắt buộc `overrideReason`.

| Method | Path | Mô tả |
|--------|------|-------|
| POST | `.../closed-override` | Thêm chat vào ticket đã đóng |
| PUT | `.../{id}/closed-override` | Sửa chat trên ticket đã đóng |
| DELETE | `.../{id}/closed-override` | Xoá chat trên ticket đã đóng |

## Cross-ticket

| Method | Path | Auth | Mô tả |
|--------|------|------|-------|
| GET | `/api/chats/me` | Mọi role | Chat do chính mình viết (mọi ticket) |
| GET | `/api/chats/search` | **Manager/Admin** | Tìm chat toàn hệ thống |
| GET | `/api/chats/mentions/me` | Mọi role | Mention của mình |
| POST | `/api/chats/erase-my-data` | Mọi role | GDPR — ẩn danh hoá chat của mình |

> ⚠️ Đường dẫn đúng là **`/api/chats/me`** — bản cũ ghi `GET /api/chats` (không tồn tại).
>
> ⚠️ `erase-my-data` trả **`data: null`**; số lượng đã xoá chỉ nằm trong `message`.

## Rate limit — `ChatWritePolicy`

Áp cho **8 endpoint ghi** (đánh dấu *rate limited* ở trên). Fixed window 1 phút, `QueueLimit = 0`
(vượt hạn → **`429` ngay**, status trần không bọc `CommonResponse`):

| Role | Hạn mức | Phạm vi đếm |
|---|---|---|
| Admin | không giới hạn | — |
| Customer | **30/phút** | theo **từng ticket** |
| Staff | **60/phút** | toàn cục theo user |
| Manager | **90/phút** | toàn cục theo user |

> ⚠️ Doc-comment trong `ChatRateLimitingExtensions.cs` ghi "Customer 10, Staff 30, Manager 60" —
> **số cũ, không khớp code**. Lấy theo `PermitLimit`.

## Notification Preferences (#570)

| Method | Path | Auth | Mô tả |
|--------|------|------|-------|
| GET | `/api/notification-preferences` | Mọi role | Xem preference |
| PUT | `/api/notification-preferences` | Mọi role | Cập nhật |

Field liên quan chat: `notifyOnChat`, `notifyOnMention`, `notifyOnReaction`, `digestWindowMinutes`.
Thuộc **NotificationService** — chi tiết ở [`api-notification.md`](../api-notification.md).

### Ai được báo khi có chat mới *(ADR-0019)*

Danh sách người nhận do **TicketService tính sẵn** và gắn vào `ChatCreatedEvent.RecipientUserIds` —
NotificationService không có bảng assignment lẫn participant nên không tự suy ra được.

**Ứng viên (cả chat công khai lẫn nội bộ):** chủ ticket + primary handler + supporter + participant
còn hoạt động (`removed_at IS NULL`) + **mọi người đã từng nhắn trên ticket**.
`PreviousPrimaryHandler` **không** tính — đã bàn giao thì thôi.
Tác giả của chính tin nhắn đó luôn bị loại.

> Nguồn "đã từng nhắn" tồn tại vì Admin/Manager nhảy vào trả lời một lần **không** được thêm vào
> assignment lẫn participant — chỉ dựa vào hai nguồn kia thì họ nhắn xong là mất hút, không bao giờ
> biết có người trả lời lại.

**Chat công khai:** lấy hết ứng viên.

**Ghi chú nội bộ (`isInternal = true`):** lọc bằng **đúng luật đọc đang chạy** —
`TicketQueryHelper.CanViewInternalChats(roles, participantCanViewInternal)`, tức là:

```
Admin | Manager | Staff   (theo vai trò)
  HOẶC  participant bất kỳ đã được cấp cờ CanViewInternal   (#522)
```

Danh sách "được báo" vì thế **trùng khít** danh sách "đọc được" — không bỏ sót người có quyền, và
không hé nội dung nội bộ cho người không có quyền.

⚠️ **Customer mặc định KHÔNG nhận** thông báo ghi chú nội bộ (`TicketCreateCommandHandler` đặt
`CanViewInternal = false`), **kể cả khi họ đã nhắn công khai trên ticket đó**. Nhưng nếu được cấp cờ
`CanViewInternal = true` qua `PATCH /api/tickets/{ticketId}/participants/{userId}` (chỉ Manager/Admin)
thì họ **đọc được** ghi chú nội bộ, và do đó **cũng được báo** — đây là hành vi có chủ đích của #522,
không phải rò rỉ.

> **Vì sao phải chính xác tới mức này:** nội dung tin nhắn đi **nguyên văn** vào `title`/`body` của
> thông báo đẩy (mẫu `ChatCreated` là `{{Title}}`/`{{Body}}`), nên nó hiện trên **màn hình khoá**.
> Danh sách người nhận là ranh giới bảo mật thật sự chứ không chỉ là chuyện tiện dụng.

**Mỗi người nhận sinh 2 record:** `InApp` (vào feed) + `Push` (dựng thông báo hệ điều hành).
Tiêu đề khác nhau theo loại: `"Tin nhắn mới trên ticket"` / `"Ghi chú nội bộ mới trên ticket"`.

**Payload gửi kèm:**

| Khoá | Type | Nullable | Mô tả |
|---|---|---|---|
| `chatId` | `Guid` | Không | Id tin nhắn |
| `ticketId` | `Guid` | Không | Id ticket |
| `senderName` | `string` | Không | Tên hiển thị người gửi |
| `isInternal` | `bool` | Không | `true` ⇒ ghi chú nội bộ. Client nên hiển thị khác đi (nhãn "Nội bộ") |

**Chat bỏ qua quiet hours, digest và hạn mức** — xem
[Tầng dispatch](../api-notification.md#tầng-dispatch--thứ-tự-cổng-chặn). Muốn tắt thông báo chat thì
tắt kênh Push hoặc tắt nhóm `Chat` trong tuỳ chọn, quiet hours không còn tác dụng với chúng.

## Endpoint đã gỡ — không còn tồn tại

| Endpoint | Ghi chú |
|---|---|
| `GET .../chats/export-pdf` | Gỡ cùng dependency QuestPDF (GH-866) |
| `POST .../chats/sentiment-check` | Gỡ cùng `ChatSentimentCheckDTO` (GH-866) |
| `POST .../chats/from-template/{templateId}` | Gỡ cùng **toàn bộ Chat Template API** (GH-866) |
| `GET/POST/PUT/DELETE /api/chat-templates` | Entity `ChatTemplate` + 2 enum đã xoá vĩnh viễn |
| `PATCH /api/chats/mentions/{id}/acknowledge` | Bỏ cơ chế ACK mention (GH-866) |
| `GET/POST /api/tickets/{ticketId}/comments` | Thay bằng `/chats` từ Sprint Chat |

> ✅ **`chat-hub.postman.json` đã được dọn (2026-08-02).** Gỡ 9 request chết (toàn bộ folder Chat
> Template, PDF export, sentiment-check, ACK mention) + query `unreadOnly` đã bỏ + 2 biến mồ côi
> (`templateId`, `mentionId`). Còn **44 request**, khớp **100%** với controller — không thiếu, không thừa.
