# Ticket Chat Hub — Tổng hợp toàn diện

> Tài liệu tổng hợp **hiện trạng** + **kế hoạch mở rộng đầy đủ** cho module Chat trong `TicketService`.
> Bao gồm: tính năng, mục đích nghiệp vụ, cấu trúc source code, database schema, endpoint matrix, integration events, roadmap thực hiện.
>
> **Phạm vi:** chỉ TicketService — không bao gồm thay đổi cross-service (NotificationService, UserService, AI module) ngoài phần Integration Events đã liệt kê.

---

## Mục lục

- [Phần I — Hiện trạng Chat](#phần-i--hiện-trạng-chat)
  - [A. Tính năng đã có](#a-tính-năng-đã-có)
  - [B. Mục đích nghiệp vụ](#b-mục-đích-nghiệp-vụ)
  - [C. Source code structure hiện tại](#c-source-code-structure-hiện-tại)
  - [D. Database tables liên quan](#d-database-tables-liên-quan)
  - [E. Gap / thiếu sót](#e-gap--thiếu-sót)
- [Phần II — Kế hoạch mở rộng (136 feature, 25 nhóm)](#phần-ii--kế-hoạch-mở-rộng-136-feature-25-nhóm)
  - [Nhóm 1: CRUD cơ bản](#nhóm-1-crud-cơ-bản-5-feature)
  - [Nhóm 2: Lịch sử & Audit](#nhóm-2-lịch-sử--audit-3-feature)
  - [Nhóm 3: Attachment nâng cao](#nhóm-3-attachment-nâng-cao-8-feature)
  - [Nhóm 4: Threaded Reply](#nhóm-4-threaded-reply-3-feature)
  - [Nhóm 5: @Mention](#nhóm-5-mention-5-feature)
  - [Nhóm 6: Reaction](#nhóm-6-reaction-4-feature)
  - [Nhóm 7: Read Receipts](#nhóm-7-read-receipts-5-feature)
  - [Nhóm 8: Rich Content / Markdown](#nhóm-8-rich-content--markdown-5-feature)
  - [Nhóm 9: Realtime SignalR](#nhóm-9-realtime-signalr-6-feature)
  - [Nhóm 10: Template / Canned Response](#nhóm-10-template--canned-response-7-feature)
  - [Nhóm 11: AI-Assist](#nhóm-11-ai-assist-7-feature)
  - [Nhóm 12: Search & Filter](#nhóm-12-search--filter-8-feature)
  - [Nhóm 13: Pin / Highlight](#nhóm-13-pin--highlight-4-feature)
  - [Nhóm 14: Translation](#nhóm-14-translation-4-feature)
  - [Nhóm 15: Metrics / Analytics](#nhóm-15-metrics--analytics-6-feature)
  - [Nhóm 16: Notification](#nhóm-16-notification-6-feature)
  - [Nhóm 17: Authorization](#nhóm-17-authorization-4-feature)
  - [Nhóm 18: Validation & Security](#nhóm-18-validation--security-8-feature)
  - [Nhóm 19: Performance & Caching](#nhóm-19-performance--caching-5-feature)
  - [Nhóm 20: Integration Events / Outbox](#nhóm-20-integration-events--outbox-6-feature)
  - [Nhóm 21: SLA Integration](#nhóm-21-sla-integration-3-feature)
  - [Nhóm 22: Knowledge Base Integration](#nhóm-22-knowledge-base-integration-3-feature)
  - [Nhóm 23: Mobile-specific](#nhóm-23-mobile-specific-4-feature)
  - [Nhóm 24: Export / Compliance](#nhóm-24-export--compliance-5-feature)
  - [Nhóm 25: Participant Management](#nhóm-25-participant-management-12-feature)
- [Phần III — Tổng kết quy mô](#phần-iii--tổng-kết-quy-mô)
- [Phần IV — Database schema chi tiết](#phần-iv--database-schema-chi-tiết)
- [Phần V — API endpoint matrix](#phần-v--api-endpoint-matrix)
- [Phần VI — Integration events matrix](#phần-vi--integration-events-matrix)
- [Phần VII — Source code structure đầy đủ](#phần-vii--source-code-structure-đầy-đủ)
- [Phần VIII — Roadmap thực hiện theo dependency](#phần-viii--roadmap-thực-hiện-theo-dependency)
- [Phần IX — Configuration & feature flags](#phần-ix--configuration--feature-flags)
- [Phần X — Testing matrix](#phần-x--testing-matrix)

---

# Phần I — Hiện trạng Chat

## A. Tính năng đã có

| # | Feature | Endpoint / Vị trí | Mục đích |
|---|---------|-------------------|----------|
| 1 | Add chat | `POST /api/tickets/{ticketId}/chats` | Customer/Staff/Manager đăng bình luận |
| 2 | List chat (pagination) | `GET /api/tickets/{ticketId}/chats` | Hiển thị timeline trao đổi |
| 3 | `IsInternal` flag | Field trong entity | Phân biệt public (Customer thấy) vs internal (Staff/Manager only) |
| 4 | Attachment đính kèm khi add | `ChatAddCommand.Attachments` | Gửi ảnh/file kèm chat |
| 5 | Filter internal cho Customer | `TicketChatsQueryHandler:44-47` | Bảo mật thông tin nội bộ |
| 6 | Authorize access ticket | `TicketQueryHelper.CanAccessTicket` | Bảo mật ticket — Customer chủ / Staff assigned / Manager/Admin |
| 7 | Activity log khi add chat | `_activityLogger.LogAsync(...Chatted)` | Audit trail |
| 8 | Soft delete support | Qua `AuditableEntity` + filter `!IsDeleted` manual | Không xóa hẳn |
| 9 | Pagination | `PageNumber + PageSize` | Phân trang khi list dài |
| 10 | Validation pipeline | `IValidatable<TicketActionResponse>` | Body required, attachment field required |

## B. Mục đích nghiệp vụ

1. **Kênh trao đổi Customer ↔ Staff** trong vòng đời ticket — thay điện thoại/email rời rạc, gom timeline vào ticket.
2. **Ghi chú nội bộ Staff ↔ Manager** (`IsInternal=true`) — bàn giao kỹ thuật khi reassign, escalate Tier 1 → Tier 2/3, Manager review trước close.
3. **Đính kèm bằng chứng** — ảnh hiện trạng pin, biên bản, file log sensor.
4. **Audit trail cho SLA dispute** — timeline có evidence ai trả lời lúc nào.
5. **Phục vụ ITIL ticket lifecycle**:
   - OPEN → ASSIGNED: Manager chat internal về rationale priority
   - ASSIGNED → IN_PROGRESS: Staff chat "đã tiếp nhận", hỏi Customer info
   - IN_PROGRESS: Log progress, internal note khi escalate
   - RESOLVED → CLOSED_PENDING_RATE: Staff giải thích cách xử lý
   - ESCALATED: Manager/Tier cao hơn chat hướng đi mới

## C. Source code structure hiện tại

```
services/TicketService/src/
├── TicketService.Api/
│   └── Controllers/
│       └── TicketChatsController.cs              # 2 endpoint POST + GET
│
├── TicketService.Application/
│   ├── CQRS/
│   │   ├── Command/
│   │   │   └── ChatAdd/
│   │   │       └── ChatAddCommand.cs             # Command + Validation + ChatAttachmentInput record
│   │   ├── Query/
│   │   │   └── Ticket/
│   │   │       └── TicketChatsQuery.cs           # Pagination + ActorRoles
│   │   └── Handler/
│   │       └── Chats/
│   │           ├── ChatAddCommandHandler.cs      # Tạo chat + attachment + activity log
│   │           └── TicketChatsQueryHandler.cs    # Authorize + filter internal + paginate
│   ├── DTOs/
│   │   └── Response/
│   │       ├── Chats/
│   │       │   ├── ChatResponse.cs
│   │       │   └── ChatActionDTO.cs
│   │       └── Tickets/
│   │           └── TicketChatDTO.cs              # DTO trả về cho client
│   ├── Helpers/
│   │   └── TicketQueryHelper.cs                     # CanAccessTicket + CanViewInternalChats
│   └── Interfaces/
│       ├── Repositories/
│       │   └── ITicketUnitOfWork.cs                 # TicketChats repository
│       └── Services/
│           ├── ITicketCurrentUserService.cs         # Resolve UserId, Role, FullName
│           └── IActivityLogger.cs                   # Log Chatted action
│
├── TicketService.Domain/
│   ├── Entities/
│   │   ├── TicketChat.cs                         # TicketId, AuthorUserId, AuthorRole, Body, IsInternal, AttachmentFileIds
│   │   ├── Ticket.cs                                # Có Chats collection
│   │   ├── TicketAttachment.cs                      # Ghi cùng khi có attachment
│   │   └── TicketActivity.cs                        # Log Chatted action
│   └── Enums/
│       ├── ActorRoleEnum.cs                         # Customer/Staff/Manager/Admin
│       ├── ActivityActionEnum.cs                    # Chatted
│       └── AttachmentSourceEnum.cs                  # CustomerSubmission/StaffWork
│
├── TicketService.Infrastructure/
│   ├── Persistence/
│   │   ├── Configurations/
│   │   │   ├── TicketChatConfiguration.cs        # Map → "ticket_chats", jsonb cho AttachmentFileIds
│   │   │   ├── TicketConfiguration.cs
│   │   │   ├── TicketAttachmentConfiguration.cs
│   │   │   └── TicketActivityConfiguration.cs
│   │   └── Converters/
│   │       └── JsonValueConverter.cs                # Convert List<Guid> ↔ jsonb
│   └── Migrations/
│       └── 20260517105233_InitialTicketSchema.cs    # CreateTable ticket_chats
│
└── tests/
    ├── TicketService.UnitTests/Handlers/Chats/
    │   └── ChatAddCommandHandlerTests.cs
    └── TicketService.IntegrationTests/Tickets/
        └── TicketChatApiTests.cs
```

## D. Database tables liên quan

### Bảng chính: `ticket_chats`

| Column | Type | Constraint | Ghi chú |
|--------|------|-----------|---------|
| `id` | uuid | PK | |
| `ticket_id` | uuid | FK → `tickets.id` ON DELETE CASCADE | |
| `author_user_id` | uuid | NOT NULL | |
| `author_role` | int | NOT NULL | `ActorRoleEnum` |
| `author_display_name` | varchar(256) | nullable | Snapshot tên author |
| `body` | text | NOT NULL | |
| `is_internal` | bool | NOT NULL | Customer không thấy nếu `true` |
| `attachment_file_ids` | **jsonb** | NOT NULL | `List<Guid>` JSON — liên kết LỎNG, không FK |
| `created_at` | timestamptz | NOT NULL | |
| `created_by` | uuid | nullable | |
| `updated_at` | timestamptz | nullable | |
| `is_deleted` | bool | NOT NULL | |
| `deleted_at` | timestamptz | nullable | |

**Indexes:** `IX_ticket_chats_ticket_id`, `IX_ticket_chats_author_user_id`

### Bảng phụ thuộc

| Bảng | Vai trò trong chat flow | Quan hệ |
|------|----------------------------|---------|
| `tickets` | Parent — FK cascade | Chat chỉ sống khi ticket tồn tại |
| `ticket_attachments` | Ghi cùng khi có file đính kèm | Liên kết LỎNG qua jsonb `attachment_file_ids` |
| `ticket_activities` | Audit log Action=Chatted | Mỗi add chat ghi 1 dòng |
| `customer_accounts` / `staff_accounts` | Authorize CanAccessTicket | Không join trực tiếp |

## E. Gap / thiếu sót

| # | Gap | Tác động |
|---|-----|----------|
| 1 | **Không có Edit endpoint** | Lỡ gõ sai phải xóa, không sửa được |
| 2 | **Không có Delete endpoint** | Không thể remove spam/sai sót |
| 3 | **Không có edit history** | Không trace lại nội dung cũ |
| 4 | **Attachment liên kết LỎNG** (jsonb, không FK) | Không cascade cleanup, không integrity DB |
| 5 | **Không có reply / mention / reaction** | UX kém so với chat hiện đại |
| 6 | **Không có realtime** (chưa SignalR) | FE phải polling |
| 7 | **Không có read receipt** | Không biết Customer đã đọc chưa |
| 8 | **Không có participant management** | Reassign Staff cũ vẫn xem được; không add chuyên gia ngoài |
| 9 | **Không có search trong chat** | Ticket dài 50+ chat khó tìm |
| 10 | **Không có template** | Staff gõ lại cùng nội dung |
| 11 | **XML doc nói sort ASC** nhưng code thực tế **DESC** (`TicketChatsQueryHandler.cs:51`) | Mismatch document |
| 12 | **Không filter `!IsDeleted` ở Ticket parent khi GET chat** | Có thể trả chat của ticket đã xóa (cần kiểm tra) |

---

# Phần II — Kế hoạch mở rộng (136 feature, 25 nhóm)

## Nhóm 1: CRUD cơ bản (5 feature)

### Tính năng

| # | Feature | Mục đích |
|---|---------|----------|
| 1 | Edit chat | Sửa nội dung chat đã đăng |
| 2 | Soft delete chat | Xóa mềm chat, giữ audit |
| 3 | Restore chat | Khôi phục chat đã xóa (Admin only) |
| 4 | Get chat by ID | Xem chi tiết 1 chat |
| 5 | Get my chats cross-ticket | Customer xem mọi chat của mình trên toàn bộ ticket |

### Source structure

```
Application/CQRS/Command/
├── ChatEdit/ChatEditCommand.cs
├── ChatDelete/ChatDeleteCommand.cs
└── ChatRestore/ChatRestoreCommand.cs

Application/CQRS/Query/Chat/
├── ChatGetByIdQuery.cs
└── MyChatsQuery.cs

Application/CQRS/Handler/Chats/
├── ChatEditCommandHandler.cs
├── ChatDeleteCommandHandler.cs
├── ChatRestoreCommandHandler.cs
├── ChatGetByIdQueryHandler.cs
└── MyChatsQueryHandler.cs

Api/Controllers/TicketChatsController.cs (mở rộng PUT, DELETE, PATCH, GET /me)
```

### Rule

- Edit: Author trong **15 phút**; Manager/Admin bất cứ lúc nào (kèm `edit_reason`)
- Delete: Author soft-delete được của mình; Manager/Admin của mọi người
- Restore: Admin only (policy `AdminOnly`)
- Block edit/delete khi ticket `CLOSED`

---

## Nhóm 2: Lịch sử & Audit (3 feature)

### Tính năng

| # | Feature | Mục đích |
|---|---------|----------|
| 6 | Edit history (versioning) | Xem các bản cũ của chat, không bao giờ xóa |
| 7 | Edit window limit (15 phút) | Tránh sửa sau khi đã có reply |
| 8 | Edit reason | Bắt buộc nếu Manager/Admin edit chat người khác (PII/legal redaction) |

### Source structure

```
Domain/Entities/
└── TicketChatEdit.cs                             # Bảng mới: lưu old_body, new_body, edited_at, edited_by, edit_reason

Infrastructure/Persistence/Configurations/
└── TicketChatEditConfiguration.cs

Application/CQRS/Query/Chat/
└── ChatHistoryQuery.cs

Application/CQRS/Handler/Chats/
└── ChatHistoryQueryHandler.cs

Migrations/
└── AddChatEditHistory.cs                         # tạo ticket_chat_edits + thêm edited_at, edit_count, last_edited_by_user_id vào ticket_chats
```

---

## Nhóm 3: Attachment nâng cao (8 feature)

### Tính năng

| # | Feature | Mục đích |
|---|---------|----------|
| 9 | Link attachment với chat_id (FK chặt) | Cascade cleanup khi chat xóa |
| 10 | Add attachment sau khi đã tạo chat | Bổ sung file sau |
| 11 | Remove attachment khỏi chat | Quản lý linh hoạt |
| 12 | Thumbnail ảnh | UX hiển thị preview |
| 13 | Inline image trong body (markdown `![]()`) | Chèn ảnh giữa text |
| 14 | Download count | Tracking lượt tải |
| 15 | Virus scan (ClamAV) | Bảo mật |
| 16 | Limit file size/type/count | Bảo mật + storage cap |

### Source structure

```
Domain/Entities/
└── TicketAttachment.cs                              # Thêm: chat_id (FK nullable), thumbnail_url, is_inline, download_count, virus_scan_status

Domain/Enums/
└── VirusScanStatusEnum.cs                           # Pending=1, Clean=2, Infected=3, Failed=4

Application/CQRS/Command/
├── ChatAttachmentAdd/ChatAttachmentAddCommand.cs
└── ChatAttachmentRemove/ChatAttachmentRemoveCommand.cs

Application/CQRS/Query/Chat/
└── ChatAttachmentsQuery.cs

Application/CQRS/Handler/Chats/
├── ChatAttachmentAddCommandHandler.cs
├── ChatAttachmentRemoveCommandHandler.cs
└── ChatAttachmentsQueryHandler.cs

Infrastructure/BackgroundServices/
└── VirusScanWorker.cs                               # Gọi ClamAV, update virus_scan_status

Migrations/
├── LinkAttachmentToChat.cs                       # Add chat_id FK
└── AddChatAttachmentEnhancements.cs              # thumbnail_url, is_inline, download_count, virus_scan_status
```

### Constraint

- Max 10 attachment/chat
- Max 50MB/file
- Whitelist MIME: `image/*`, `application/pdf`, `video/mp4`, `text/plain`
- Block download nếu `virus_scan_status = Infected`

---

## Nhóm 4: Threaded Reply (3 feature)

### Tính năng

| # | Feature | Mục đích |
|---|---------|----------|
| 17 | Reply 1 chat cụ thể | Trả lời từng câu hỏi của Customer/Staff |
| 18 | Đếm số reply | Hiển thị "3 phản hồi" |
| 19 | Threaded view mode | Group theo `thread_root_id` |

### Source structure

```
Domain/Entities/
└── TicketChat.cs                                 # Thêm: parent_chat_id (self-FK), thread_root_id, reply_count

Application/CQRS/Command/
└── ChatReply/ChatReplyCommand.cs

Application/CQRS/Query/Chat/
└── ChatRepliesQuery.cs

Application/CQRS/Handler/Chats/
├── ChatReplyCommandHandler.cs
└── ChatRepliesQueryHandler.cs

Migrations/
└── AddChatThreading.cs
```

### Rule

- **Tối đa 1 level reply** (không reply-of-reply)
- Validate: `parent_chat_id` phải cùng `ticket_id`
- Soft delete parent: reply vẫn hiển thị, parent đánh dấu "đã xóa"

---

## Nhóm 5: @Mention (5 feature)

### Tính năng

| # | Feature | Mục đích |
|---|---------|----------|
| 20 | Mention user `@username` trong body | Tag người liên quan |
| 21 | Notify khi được mention | Push + email qua NotificationService |
| 22 | List mention của tôi | Xem mention chưa acknowledge |
| 23 | Acknowledge mention | Đánh dấu đã xem |
| 24 | Mention nhóm (team/role) | `@team:tier2-staff`, `@role:manager` |

### Source structure

```
Domain/Entities/
└── TicketChatMention.cs                          # Bảng mới: chat_id, mentioned_user_id, mentioned_user_role, is_acknowledged

Application/Interfaces/Services/
└── IMentionParser.cs                                # Parse @username từ body

Infrastructure/Services/
└── MentionParserService.cs                          # Regex + resolve userId qua UserService cache Redis

Application/CQRS/Command/
└── ChatMentionAcknowledge/ChatMentionAcknowledgeCommand.cs

Application/CQRS/Query/Chat/
└── MyMentionsQuery.cs

Application/CQRS/Handler/Chats/
├── ChatMentionAcknowledgeCommandHandler.cs
└── MyMentionsQueryHandler.cs

SharedContracts/Events/Ticket/
└── ChatMentionedEvent.cs

Infrastructure/Persistence/Configurations/
└── TicketChatMentionConfiguration.cs

Migrations/
└── AddChatMentions.cs
```

### Integration

- Sau khi `_uow.SaveChangesAsync()` thành công → publish `ChatMentionedEvent` qua Outbox
- `NotificationService` consume → gửi push (Expo) + email

---

## Nhóm 6: Reaction (4 feature)

### Tính năng

| # | Feature | Mục đích |
|---|---------|----------|
| 25 | Add reaction | 👍 / ✅ / ❓ / 👎 / ⚠️ |
| 26 | Remove reaction | Toggle off |
| 27 | List reactions per chat | Ai đã react gì |
| 28 | Aggregate count theo type | Hiển thị "3 👍, 1 ✅" |

### Source structure

```
Domain/Entities/
└── TicketChatReaction.cs                         # Bảng mới: chat_id, user_id, reaction_type — UNIQUE (chat_id, user_id, type)

Domain/Enums/
└── ReactionTypeEnum.cs                              # ThumbsUp=1, Acknowledged=2, Resolved=3, NeedMoreInfo=4, Disagree=5

Application/CQRS/Command/
├── ChatReactionAdd/ChatReactionAddCommand.cs
└── ChatReactionRemove/ChatReactionRemoveCommand.cs

Application/CQRS/Query/Chat/
└── ChatReactionsQuery.cs

Application/CQRS/Handler/Chats/
├── ChatReactionAddCommandHandler.cs
├── ChatReactionRemoveCommandHandler.cs
└── ChatReactionsQueryHandler.cs

SharedContracts/Events/Ticket/
└── ChatReactedEvent.cs

Migrations/
└── AddChatReactions.cs
```

---

## Nhóm 7: Read Receipts (5 feature)

### Tính năng

| # | Feature | Mục đích |
|---|---------|----------|
| 29 | Mark as read | Đánh dấu đã đọc |
| 30 | Auto mark-read | Tự đánh dấu khi user gọi GetList |
| 31 | Xem ai đã đọc | Staff/Manager kiểm tra Customer đã thấy update chưa |
| 32 | Unread count per ticket | Badge "3 chưa đọc" |
| 33 | Cảnh báo Customer chưa đọc N chat trong M giờ | Manager proactive escalate |

### Source structure

```
Domain/Entities/
└── TicketChatRead.cs                             # Bảng mới: chat_id, user_id, user_role, read_at — UNIQUE (chat_id, user_id)

Application/CQRS/Command/
└── ChatMarkRead/ChatMarkReadCommand.cs

Application/CQRS/Query/Chat/
├── ChatReadersQuery.cs
└── TicketUnreadCountQuery.cs

Application/CQRS/Handler/Chats/
├── ChatMarkReadCommandHandler.cs
├── ChatReadersQueryHandler.cs
└── TicketUnreadCountQueryHandler.cs

Infrastructure/BackgroundServices/
└── ChatReadReceiptBulkWriter.cs                  # Bulk insert read receipts (channel + batch)

Migrations/
└── AddChatReadReceipts.cs
```

---

## Nhóm 8: Rich Content / Markdown (5 feature)

### Tính năng

| # | Feature | Mục đích |
|---|---------|----------|
| 34 | Markdown support | Bold, italic, list, code block, link |
| 35 | XSS sanitize | Bảo mật |
| 36 | Code block syntax highlight | Paste log đẹp (FE Prism.js render) |
| 37 | Auto-link URL | Biến URL thành link clickable |
| 38 | Emoji picker + render | UX |

### Source structure

```
Domain/Entities/
└── TicketChat.cs                                 # Thêm: body_format (enum), body_html (cached render)

Domain/Enums/
└── ChatBodyFormatEnum.cs                         # PlainText=1, Markdown=2

Application/Interfaces/Services/
└── IMarkdownRenderer.cs

Infrastructure/Services/
└── MarkdigMarkdownRenderer.cs                       # Markdig + Ganss.Xss whitelist tag

Migrations/
└── AddChatMarkdownSupport.cs
```

### Whitelist tag

`<p>, <strong>, <em>, <code>, <pre>, <ul>, <ol>, <li>, <a>, <blockquote>, <br>, <img>` (img chỉ khi src trùng attachment của ticket)

---

## Nhóm 9: Realtime SignalR (6 feature)

### Tính năng

| # | Feature | Mục đích |
|---|---------|----------|
| 39 | Push chat mới tới mọi client đang xem ticket | Realtime UX |
| 40 | Push edit/delete | Đồng bộ state |
| 41 | Push reaction | Realtime emoji |
| 42 | Typing indicator | "X đang gõ..." |
| 43 | Push mention trực tiếp | Bắn riêng cho user được mention |
| 44 | Online presence | Hiển thị ai đang xem ticket |

### Source structure

```
Api/Hubs/
└── TicketChatHub.cs                              # Path: /hubs/ticket-chats

Api/Program.cs                                       # AddSignalR + AddStackExchangeRedis (backplane multi-instance)

Api/Authentication/
└── SignalRJwtConfiguration.cs                       # JWT qua query string ?access_token=...

Application/Interfaces/Services/
└── ITicketChatRealtimeNotifier.cs

Infrastructure/Realtime/
└── SignalRTicketChatNotifier.cs                  # Inject IHubContext, broadcast to group ticket-{id}
```

### Server-push events

- `ChatAdded(chatDto)`
- `ChatEdited(chatDto)`
- `ChatDeleted(chatId, byUser)`
- `ReactionAdded(chatId, reaction)`
- `UserTyping(ticketId, userId, displayName)`
- `MentionReceived(chatDto)` — chỉ gửi cho user được mention

### Client method

- `JoinTicket(ticketId)` — verify quyền truy cập rồi mới add vào group
- `LeaveTicket(ticketId)`
- `Typing(ticketId)` — broadcast "X đang gõ..."

> Tham khảo pattern có sẵn: `services/SmsService/src/SmsService.Infrastructure/Realtime/SmsGatewayHub.cs`

---

## Nhóm 10: Template / Canned Response (7 feature)

### Tính năng

| # | Feature | Mục đích |
|---|---------|----------|
| 45 | Tạo template cá nhân | Staff tự dùng |
| 46 | Tạo template chung | Manager/Admin tạo Global/Team |
| 47 | Apply template vào chat | Quick paste — auto fill placeholder |
| 48 | Placeholder động | `{{customer_name}}`, `{{ticket_code}}`, `{{battery_id}}` |
| 49 | Phân loại template | Category enum |
| 50 | Usage stats | Template nào dùng nhiều |
| 51 | Share trong team | Team scope |

### Source structure

```
Domain/Entities/
└── ChatTemplate.cs                               # Bảng mới: name, content, category, scope, team_id, usage_count, is_active

Domain/Enums/
├── ChatTemplateCategoryEnum.cs                   # Greeting=1, RequestInfo=2, Update=3, Resolution=4, Internal=5, Other=6
└── ChatTemplateScopeEnum.cs                      # Personal=1, Team=2, Global=3

Application/Interfaces/Services/
└── ITemplateRenderer.cs

Infrastructure/Services/
└── TemplateRendererService.cs                       # Resolve placeholder, validate template

Application/CQRS/Command/
├── ChatTemplateCreate/ChatTemplateCreateCommand.cs
├── ChatTemplateUpdate/ChatTemplateUpdateCommand.cs
├── ChatTemplateDelete/ChatTemplateDeleteCommand.cs
└── ChatFromTemplate/ChatFromTemplateCommand.cs

Application/CQRS/Query/Template/
└── ChatTemplatesQuery.cs

Application/CQRS/Handler/Templates/
├── ChatTemplateCreateCommandHandler.cs
├── ChatTemplateUpdateCommandHandler.cs
├── ChatTemplateDeleteCommandHandler.cs
├── ChatFromTemplateCommandHandler.cs
└── ChatTemplatesQueryHandler.cs

Api/Controllers/
└── ChatTemplatesController.cs                    # Controller mới

Migrations/
└── AddChatTemplates.cs
```

---

## Nhóm 11: AI-Assist (7 feature)

### Tính năng

| # | Feature | Mục đích |
|---|---------|----------|
| 52 | Suggest chat | AI gợi ý reply cho Staff |
| 53 | Multiple candidates | 3 phương án để chọn |
| 54 | Theo intent | RequestInfo / TechAnswer / Resolution / FollowUp |
| 55 | Log AI usage | Train improve model |
| 56 | Mask PII trước gửi AI | Bảo mật |
| 57 | Sentiment analysis | Detect Customer tức giận để alert |
| 58 | Auto-summarize thread dài | Tóm tắt chat dài cho Staff mới tiếp nhận |

### Source structure

```
Domain/Entities/
└── ChatAiSuggestion.cs                           # Bảng mới: ticket_id, intent, suggestions (jsonb), selected_index, edited_before_post

Domain/Enums/
└── ChatAiIntentEnum.cs                           # RequestInfo=1, TechnicalAnswer=2, Resolution=3, FollowUp=4

Application/Interfaces/Services/
├── IChatAiSuggestionClient.cs                    # HTTP client gọi AI module FastAPI
└── IPiiDetector.cs

Application/CQRS/Command/
└── ChatSuggest/ChatSuggestCommand.cs

Application/CQRS/Handler/Chats/
└── ChatSuggestCommandHandler.cs

Infrastructure/AiClient/
└── FastApiChatAiClient.cs                        # POST /ai/chat-suggest

Infrastructure/Services/
└── PiiDetectorService.cs                            # Regex CCCD, sđt, email — mask trước khi gửi AI

Migrations/
└── AddChatAiSuggestions.cs
```

### Flow

1. Staff gõ vài chữ → click "AI suggest"
2. Backend mask PII → gọi AI module với context (ticket title + last 5 chat + sensor data)
3. Trả 3 suggestion → Staff chọn 1 → có thể edit → post
4. Log selection vào `chat_ai_suggestions` để improve model

---

## Nhóm 12: Search & Filter (8 feature)

### Tính năng

| # | Feature | Mục đích |
|---|---------|----------|
| 59 | Full-text search tiếng Việt có dấu | Tìm trong body chat |
| 60 | Filter authorRole | Lọc theo role |
| 61 | Filter authorUserId | Lọc theo user cụ thể |
| 62 | Filter `isInternal` | Lọc loại |
| 63 | Filter `hasAttachment` | Có file không |
| 64 | Filter time range | Theo ngày |
| 65 | Filter `reactionType` | Có react không |
| 66 | Global cross-ticket search | Admin/Manager compliance lookup |

### Source structure

```
Domain/Entities/
└── TicketChat.cs                                 # Thêm: body_tsv (tsvector cho Postgres)

Application/CQRS/Query/Chat/
├── TicketChatsQuery.cs                           # Mở rộng filter params
└── ChatGlobalSearchQuery.cs

Application/CQRS/Handler/Chats/
└── ChatGlobalSearchQueryHandler.cs

Infrastructure/Persistence/Configurations/
└── TicketChatConfiguration.cs                    # Configure tsvector + GIN index

Migrations/
└── AddChatFullTextSearch.cs                      # + trigger Postgres tự update body_tsv khi body đổi
```

### SQL

```sql
CREATE INDEX IX_ticket_chats_body_tsv
ON ticket_chats USING gin(body_tsv);

-- Trigger update tsv khi insert/update
CREATE TRIGGER ticket_chats_tsv_trigger
BEFORE INSERT OR UPDATE ON ticket_chats
FOR EACH ROW EXECUTE FUNCTION tsvector_update_trigger(body_tsv, 'pg_catalog.simple', body);
```

---

## Nhóm 13: Pin / Highlight (4 feature)

### Tính năng

| # | Feature | Mục đích |
|---|---------|----------|
| 67 | Pin chat | Ghim lên đầu timeline |
| 68 | Unpin | Bỏ ghim |
| 69 | Giới hạn 3 pinned/ticket | Tránh spam pin |
| 70 | Highlight (color/badge) | Nổi bật visually |

### Source structure

```
Domain/Entities/
└── TicketChat.cs                                 # Thêm: is_pinned, pinned_at, pinned_by_user_id

Application/CQRS/Command/
├── ChatPin/ChatPinCommand.cs
└── ChatUnpin/ChatUnpinCommand.cs

Application/CQRS/Handler/Chats/
├── ChatPinCommandHandler.cs
└── ChatUnpinCommandHandler.cs

Migrations/
└── AddChatPinning.cs
```

### Rule

- Chỉ Manager/Admin/Staff được pin
- Sort khi GetList: `is_pinned DESC, created_at DESC`

---

## Nhóm 14: Translation (4 feature)

### Tính năng

| # | Feature | Mục đích |
|---|---------|----------|
| 71 | Auto-detect ngôn ngữ | Lưu `original_language` |
| 72 | Dịch chat VN ↔ EN | Lazy translate khi user click |
| 73 | Cache bản dịch | TTL 30 ngày + Redis |
| 74 | Hiển thị song ngữ | UX |

### Source structure

```
Domain/Entities/
├── TicketChat.cs                                 # Thêm: original_language (varchar 5)
└── TicketChatTranslation.cs                      # Bảng mới: chat_id, target_language, translated_body, provider

Domain/Enums/
└── TranslationProviderEnum.cs                       # GoogleTranslate=1, DeepL=2, Manual=3

Application/Interfaces/Services/
└── ITranslationProvider.cs

Infrastructure/Translation/
└── GoogleTranslateProvider.cs

Application/CQRS/Command/
└── ChatTranslate/ChatTranslateCommand.cs

Application/CQRS/Handler/Chats/
└── ChatTranslateCommandHandler.cs

Migrations/
└── AddChatTranslations.cs
```

---

## Nhóm 15: Metrics / Analytics (6 feature)

### Tính năng

| # | Feature | Mục đích |
|---|---------|----------|
| 75 | Avg response time của Staff | KPI |
| 76 | Số chat/ticket trung bình | KPI |
| 77 | % ticket có internal note | Quality indicator |
| 78 | Mention count per user | Workload signal |
| 79 | Manager dashboard | So sánh Staff với team avg |
| 80 | Chat heatmap theo giờ | Activity pattern |

### Source structure

```
Domain/Entities/
└── ChatMetricsDaily.cs                           # Bảng mới: date, staff_id, ticket_id, chat_count, avg_response_time_min

Infrastructure/BackgroundServices/
└── ChatMetricsAggregatorService.cs               # Hosted service, chạy mỗi giờ

Application/CQRS/Query/Metrics/
└── ChatMetricsQuery.cs

Application/CQRS/Handler/Metrics/
└── ChatMetricsQueryHandler.cs

Api/Controllers/
└── AdminChatMetricsController.cs                 # Endpoint cho dashboard

Migrations/
└── AddChatMetrics.cs
```

---

## Nhóm 16: Notification (6 feature)

### Tính năng

| # | Feature | Mục đích |
|---|---------|----------|
| 81 | Push chat mới | Mobile (Expo) / Web push |
| 82 | Email khi user offline | Reach Customer |
| 83 | Web push browser | Staff |
| 84 | User preference | Bật/tắt từng loại |
| 85 | Quiet hours | Không notify giờ ngủ |
| 86 | Notification digest | Gom chat trong N phút thành 1 notify |

### Source structure (cross-service)

```
SharedContracts/Events/Ticket/
├── ChatCreatedEvent.cs
├── ChatDeletedEvent.cs
├── ChatEditedEvent.cs
├── ChatReactedEvent.cs
└── ChatMentionedEvent.cs

services/NotificationService/src/NotificationService.Infrastructure/Consumers/
├── ChatCreatedConsumer.cs
├── ChatMentionConsumer.cs
├── ChatReactionConsumer.cs
└── (digest job aggregator)

services/UserService/Domain/Entities/
└── NotificationPreference.cs                        # Thêm: notify_on_chat, notify_on_mention, notify_on_reaction, quiet_hours_start, quiet_hours_end
```

---

## Nhóm 17: Authorization (4 feature)

### Tính năng

| # | Feature | Mục đích |
|---|---------|----------|
| 87 | Granular permission | Tách `create.public`, `create.internal`, `edit.own`, `edit.any`, `delete.own`, `delete.any`, `pin`, `view.internal`, `template.create.global` |
| 88 | Block chat khi ticket CLOSED | Tránh edit lịch sử (Admin được) |
| 89 | Cho Customer rate kèm chat khi CLOSED_PENDING_RATE | UX |
| 90 | Centralize authz helper | Test dễ |

### Source structure

```
Application/Helpers/
├── TicketQueryHelper.cs                             # Mở rộng — join ticket_participants
└── ChatAuthorizationHelper.cs                    # Mới

Application/Interfaces/Services/
└── IChatAuthorizationService.cs

Infrastructure/Services/
└── ChatAuthorizationService.cs                   # CanEditChat(chat, actor) / CanDeleteChat / CanPinChat
```

---

## Nhóm 18: Validation & Security (8 feature)

### Tính năng

| # | Feature | Mục đích |
|---|---------|----------|
| 91 | Min/max body length (1–10000) | Quality |
| 92 | Reject whitespace/emoji thuần | Spam |
| 93 | Spam detection (dup 3 lần/5p) | Anti-spam |
| 94 | XSS sanitization | Bảo mật |
| 95 | Profanity filter | Tone (config dictionary VN/EN) |
| 96 | Rate limiting | Customer 10/p/ticket, Staff 30/p |
| 97 | PII detection | Cảnh báo khi post CCCD/sđt/email |
| 98 | Hate speech detection (AI) | Compliance |

### Source structure

```
Application/Interfaces/Services/
├── IProfanityFilter.cs
├── IPiiDetector.cs
└── ISpamDetector.cs

Infrastructure/Validation/
├── ProfanityFilterService.cs
├── PiiDetectorService.cs
└── SpamDetectorService.cs

Api/Middleware/
└── ChatRateLimitMiddleware.cs                    # AspNetCoreRateLimit package
```

---

## Nhóm 19: Performance & Caching (5 feature)

### Tính năng

| # | Feature | Mục đích |
|---|---------|----------|
| 99 | Redis cache chat page 1 | Tốc độ — TTL 30s, invalidate khi có chat mới |
| 100 | Cursor-based pagination | Mobile infinite scroll |
| 101 | Eager-load attachment count | Tránh N+1 |
| 102 | DB indexes tối ưu | Composite + GIN |
| 103 | Bulk read receipt writer | Channel + batch insert |

### Source structure

```
Infrastructure/Caching/
└── ChatCacheService.cs                           # Redis-based

Application/CQRS/Query/Chat/
└── TicketChatsCursorQuery.cs                     # Cursor variant cho mobile

Application/CQRS/Handler/Chats/
└── TicketChatsCursorQueryHandler.cs

Migrations/
└── AddChatIndexes.cs                             # Composite indexes
```

### Indexes cần có

```
ticket_chats(ticket_id, created_at DESC)
ticket_chats(ticket_id, is_pinned, created_at DESC)
ticket_chats(parent_chat_id)
ticket_chats(thread_root_id)
ticket_chats(author_user_id, created_at DESC)
ticket_chats(body_tsv) GIN
ticket_chat_mentions(mentioned_user_id, is_acknowledged)
ticket_chat_reactions(chat_id, reaction_type)
ticket_chat_reads(user_id, chat_id)
ticket_attachments(chat_id)
```

---

## Nhóm 20: Integration Events / Outbox (6 feature)

### Tính năng

| # | Feature | Mục đích |
|---|---------|----------|
| 104 | Publish `ChatCreatedEvent` | Notify cross-service (NotificationService) |
| 105 | Publish `ChatMentionedEvent` | Mention notify |
| 106 | Publish `ChatDeletedEvent` | Sync downstream |
| 107 | Publish `ChatReactedEvent` | Notify author |
| 108 | Outbox atomic | Đảm bảo deliverability với DB write |
| 109 | Saga escalation review | Mention Manager + ticket P1 → trigger escalation review saga |

### Source structure

```
SharedContracts/Events/Ticket/
├── ChatCreatedEvent.cs
├── ChatEditedEvent.cs
├── ChatDeletedEvent.cs
├── ChatMentionedEvent.cs
└── ChatReactedEvent.cs

Application/CQRS/Handler/Chats/
└── (mọi handler publish event qua _outbox.PublishAsync sau khi SaveChanges)

Infrastructure/Sagas/
└── ChatEscalationReviewSaga.cs                   # MassTransit Saga state machine
```

---

## Nhóm 21: SLA Integration (3 feature)

### Tính năng

| # | Feature | Mục đích |
|---|---------|----------|
| 110 | Reset SLA pause khi Customer chat | Auto-resume timer khi đã pause vì chờ Customer |
| 111 | Auto-pause SLA khi Staff yêu cầu info | Stop timer khi Staff chat `await_customer_info` |
| 112 | Log chat vào SLA breach evidence | Audit cho dispute |

### Source structure

```
Application/CQRS/Handler/Chats/
└── ChatAddCommandHandler.cs                      # Mở rộng — trigger SLA timer logic

Application/Interfaces/Services/
└── ISlaTimerService.cs                              # (đã có) thêm method:
                                                     # - PauseForCustomerInfo(ticketId, chatId)
                                                     # - ResumeOnCustomerReply(ticketId, chatId)

Domain/Entities/
└── SlaPauseEvent.cs                                 # (đã có) thêm enum reason "AwaitingCustomerChat"
```

---

## Nhóm 22: Knowledge Base Integration (3 feature)

### Tính năng

| # | Feature | Mục đích |
|---|---------|----------|
| 113 | Suggest KB article theo nội dung chat | Help Staff trả lời nhanh |
| 114 | Attach KB reference vào chat | Quick reference |
| 115 | Convert chat hay → KB draft | Tăng KB database |

### Source structure

```
Domain/Entities/
└── TicketKbReference.cs                             # (đã có) thêm field chat_id (nullable)

Application/CQRS/Command/
├── ChatAttachKbReference/ChatAttachKbReferenceCommand.cs
└── ConvertChatToKbDraft/ConvertChatToKbDraftCommand.cs

Application/Interfaces/Services/
└── IKbSuggestionService.cs                          # Match KB từ chat body (full-text similarity)

Infrastructure/Services/
└── KbSuggestionService.cs
```

---

## Nhóm 23: Mobile-specific (4 feature)

### Tính năng

| # | Feature | Mục đích |
|---|---------|----------|
| 116 | Voice-to-text chat | Customer dictate → convert sang text |
| 117 | Quick reaction từ notification | Không cần mở app |
| 118 | Offline draft (FE) | Soạn khi mất mạng, gửi khi online |
| 119 | Camera attachment trực tiếp | Chụp ảnh + gửi từ chat box |

### Source structure (backend)

```
Application/CQRS/Command/
└── ChatVoiceTranscribe/ChatVoiceTranscribeCommand.cs

Application/Interfaces/Services/
└── IVoiceTranscriptionService.cs                    # Gọi Whisper API hoặc Google STT

Infrastructure/AiClient/
└── WhisperTranscriptionService.cs

Api/Controllers/
└── TicketChatsController.cs                      # Endpoint POST voice với audio multipart
```

> Phần offline draft + camera xử lý ở **Mobile** (Expo) — backend chỉ cần endpoint POST attachment có chunk/resume upload.

---

## Nhóm 24: Export / Compliance (5 feature)

### Tính năng

| # | Feature | Mục đích |
|---|---------|----------|
| 120 | Export chat timeline ra PDF | Legal/audit |
| 121 | Print-friendly view | In ra |
| 122 | Redact PII trước export | GDPR |
| 123 | Retention policy auto archive | Storage management — chat > N năm archive |
| 124 | GDPR right-to-erasure | Customer yêu cầu xóa chat của mình |

### Source structure

```
Application/CQRS/Command/
├── ChatExportPdf/ChatExportPdfCommand.cs
└── ChatEraseUserData/ChatEraseUserDataCommand.cs

Application/Interfaces/Services/
├── IPdfExporter.cs
└── IDataRetentionService.cs

Infrastructure/Export/
└── QuestPdfChatExporter.cs                       # Dùng QuestPDF package

Infrastructure/BackgroundServices/
└── ChatRetentionService.cs                       # Auto archive > N năm
```

---

## Nhóm 25: Participant Management (12 feature) ⭐ Cốt lõi

### Tính năng

| # | Feature | Mục đích |
|---|---------|----------|
| 125 | Add participant | Mời user vào ticket chat |
| 126 | Remove participant | Loại user khỏi chat |
| 127 | List participants | Xem ai đang trong chat |
| 128 | Watcher mode | Xem-only, không post |
| 129 | Collaborator mode | Staff phụ có quyền post |
| 130 | Delegate Customer | Customer ủy quyền cho người khác đại diện |
| 131 | Auto-add khi reassign Staff | Staff cũ vẫn ở lại role `PreviousAssignee` |
| 132 | Bulk add team | Manager add cả Tier 2 vào ticket P1 |
| 133 | Self-leave | Watcher tự rời |
| 134 | Permission per participant | `can_post`, `can_view_internal` riêng |
| 135 | Participant history | Audit ai từng tham gia |
| 136 | Notification add/remove | Báo user mới được mời |

### Source structure

```
Domain/Entities/
└── TicketParticipant.cs                             # Bảng mới: ticket_id, user_id, role, participant_type, can_post, can_view_internal, added_by, added_at, removed_at

Domain/Enums/
└── ParticipantTypeEnum.cs                           # Owner=1, PrimaryAssignee=2, Collaborator=3, Watcher=4, Delegate=5, PreviousAssignee=6

Application/CQRS/Command/
├── ParticipantAdd/ParticipantAddCommand.cs
├── ParticipantRemove/ParticipantRemoveCommand.cs
├── ParticipantBulkAdd/ParticipantBulkAddCommand.cs
├── ParticipantSelfLeave/ParticipantSelfLeaveCommand.cs
└── ParticipantUpdateRole/ParticipantUpdateRoleCommand.cs

Application/CQRS/Query/Participant/
├── TicketParticipantsQuery.cs
└── ParticipantHistoryQuery.cs

Application/CQRS/Handler/Participants/
├── ParticipantAddCommandHandler.cs
├── ParticipantRemoveCommandHandler.cs
├── ParticipantBulkAddCommandHandler.cs
├── ParticipantSelfLeaveCommandHandler.cs
├── ParticipantUpdateRoleCommandHandler.cs
├── TicketParticipantsQueryHandler.cs
└── ParticipantHistoryQueryHandler.cs

Application/Helpers/
└── TicketQueryHelper.cs                             # Mở rộng — CanAccessTicket join ticket_participants

SharedContracts/Events/Ticket/
├── ParticipantAddedEvent.cs
├── ParticipantRemovedEvent.cs
└── ParticipantRoleChangedEvent.cs

Api/Controllers/
└── TicketParticipantsController.cs                  # Controller mới

Migrations/
└── AddTicketParticipants.cs
```

### Logic phức tạp

- **Soft delete participant** khi remove (set `removed_at`) — không hard delete, giữ audit
- Chat cũ của participant bị remove **vẫn giữ nguyên** trong timeline
- **SignalR**: khi participant bị remove → force disconnect khỏi hub group `ticket-{id}`
- **Owner (Customer chính) KHÔNG bị remove được** — chỉ Admin được, và cần lý do
- **Manager/Admin auto là implicit participant** — không cần add vào bảng (vẫn check role)
- **Reassign Staff**: Staff cũ tự động chuyển sang `participant_type = PreviousAssignee` (vẫn xem được, không post được nữa)

---

# Phần III — Tổng kết quy mô

## Thống kê

| Hạng mục | Số lượng |
|---------|---------|
| **Tổng feature/function** | **136** |
| **Nhóm chức năng** | **25** |
| **Bảng mới** | **11** |
| **Bảng sửa (thêm cột)** | **4** |
| **Enum mới** | **10** |
| **Endpoint REST mới** | **~40** |
| **SignalR Hub** | **1** (6 server-push events) |
| **Command handlers** | **~35** |
| **Query handlers** | **~18** |
| **Background services** | **4** |
| **Integration events mới** | **9** |
| **RabbitMQ consumers** (NotificationService) | **4** |
| **Migration files** | **15** |
| **Service interfaces mới** | **~15** |

## Bảng mới (11)

1. `ticket_chat_edits` — edit history
2. `ticket_chat_mentions` — @mention
3. `ticket_chat_reactions` — reaction
4. `ticket_chat_reads` — read receipt
5. `ticket_chat_translations` — translation cache
6. `chat_templates` — canned response
7. `chat_ai_suggestions` — AI suggestion log
8. `chat_metrics_daily` — analytics aggregation
9. `ticket_participants` — participant management ⭐
10. (optional) `chat_template_usage_log`
11. (optional) `chat_pii_detections`

## Bảng sửa (4)

| Bảng | Cột thêm |
|------|----------|
| `ticket_chats` | `edited_at`, `edit_count`, `last_edited_by_user_id`, `parent_chat_id`, `thread_root_id`, `reply_count`, `body_format`, `body_html`, `body_tsv`, `is_pinned`, `pinned_at`, `pinned_by_user_id`, `original_language` (13 cột) |
| `ticket_attachments` | `chat_id`, `thumbnail_url`, `is_inline`, `download_count`, `virus_scan_status` (5 cột) |
| `ticket_activities` | Mở rộng `ActivityActionEnum` values (không đổi schema) |
| `outbox_messages` | Không đổi schema — dùng publish event types mới |

## Enum mới (10)

1. `ChatBodyFormatEnum` — PlainText=1, Markdown=2
2. `ReactionTypeEnum` — ThumbsUp=1, Acknowledged=2, Resolved=3, NeedMoreInfo=4, Disagree=5
3. `ChatTemplateCategoryEnum` — Greeting=1, RequestInfo=2, Update=3, Resolution=4, Internal=5, Other=6
4. `ChatTemplateScopeEnum` — Personal=1, Team=2, Global=3
5. `TranslationProviderEnum` — GoogleTranslate=1, DeepL=2, Manual=3
6. `VirusScanStatusEnum` — Pending=1, Clean=2, Infected=3, Failed=4
7. `ChatAiIntentEnum` — RequestInfo=1, TechnicalAnswer=2, Resolution=3, FollowUp=4
8. `ParticipantTypeEnum` — Owner=1, PrimaryAssignee=2, Collaborator=3, Watcher=4, Delegate=5, PreviousAssignee=6
9. (optional) `ChatRetentionPolicyEnum`
10. Mở rộng `ActivityActionEnum`: thêm `ChatEdited`, `ChatDeleted`, `ChatRestored`, `ChatPinned`, `ChatReacted`, `ParticipantAdded`, `ParticipantRemoved`

---

# Phần IV — Database schema chi tiết

## 4.1 `ticket_chats` (mở rộng)

```sql
ALTER TABLE ticket_chats ADD COLUMN edited_at timestamptz NULL;
ALTER TABLE ticket_chats ADD COLUMN edit_count int NOT NULL DEFAULT 0;
ALTER TABLE ticket_chats ADD COLUMN last_edited_by_user_id uuid NULL;
ALTER TABLE ticket_chats ADD COLUMN parent_chat_id uuid NULL REFERENCES ticket_chats(id);
ALTER TABLE ticket_chats ADD COLUMN thread_root_id uuid NULL;
ALTER TABLE ticket_chats ADD COLUMN reply_count int NOT NULL DEFAULT 0;
ALTER TABLE ticket_chats ADD COLUMN body_format int NOT NULL DEFAULT 1;
ALTER TABLE ticket_chats ADD COLUMN body_html text NULL;
ALTER TABLE ticket_chats ADD COLUMN body_tsv tsvector NULL;
ALTER TABLE ticket_chats ADD COLUMN is_pinned bool NOT NULL DEFAULT false;
ALTER TABLE ticket_chats ADD COLUMN pinned_at timestamptz NULL;
ALTER TABLE ticket_chats ADD COLUMN pinned_by_user_id uuid NULL;
ALTER TABLE ticket_chats ADD COLUMN original_language varchar(5) NULL;
```

## 4.2 `ticket_chat_edits` (mới)

```sql
CREATE TABLE ticket_chat_edits (
  id uuid PRIMARY KEY,
  chat_id uuid NOT NULL REFERENCES ticket_chats(id) ON DELETE CASCADE,
  old_body text NOT NULL,
  new_body text NOT NULL,
  edited_at timestamptz NOT NULL,
  edited_by_user_id uuid NOT NULL,
  edited_by_role int NOT NULL,
  edit_reason text NULL,
  created_at timestamptz NOT NULL,
  is_deleted bool NOT NULL DEFAULT false,
  deleted_at timestamptz NULL
);
CREATE INDEX IX_ticket_chat_edits_chat_id ON ticket_chat_edits(chat_id);
```

## 4.3 `ticket_chat_mentions` (mới)

```sql
CREATE TABLE ticket_chat_mentions (
  id uuid PRIMARY KEY,
  chat_id uuid NOT NULL REFERENCES ticket_chats(id) ON DELETE CASCADE,
  mentioned_user_id uuid NOT NULL,
  mentioned_user_role int NOT NULL,
  mentioned_display_name varchar(256) NOT NULL,
  is_acknowledged bool NOT NULL DEFAULT false,
  acknowledged_at timestamptz NULL,
  created_at timestamptz NOT NULL,
  is_deleted bool NOT NULL DEFAULT false,
  deleted_at timestamptz NULL
);
CREATE INDEX IX_ticket_chat_mentions_user_unread
ON ticket_chat_mentions(mentioned_user_id, is_acknowledged);
```

## 4.4 `ticket_chat_reactions` (mới)

```sql
CREATE TABLE ticket_chat_reactions (
  id uuid PRIMARY KEY,
  chat_id uuid NOT NULL REFERENCES ticket_chats(id) ON DELETE CASCADE,
  user_id uuid NOT NULL,
  user_role int NOT NULL,
  reaction_type int NOT NULL,
  created_at timestamptz NOT NULL,
  is_deleted bool NOT NULL DEFAULT false,
  deleted_at timestamptz NULL,
  CONSTRAINT UQ_ticket_chat_reactions UNIQUE (chat_id, user_id, reaction_type)
);
CREATE INDEX IX_ticket_chat_reactions_chat_type
ON ticket_chat_reactions(chat_id, reaction_type);
```

## 4.5 `ticket_chat_reads` (mới)

```sql
CREATE TABLE ticket_chat_reads (
  id uuid PRIMARY KEY,
  chat_id uuid NOT NULL REFERENCES ticket_chats(id) ON DELETE CASCADE,
  user_id uuid NOT NULL,
  user_role int NOT NULL,
  read_at timestamptz NOT NULL,
  CONSTRAINT UQ_ticket_chat_reads UNIQUE (chat_id, user_id)
);
CREATE INDEX IX_ticket_chat_reads_user ON ticket_chat_reads(user_id, chat_id);
```

## 4.6 `ticket_chat_translations` (mới)

```sql
CREATE TABLE ticket_chat_translations (
  id uuid PRIMARY KEY,
  chat_id uuid NOT NULL REFERENCES ticket_chats(id) ON DELETE CASCADE,
  target_language varchar(5) NOT NULL,
  translated_body text NOT NULL,
  provider int NOT NULL,
  translated_at timestamptz NOT NULL,
  is_deleted bool NOT NULL DEFAULT false,
  deleted_at timestamptz NULL,
  CONSTRAINT UQ_ticket_chat_translations UNIQUE (chat_id, target_language)
);
```

## 4.7 `chat_templates` (mới)

```sql
CREATE TABLE chat_templates (
  id uuid PRIMARY KEY,
  name varchar(200) NOT NULL,
  content text NOT NULL,
  category int NOT NULL,
  is_internal_default bool NOT NULL DEFAULT false,
  created_by_user_id uuid NOT NULL,
  scope int NOT NULL,
  team_id uuid NULL,
  usage_count int NOT NULL DEFAULT 0,
  is_active bool NOT NULL DEFAULT true,
  created_at timestamptz NOT NULL,
  created_by uuid NULL,
  updated_at timestamptz NULL,
  is_deleted bool NOT NULL DEFAULT false,
  deleted_at timestamptz NULL
);
CREATE INDEX IX_chat_templates_scope_active ON chat_templates(scope, is_active);
```

## 4.8 `chat_ai_suggestions` (mới)

```sql
CREATE TABLE chat_ai_suggestions (
  id uuid PRIMARY KEY,
  ticket_id uuid NOT NULL,
  suggested_at timestamptz NOT NULL,
  intent int NOT NULL,
  suggestions jsonb NOT NULL,
  selected_index int NULL,
  edited_before_post bool NOT NULL DEFAULT false,
  final_chat_id uuid NULL,
  created_at timestamptz NOT NULL,
  is_deleted bool NOT NULL DEFAULT false
);
```

## 4.9 `chat_metrics_daily` (mới)

```sql
CREATE TABLE chat_metrics_daily (
  id uuid PRIMARY KEY,
  metric_date date NOT NULL,
  staff_id uuid NULL,
  ticket_id uuid NULL,
  chat_count int NOT NULL,
  avg_response_time_min decimal(10,2) NULL,
  internal_count int NOT NULL,
  mention_received_count int NOT NULL,
  created_at timestamptz NOT NULL
);
CREATE INDEX IX_chat_metrics_daily_date_staff ON chat_metrics_daily(metric_date, staff_id);
```

## 4.10 `ticket_participants` (mới) ⭐

```sql
CREATE TABLE ticket_participants (
  id uuid PRIMARY KEY,
  ticket_id uuid NOT NULL REFERENCES tickets(id) ON DELETE CASCADE,
  user_id uuid NOT NULL,
  user_role int NOT NULL,
  participant_type int NOT NULL,
  can_post bool NOT NULL DEFAULT true,
  can_view_internal bool NOT NULL DEFAULT false,
  added_by_user_id uuid NOT NULL,
  added_at timestamptz NOT NULL,
  removed_at timestamptz NULL,
  removed_by_user_id uuid NULL,
  remove_reason text NULL,
  created_at timestamptz NOT NULL,
  is_deleted bool NOT NULL DEFAULT false,
  deleted_at timestamptz NULL
);
CREATE INDEX IX_ticket_participants_ticket_active
ON ticket_participants(ticket_id) WHERE removed_at IS NULL;
CREATE INDEX IX_ticket_participants_user ON ticket_participants(user_id);
```

## 4.11 `ticket_attachments` (mở rộng)

```sql
ALTER TABLE ticket_attachments ADD COLUMN chat_id uuid NULL REFERENCES ticket_chats(id) ON DELETE SET NULL;
ALTER TABLE ticket_attachments ADD COLUMN thumbnail_url varchar(1000) NULL;
ALTER TABLE ticket_attachments ADD COLUMN is_inline bool NOT NULL DEFAULT false;
ALTER TABLE ticket_attachments ADD COLUMN download_count int NOT NULL DEFAULT 0;
ALTER TABLE ticket_attachments ADD COLUMN virus_scan_status int NOT NULL DEFAULT 1;
CREATE INDEX IX_ticket_attachments_chat ON ticket_attachments(chat_id);
```

---

# Phần V — API endpoint matrix

## 5.1 Chat endpoints

| Method | Path | Mục đích | Auth |
|--------|------|----------|------|
| POST | `/api/tickets/{ticketId}/chats` | Add chat (hiện có) | `[Authorize]` |
| GET | `/api/tickets/{ticketId}/chats` | List (hiện có) | `[Authorize]` |
| GET | `/api/tickets/{ticketId}/chats/cursor` | Cursor pagination cho mobile | `[Authorize]` |
| GET | `/api/tickets/{ticketId}/chats/{id}` | Detail 1 chat | `[Authorize]` |
| PUT | `/api/tickets/{ticketId}/chats/{id}` | Edit | `[Authorize]` |
| DELETE | `/api/tickets/{ticketId}/chats/{id}` | Soft delete | `[Authorize]` |
| PATCH | `/api/tickets/{ticketId}/chats/{id}/restore` | Restore | `AdminOnly` |
| GET | `/api/tickets/{ticketId}/chats/{id}/history` | Edit history | `[Authorize]` |
| POST | `/api/tickets/{ticketId}/chats/{id}/replies` | Reply 1 chat | `[Authorize]` |
| GET | `/api/tickets/{ticketId}/chats/{id}/replies` | List replies | `[Authorize]` |
| POST | `/api/tickets/{ticketId}/chats/{id}/reactions` | Add reaction | `[Authorize]` |
| DELETE | `/api/tickets/{ticketId}/chats/{id}/reactions/{type}` | Remove reaction | `[Authorize]` |
| GET | `/api/tickets/{ticketId}/chats/{id}/reactions` | List reactions | `[Authorize]` |
| POST | `/api/tickets/{ticketId}/chats/mark-read` | Bulk mark read | `[Authorize]` |
| GET | `/api/tickets/{ticketId}/chats/{id}/readers` | Ai đã đọc | `Staff/Manager` |
| GET | `/api/tickets/{ticketId}/unread-count` | Số chưa đọc | `[Authorize]` |
| POST | `/api/tickets/{ticketId}/chats/{id}/pin` | Pin | `Staff/Manager/Admin` |
| DELETE | `/api/tickets/{ticketId}/chats/{id}/pin` | Unpin | `Staff/Manager/Admin` |
| POST | `/api/tickets/{ticketId}/chats/{id}/translate` | Translate | `[Authorize]` |
| POST | `/api/tickets/{ticketId}/chats/{id}/attachments` | Add attachment sau | `[Authorize]` |
| DELETE | `/api/tickets/{ticketId}/chats/{id}/attachments/{attId}` | Remove attachment | `[Authorize]` |
| GET | `/api/tickets/{ticketId}/chats/{id}/attachments` | List attachments | `[Authorize]` |
| POST | `/api/tickets/{ticketId}/chats/from-template/{templateId}` | Post từ template | `Staff/Manager` |
| POST | `/api/tickets/{ticketId}/chats/suggest` | AI suggest | `Staff/Manager` |
| POST | `/api/tickets/{ticketId}/chats/voice` | Voice-to-text | `[Authorize]` |
| POST | `/api/tickets/{ticketId}/chats/{id}/attach-kb` | Attach KB reference | `Staff/Manager` |
| POST | `/api/tickets/{ticketId}/chats/{id}/to-kb-draft` | Convert → KB draft | `Staff/Manager` |
| GET | `/api/tickets/{ticketId}/chats/export-pdf` | Export PDF timeline | `Manager/Admin` |

## 5.2 Cross-ticket endpoints

| Method | Path | Mục đích |
|--------|------|----------|
| GET | `/api/chats/me` | My chats cross-ticket |
| GET | `/api/chats/mentions/me` | My mentions |
| PATCH | `/api/chats/mentions/{id}/acknowledge` | Ack mention |
| GET | `/api/chats/search` | Global search (Admin/Manager) |
| POST | `/api/chats/erase-my-data` | GDPR right-to-erasure |

## 5.3 Template endpoints

| Method | Path | Mục đích |
|--------|------|----------|
| GET | `/api/chat-templates` | List |
| POST | `/api/chat-templates` | Create |
| PUT | `/api/chat-templates/{id}` | Update |
| DELETE | `/api/chat-templates/{id}` | Delete |

## 5.4 Participant endpoints

| Method | Path | Mục đích |
|--------|------|----------|
| GET | `/api/tickets/{ticketId}/participants` | List active |
| POST | `/api/tickets/{ticketId}/participants` | Add |
| POST | `/api/tickets/{ticketId}/participants/bulk` | Bulk add |
| DELETE | `/api/tickets/{ticketId}/participants/{userId}` | Remove |
| POST | `/api/tickets/{ticketId}/participants/leave` | Self-leave |
| PATCH | `/api/tickets/{ticketId}/participants/{userId}` | Update role/permission |
| GET | `/api/tickets/{ticketId}/participants/history` | Lịch sử full |

## 5.5 Metrics endpoints

| Method | Path | Mục đích |
|--------|------|----------|
| GET | `/api/admin/chat-metrics` | Manager dashboard |
| GET | `/api/admin/chat-metrics/heatmap` | Activity heatmap |

## 5.6 SignalR Hub

| Path | Methods (client → server) | Server-push events |
|------|--------------------------|---------------------|
| `/hubs/ticket-chats` | `JoinTicket(ticketId)`, `LeaveTicket(ticketId)`, `Typing(ticketId)` | `ChatAdded`, `ChatEdited`, `ChatDeleted`, `ReactionAdded`, `UserTyping`, `MentionReceived` |

**Tổng: ~40 endpoint REST + 1 SignalR Hub (6 events)**

---

# Phần VI — Integration events matrix

| Event | Publish khi | Consumer | Action |
|-------|-------------|----------|--------|
| `ChatCreatedEvent` | Add chat success | NotificationService | Push notify Customer/Staff |
| `ChatEditedEvent` | Edit chat success | NotificationService | (optional) notify thread |
| `ChatDeletedEvent` | Soft delete chat | NotificationService + Audit | Update FE state |
| `ChatMentionedEvent` | Có mention trong body | NotificationService | Push notify mentioned user |
| `ChatReactedEvent` | Add reaction | NotificationService | Notify author of chat |
| `ParticipantAddedEvent` | Add participant | NotificationService | Welcome notify |
| `ParticipantRemovedEvent` | Remove participant | NotificationService + SignalR | Force disconnect, notify |
| `ParticipantRoleChangedEvent` | Update role | NotificationService | Notify role change |
| (Saga) `ChatEscalationReviewRequested` | Mention Manager + ticket P1 | TicketService Saga | Trigger escalation review workflow |

---

# Phần VII — Source code structure đầy đủ

```
services/TicketService/src/
├── TicketService.Api/
│   ├── Controllers/
│   │   ├── TicketChatsController.cs                  # MỞ RỘNG ~20 endpoint
│   │   ├── TicketParticipantsController.cs              # MỚI
│   │   ├── ChatTemplatesController.cs                # MỚI
│   │   └── AdminChatMetricsController.cs             # MỚI
│   ├── Hubs/
│   │   └── TicketChatHub.cs                          # MỚI — SignalR
│   ├── Middleware/
│   │   └── ChatRateLimitMiddleware.cs                # MỚI
│   ├── Authentication/
│   │   └── SignalRJwtConfiguration.cs                   # MỚI
│   └── Program.cs                                       # Đăng ký SignalR + Redis backplane
│
├── TicketService.Application/
│   ├── CQRS/
│   │   ├── Command/
│   │   │   # ChatAdd (đã có)
│   │   │   ├── ChatEdit/
│   │   │   ├── ChatDelete/
│   │   │   ├── ChatRestore/
│   │   │   ├── ChatReply/
│   │   │   ├── ChatPin/
│   │   │   ├── ChatUnpin/
│   │   │   ├── ChatReactionAdd/
│   │   │   ├── ChatReactionRemove/
│   │   │   ├── ChatMarkRead/
│   │   │   ├── ChatTranslate/
│   │   │   ├── ChatMentionAcknowledge/
│   │   │   ├── ChatAttachmentAdd/
│   │   │   ├── ChatAttachmentRemove/
│   │   │   ├── ChatFromTemplate/
│   │   │   ├── ChatSuggest/
│   │   │   ├── ChatTemplateCreate/
│   │   │   ├── ChatTemplateUpdate/
│   │   │   ├── ChatTemplateDelete/
│   │   │   ├── ChatVoiceTranscribe/
│   │   │   ├── ChatExportPdf/
│   │   │   ├── ChatEraseUserData/
│   │   │   ├── ChatAttachKbReference/
│   │   │   ├── ConvertChatToKbDraft/
│   │   │   ├── ParticipantAdd/
│   │   │   ├── ParticipantRemove/
│   │   │   ├── ParticipantBulkAdd/
│   │   │   ├── ParticipantSelfLeave/
│   │   │   └── ParticipantUpdateRole/
│   │   ├── Query/
│   │   │   ├── Ticket/
│   │   │   │   └── TicketChatsQuery.cs               # Mở rộng filter
│   │   │   ├── Chat/                                 # Folder mới
│   │   │   │   ├── ChatGetByIdQuery.cs
│   │   │   │   ├── ChatRepliesQuery.cs
│   │   │   │   ├── ChatHistoryQuery.cs
│   │   │   │   ├── ChatReactionsQuery.cs
│   │   │   │   ├── ChatReadersQuery.cs
│   │   │   │   ├── ChatAttachmentsQuery.cs
│   │   │   │   ├── MyChatsQuery.cs
│   │   │   │   ├── MyMentionsQuery.cs
│   │   │   │   ├── ChatGlobalSearchQuery.cs
│   │   │   │   ├── TicketUnreadCountQuery.cs
│   │   │   │   └── TicketChatsCursorQuery.cs
│   │   │   ├── Template/
│   │   │   │   └── ChatTemplatesQuery.cs
│   │   │   ├── Participant/
│   │   │   │   ├── TicketParticipantsQuery.cs
│   │   │   │   └── ParticipantHistoryQuery.cs
│   │   │   └── Metrics/
│   │   │       ├── ChatMetricsQuery.cs
│   │   │       └── ChatHeatmapQuery.cs
│   │   └── Handler/
│   │       ├── Chats/                                # ~30 handler
│   │       ├── Templates/                               # 4 handler
│   │       ├── Participants/                            # 7 handler
│   │       └── Metrics/                                 # 2 handler
│   ├── DTOs/
│   │   └── Response/
│   │       ├── Chats/                                # Mở rộng DTO
│   │       │   ├── ChatResponse.cs
│   │       │   ├── ChatActionDTO.cs
│   │       │   ├── ChatEditHistoryDTO.cs
│   │       │   ├── ChatMentionDTO.cs
│   │       │   ├── ChatReactionDTO.cs
│   │       │   ├── ChatReactionAggregateDTO.cs
│   │       │   ├── ChatReaderDTO.cs
│   │       │   └── ChatAiSuggestionDTO.cs
│   │       ├── Tickets/
│   │       │   └── TicketChatDTO.cs                  # Mở rộng
│   │       ├── Templates/
│   │       │   └── ChatTemplateDTO.cs
│   │       ├── Participants/
│   │       │   ├── TicketParticipantDTO.cs
│   │       │   └── ParticipantHistoryDTO.cs
│   │       └── Metrics/
│   │           └── ChatMetricsDTO.cs
│   ├── Helpers/
│   │   ├── TicketQueryHelper.cs                         # Mở rộng — join ticket_participants
│   │   └── ChatAuthorizationHelper.cs                # Mới
│   └── Interfaces/
│       ├── Repositories/
│       │   └── ITicketUnitOfWork.cs                     # Mở rộng các repo mới
│       └── Services/
│           ├── ITicketCurrentUserService.cs             # Đã có
│           ├── IActivityLogger.cs                       # Đã có
│           ├── IMentionParser.cs                        # Mới
│           ├── IMarkdownRenderer.cs                     # Mới
│           ├── ITemplateRenderer.cs                     # Mới
│           ├── ITranslationProvider.cs                  # Mới
│           ├── IProfanityFilter.cs                      # Mới
│           ├── IPiiDetector.cs                          # Mới
│           ├── ISpamDetector.cs                         # Mới
│           ├── IChatAuthorizationService.cs          # Mới
│           ├── IChatAiSuggestionClient.cs            # Mới
│           ├── ITicketChatRealtimeNotifier.cs        # Mới
│           ├── IVoiceTranscriptionService.cs            # Mới
│           ├── IPdfExporter.cs                          # Mới
│           ├── IDataRetentionService.cs                 # Mới
│           ├── IKbSuggestionService.cs                  # Mới
│           └── IChatCacheService.cs                  # Mới
│
├── TicketService.Domain/
│   ├── Entities/
│   │   ├── TicketChat.cs                             # Mở rộng 13 cột
│   │   ├── TicketAttachment.cs                          # Mở rộng 5 cột
│   │   ├── Ticket.cs                                    # Thêm Participants navigation
│   │   ├── TicketActivity.cs                            # (giữ)
│   │   ├── TicketKbReference.cs                         # Thêm chat_id
│   │   ├── TicketChatEdit.cs                         # Mới
│   │   ├── TicketChatMention.cs                      # Mới
│   │   ├── TicketChatReaction.cs                     # Mới
│   │   ├── TicketChatRead.cs                         # Mới
│   │   ├── TicketChatTranslation.cs                  # Mới
│   │   ├── TicketParticipant.cs                         # Mới ⭐
│   │   ├── ChatTemplate.cs                           # Mới
│   │   ├── ChatAiSuggestion.cs                       # Mới
│   │   └── ChatMetricsDaily.cs                       # Mới
│   └── Enums/
│       ├── ActorRoleEnum.cs                             # (giữ)
│       ├── ActivityActionEnum.cs                        # Mở rộng values
│       ├── AttachmentSourceEnum.cs                      # (giữ)
│       ├── ChatBodyFormatEnum.cs                     # Mới
│       ├── ReactionTypeEnum.cs                          # Mới
│       ├── ChatTemplateCategoryEnum.cs               # Mới
│       ├── ChatTemplateScopeEnum.cs                  # Mới
│       ├── TranslationProviderEnum.cs                   # Mới
│       ├── VirusScanStatusEnum.cs                       # Mới
│       ├── ChatAiIntentEnum.cs                       # Mới
│       └── ParticipantTypeEnum.cs                       # Mới ⭐
│
├── TicketService.Infrastructure/
│   ├── Persistence/
│   │   ├── Configurations/
│   │   │   ├── TicketChatConfiguration.cs            # Mở rộng — thêm config tsvector, threading
│   │   │   ├── TicketAttachmentConfiguration.cs         # Mở rộng
│   │   │   ├── TicketChatEditConfiguration.cs        # Mới
│   │   │   ├── TicketChatMentionConfiguration.cs     # Mới
│   │   │   ├── TicketChatReactionConfiguration.cs    # Mới
│   │   │   ├── TicketChatReadConfiguration.cs        # Mới
│   │   │   ├── TicketChatTranslationConfiguration.cs # Mới
│   │   │   ├── TicketParticipantConfiguration.cs        # Mới
│   │   │   ├── ChatTemplateConfiguration.cs          # Mới
│   │   │   ├── ChatAiSuggestionConfiguration.cs      # Mới
│   │   │   └── ChatMetricsDailyConfiguration.cs      # Mới
│   │   └── Converters/                                  # (giữ)
│   ├── Services/
│   │   ├── MentionParserService.cs
│   │   ├── MarkdigMarkdownRenderer.cs
│   │   ├── TemplateRendererService.cs
│   │   ├── ProfanityFilterService.cs
│   │   ├── PiiDetectorService.cs
│   │   ├── SpamDetectorService.cs
│   │   ├── ChatAuthorizationService.cs
│   │   └── KbSuggestionService.cs
│   ├── Translation/
│   │   └── GoogleTranslateProvider.cs
│   ├── AiClient/
│   │   ├── FastApiChatAiClient.cs
│   │   └── WhisperTranscriptionService.cs
│   ├── Realtime/
│   │   └── SignalRTicketChatNotifier.cs
│   ├── Caching/
│   │   └── ChatCacheService.cs                       # Redis
│   ├── Export/
│   │   └── QuestPdfChatExporter.cs
│   ├── BackgroundServices/
│   │   ├── VirusScanWorker.cs
│   │   ├── ChatReadReceiptBulkWriter.cs
│   │   ├── ChatMetricsAggregatorService.cs
│   │   └── ChatRetentionService.cs
│   ├── Sagas/
│   │   └── ChatEscalationReviewSaga.cs
│   ├── DependencyInjection/
│   │   └── ManageDependencyInjection.cs                 # Đăng ký services + background + cache
│   └── Migrations/                                      # 15 migration mới
│       ├── 20260620_AddChatEditHistory.cs
│       ├── 20260620_LinkAttachmentToChat.cs
│       ├── 20260620_AddChatThreading.cs
│       ├── 20260620_AddChatMentions.cs
│       ├── 20260620_AddChatReactions.cs
│       ├── 20260620_AddChatReadReceipts.cs
│       ├── 20260620_AddChatMarkdownSupport.cs
│       ├── 20260620_AddChatPinning.cs
│       ├── 20260620_AddChatTemplates.cs
│       ├── 20260620_AddChatTranslations.cs
│       ├── 20260620_AddChatAiSuggestions.cs
│       ├── 20260620_AddChatFullTextSearch.cs
│       ├── 20260620_AddChatMetrics.cs
│       ├── 20260620_AddChatAttachmentEnhancements.cs
│       ├── 20260620_AddChatIndexes.cs
│       └── 20260620_AddTicketParticipants.cs
│
└── tests/
    ├── TicketService.UnitTests/
    │   └── Handlers/
    │       ├── Chats/                                # ~30 test class
    │       ├── Templates/                               # 4 test class
    │       ├── Participants/                            # 7 test class
    │       └── Metrics/
    └── TicketService.IntegrationTests/
        ├── Tickets/
        │   └── TicketChatApiTests.cs                 # Mở rộng
        ├── Templates/
        │   └── ChatTemplatesApiTests.cs
        ├── Participants/
        │   └── TicketParticipantsApiTests.cs
        └── Hubs/
            └── TicketChatHubTests.cs

shared/src/SharedContracts/Events/Ticket/
├── ChatCreatedEvent.cs                               # Mới
├── ChatEditedEvent.cs                                # Mới
├── ChatDeletedEvent.cs                               # Mới
├── ChatMentionedEvent.cs                             # Mới
├── ChatReactedEvent.cs                               # Mới
├── ParticipantAddedEvent.cs                             # Mới
├── ParticipantRemovedEvent.cs                           # Mới
└── ParticipantRoleChangedEvent.cs                       # Mới

services/NotificationService/src/NotificationService.Infrastructure/Consumers/
├── ChatCreatedConsumer.cs                            # Mới
├── ChatMentionConsumer.cs                            # Mới
├── ChatReactionConsumer.cs                           # Mới
└── ParticipantChangeConsumer.cs                         # Mới
```

---

# Phần VIII — Roadmap thực hiện theo dependency

| Bước | Nhóm | Lý do |
|------|------|------|
| 1 | Nhóm 1 (CRUD) + Nhóm 2 (history) + Nhóm 3 (attachment refactor) | **Foundation** — mọi tính năng sau đều cần |
| 2 | Nhóm 17 (authz) + Nhóm 18 (validation) | Bảo mật trước khi mở rộng |
| 3 | Nhóm 25 (participant) ⭐ | Cốt lõi nghiệp vụ — cần cho mọi mở rộng visibility sau |
| 4 | Nhóm 8 (markdown) | Đơn lẻ, không phụ thuộc |
| 5 | Nhóm 4 (reply) + Nhóm 13 (pin) | Đơn lẻ |
| 6 | Nhóm 5 (mention) + Nhóm 20 (integration events) + Nhóm 16 (notification) | Bộ ba liên kết — cần Outbox publish + NotificationService consume |
| 7 | Nhóm 6 (reaction) + Nhóm 7 (read receipt) | Sau notification |
| 8 | Nhóm 10 (template) | Productivity, đơn lẻ |
| 9 | Nhóm 9 (SignalR realtime) | Wrap broadcast cho mọi feature đã có ở trên |
| 10 | Nhóm 12 (search) + Nhóm 19 (performance) | Sau khi data nhiều |
| 11 | Nhóm 14 (translation) | Optional theo locale |
| 12 | Nhóm 11 (AI assist) | Phụ thuộc AI module ready |
| 13 | Nhóm 21 (SLA) + Nhóm 22 (KB) | Integration cross-feature |
| 14 | Nhóm 15 (metrics) | Sau khi có usage data |
| 15 | Nhóm 23 (mobile) + Nhóm 24 (export/compliance) | Final polish |

## Critical path

```
Foundation (1+2+3)
   ↓
Authz + Validation (17+18)
   ↓
Participant (25)         ──┐
   ↓                       │
[song song] Markdown (8)   ├──┐
[song song] Reply (4)      │  │
[song song] Pin (13)       │  │
   ↓                       │  │
Mention + Notification (5+16+20) ─┐
   ↓                              │
Reaction + Read (6+7)             │
   ↓                              │
SignalR realtime (9) ─────────────┘
   ↓
Search + Perf (12+19)
   ↓
AI + Translation + Metrics (11+14+15)
   ↓
SLA + KB + Mobile + Export (21+22+23+24)
```

---

# Phần IX — Configuration & feature flags

`appsettings.json`:

```json
{
  "Chat": {
    "MaxBodyLength": 10000,
    "MinBodyLength": 1,
    "EditWindowMinutes": 15,
    "MaxAttachmentsPerChat": 10,
    "MaxAttachmentSizeMb": 50,
    "MaxPinnedPerTicket": 3,
    "AllowedMimeTypes": ["image/jpeg", "image/png", "image/gif", "application/pdf", "video/mp4", "text/plain"],
    "RateLimit": {
      "CustomerPerMinute": 10,
      "StaffPerMinute": 30,
      "ManagerPerMinute": 60
    },
    "Cache": {
      "Page1TtlSeconds": 30,
      "UserDisplayNameTtlMinutes": 5,
      "TranslationTtlDays": 30
    },
    "Realtime": {
      "TypingDebounceMs": 500,
      "PresenceTimeoutSeconds": 60
    },
    "Ai": {
      "SuggestEndpoint": "https://ai-module/ai/chat-suggest",
      "MaxSuggestionsPerCall": 3,
      "TimeoutSeconds": 10
    },
    "Translation": {
      "Provider": "GoogleTranslate",
      "ApiKey": "[SECRET]"
    },
    "Retention": {
      "ArchiveAfterYears": 2,
      "PermanentDeleteAfterYears": 7
    },
    "Features": {
      "EnableMarkdown": true,
      "EnableThreading": true,
      "EnableMentions": true,
      "EnableReactions": true,
      "EnableReadReceipts": true,
      "EnablePinning": true,
      "EnableSignalR": true,
      "EnableAiSuggest": false,
      "EnableTranslation": false,
      "EnableVirusScan": false,
      "EnableProfanityFilter": false,
      "EnablePiiDetection": true,
      "EnableTemplates": true,
      "EnableParticipants": true,
      "EnableMetrics": true,
      "EnableExport": false
    }
  }
}
```

---

# Phần X — Testing matrix

## 10.1 Unit test (target ≥ 80% coverage)

Per handler:
- `ChatEditCommandHandler`: edit trong/ngoài 15 phút, không phải author, ticket closed, edit_reason required cho Admin
- `ChatDeleteCommandHandler`: author vs admin, cascade soft delete mention/reaction/read
- `ChatRestoreCommandHandler`: Admin only
- `ChatReplyCommandHandler`: reply-of-reply bị block, cross-ticket parent bị block
- `ChatReactionAddCommandHandler`: duplicate reaction bị reject (unique constraint)
- `ChatMarkReadCommandHandler`: bulk insert idempotent
- `ChatPinCommandHandler`: max 3 pinned enforce
- `ChatMentionParser`: regex edge cases, không resolve được user → null
- `MarkdownRenderer`: XSS injection bị sanitize
- `TemplateRenderer`: placeholder không tồn tại → error
- `ParticipantAddCommandHandler`: Owner không bị remove được, Customer chỉ Admin remove
- `ChatAuthorizationHelper`: matrix permission đầy đủ

## 10.2 Integration test (TestContainers Postgres)

- Full flow add → edit → delete → restore
- Mention → publish event → consumer nhận
- Threaded reply hiển thị đúng order
- Full-text search trả đúng kết quả tiếng Việt có dấu
- SignalR broadcast tới group đúng
- Participant remove → chat cũ vẫn còn, force disconnect
- Reassign Staff → Staff cũ auto chuyển `PreviousAssignee`
- Reaction unique constraint enforce ở DB level
- Cascade delete: xóa ticket → cascade xóa chat/mention/reaction/read

## 10.3 Performance test

- 1000 chat trên 1 ticket — query pagination < 200ms
- SignalR broadcast 100 concurrent user — latency < 500ms
- Full-text search trên 1M chat — < 500ms
- Mark-read bulk 1000 chat — < 1s
- AI suggest latency < 3s (bao gồm gọi AI module)

## 10.4 Security test

- XSS payload trong markdown body → bị sanitize hoàn toàn
- SQL injection trong search query → tham số hóa
- Rate limit enforce đúng theo role
- Customer không thấy được internal chat qua bất kỳ endpoint nào
- Participant bị remove không gọi được SignalR hub group ticket-{id}

---

## Phụ lục — Tham chiếu

- **Rules dự án:** `.claude/rules/tech/be.md`
- **Business flow:** `.claude/docs/core-business-flow.md`
- **SignalR pattern tham khảo:** `services/SmsService/src/SmsService.Infrastructure/Realtime/SmsGatewayHub.cs`
- **Outbox pattern:** Đã có `OutboxMessage` entity + `OutboxMessageConfiguration`
- **Saga pattern:** Đã có `AlertTicketSagaStateConfiguration` làm tham khảo
- **TimescaleDB pagination pattern:** `be.md §13` — cursor-based reuse được cho chat cursor query

---

> **Author note:** Tài liệu này là **kế hoạch toàn diện**, không phải plan triển khai 1 sprint. Mỗi nhóm tính năng = 1 GitHub Issue riêng + 1 `plan.md` chi tiết theo template chuẩn dự án trước khi code.
>
> **Khi triển khai từng nhóm:**
> 1. Tạo GitHub Issue mô tả scope nhóm
> 2. `/kltn-plan {issue-number}` → viết `logs/GH-{number}/plan.md`
> 3. User approve plan
> 4. `/kltn-implement {issue-number}` → code
> 5. `/kltn-reviewcode` → `/kltn-test` → `/kltn-ship`
>
> **Last updated:** 2026-06-20
