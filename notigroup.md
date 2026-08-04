# Nhóm người nhận & quan hệ DB cho gửi thông báo hàng loạt

**Ngày:** 02/08/2026 · **Phạm vi:** NotificationService · **Trạng thái:** kế hoạch, chưa implement

> **Đã đưa vào sprint backlog:** [`overall.md`](overall.md) §17 **Sprint 6.4** (§17.6.4.0–17.6.4.6) —
> 15 task `#NOTI4-01..15`, ~13.5 dev-day (Phase A 3.5d · B 4.5d · C 5.5d).
> File này là **bản thiết kế chi tiết** (ERD, index, ràng buộc, lý do từng quyết định);
> `overall.md` là **bản dùng để lập issue**. Khi hai bên lệch nhau, file này là nguồn chi tiết.

Tài liệu này trả lời hai câu hỏi:

1. Đã có nhóm người dùng để gửi thông báo hàng loạt chưa?
2. Đã có quan hệ DB tối ưu chuyện **1 thông báo → nhiều người (nhiều nhóm)** và **1 người (1 nhóm) → nhiều thông báo** chưa?

Câu trả lời ngắn cho cả hai: **chưa**. Phần còn lại là kế hoạch để có.

---

## 0. Tóm tắt cho người vội

| Hạng mục | Hiện trạng | Sau kế hoạch này |
|---|---|---|
| Nhóm người dùng quản lý được | ❌ không có bảng nào | ✅ `notification_groups` + thành viên |
| Gửi cho nhiều người trong 1 lệnh | ❌ API chỉ nhận 1 `UserId` | ✅ gửi theo nhóm / nhiều nhóm / danh sách người |
| 1 thông báo → nhiều người (quan hệ DB) | ❌ không mô hình hoá, nội dung nhân bản từng dòng | ✅ `notification_batches` giữ nội dung 1 lần |
| 1 người → nhiều thông báo | ✅ đã có, có index `(user_id, status)` | ✅ giữ nguyên |
| 1 người thuộc nhiều nhóm | ❌ | ✅ bảng nối nhiều-nhiều |
| 1 nhóm nhận nhiều thông báo | ❌ | ✅ bảng nối nhiều-nhiều |
| Khoá ngoại trong NotificationService | ❌ **0 FK, 0 navigation property** | ✅ có FK trong cụm bảng mới |

Chia làm **3 giai đoạn độc lập triển khai được**, mỗi giai đoạn tự nó đã có giá trị và lùi lại được.

---

## 1. Hiện trạng — đo trên môi trường đang chạy

### 1.1 Không có nhóm

NotificationService có đúng 9 entity, DB có đúng 10 bảng. Không bảng nào mang khái niệm
group / segment / audience. AuthService cũng vậy (15 entity: Account, Role, Permission,
StaffProfile, StaffSkill, …).

Thứ gần nhất là `IRecipientResolver.GetActiveByRoleAsync(params string[] roles)` — nhưng nó **không
phải nhóm**:

- "Nhóm" chỉ là 4 role cố định, viết thẳng chuỗi trong code tại **15 chỗ** (`"Manager", "Admin"`).
  Không tạo / sửa / xoá / đặt tên được.
- Chỉ dùng nội bộ trong consumer. Không endpoint nào cho admin chọn nhóm để gửi.
- Endpoint gửi tay `POST /api/notifications` (`[Authorize(Roles = "Admin")]`) nhận đúng
  **một** `Guid UserId`; form trên web cũng chỉ chọn một người.
- Không command/query nào nhận `List<Guid>` / `UserIds` / `Roles`.

Fan-out hiện làm bằng **vòng lặp trong C#** (`NotificationWriter.WriteAsync`):
`foreach người → foreach kênh → AddAsync`.

### 1.2 Không có quan hệ DB

Theo nghĩa đen: **0 foreign key, 0 navigation property** trong toàn bộ NotificationService.
Grep `HasOne|HasMany|WithMany|ForeignKey` trong mọi file Configuration cho kết quả trống; không
entity nào có `ICollection<>`. `notifications.user_id` chỉ là cột `uuid` rời, không trỏ tới đâu.

Bảng `notifications` là **1 dòng = 1 người × 1 kênh**, nội dung chép lại nguyên văn từng dòng:

```
user_id       uuid NOT NULL     ← đúng một người
title         varchar(200)      ← lặp trên mọi dòng
body          varchar(2000)     ← lặp trên mọi dòng
payload_json  jsonb             ← lặp trên mọi dòng
```

Index hiện có:

```
PK_notifications                       (id)
IX_notifications_created_at            (created_at)
IX_notifications_entity_type_entity_id (entity_type, entity_id)
IX_notifications_user_id_status        (user_id, status)     ← chiều "1 người → nhiều thông báo"
ix_notifications_dispatch_queue        (status, next_attempt_at, created_at)
```

Số liệu đo được: **1.282 dòng / 9 người nhận / 242 "lần gửi" gom được theo
`(type, entity_id, giây)`**. Không có `batch_id` hay `campaign_id`, nên muốn biết "thông báo này đã
gửi cho những ai" phải gom mò theo thời gian.

Ba hệ quả cụ thể:

1. Gửi 100 người × 4 kênh = **400 dòng**, mỗi dòng chép lại title + body + payload.
2. **Không sửa / thu hồi được một lần gửi** — phải `UPDATE` N dòng mà không có khoá để tìm chúng.
3. **Không thống kê được** "đợt gửi này bao nhiêu người đã đọc".

> `IX_notifications_entity_type_entity_id` là thứ gần nhất với việc gom, nhưng nó gom theo *thực
> thể nghiệp vụ* (ticket X), không theo *lần gửi*. Nhiều đợt gửi khác nhau về cùng một ticket sẽ
> trộn lẫn — thấy rõ trong dữ liệu: cùng `type=9` + cùng `entity_id` có tới 50 dòng trong một giây.

### 1.3 Nền móng đã sửa xong hôm nay

Trước 02/08/2026, `account_read_models` — bảng nguồn duy nhất để resolve người nhận — chỉ có
**2/10** dòng, và **không có Admin nào**. Mọi consumer gọi `GetActiveByRoleAsync("Admin")` đều trả
rỗng rồi rơi vào nhánh `if (recipientIds.Count == 0) { log warning; return; }`.

Đã sửa (xem `CHANGELOG.md` mục 02/08/2026): nay read-model khớp 100% với `auth_db` trên cả 10 dòng,
và đổi role / đổi trạng thái / khôi phục tài khoản đều đồng bộ sang.

**Việc này là điều kiện cần của toàn bộ kế hoạch dưới đây.** Nhóm dù thiết kế đẹp đến đâu cũng vô
dụng nếu bảng người dùng phía sau nó thiếu người — chính xác là lỗi vừa sửa.

---

## 2. Bốn bài toán cần giải

| # | Bài toán | Vì sao hiện tại không giải được |
|---|---|---|
| B1 | Admin gom một nhóm người bất kỳ và gửi một lần | Không có bảng nhóm, API chỉ nhận 1 người |
| B2 | Một lần gửi lưu nội dung một lần, không nhân bản | Nội dung nằm trên từng dòng `notifications` |
| B3 | Trả lời "thông báo X đã tới ai, bao nhiêu người đọc" | Không có khoá gom lần gửi |
| B4 | Người ở trong 2 nhóm cùng được nhắm thì chỉ nhận **một** lần | Không có bước gom trùng, không có ràng buộc DB |

B4 là cái dễ bỏ sót nhất và là thứ sẽ gây lỗi thật: nếu Admin gửi cho cả nhóm "Quản lý" lẫn nhóm
"Trực sự cố" mà một người thuộc cả hai, người đó nhận hai lần y hệt.

---

## 3. Thiết kế dữ liệu

### 3.1 Sơ đồ

```
notification_groups ──1─┬─N── notification_group_members ──N─── (account_read_models.id)
                        │                                        ↑ KHÔNG đặt FK — xem §3.6
                        │
                        └─N── notification_batch_targets ──N─1── notification_batches
                                                                      │
                                                                      │ 1
                                                                      │
                                                                      N
                                                                 notifications
                                                              (1 dòng = 1 người × 1 kênh)
```

Đọc theo hai chiều người dùng hỏi:

- **1 thông báo → nhiều người / nhiều nhóm:** `notification_batches` 1─N `notification_batch_targets`
  N─1 `notification_groups` 1─N `notification_group_members`. Nội dung nằm ở batch, viết **một lần**.
- **1 người / 1 nhóm → nhiều thông báo:** `notifications.user_id` (đã có index) cho cá nhân;
  `notification_batch_targets.group_id` cho nhóm.

### 3.2 `notification_groups`

| Cột | Kiểu | Ghi chú |
|---|---|---|
| `id` | uuid PK | |
| `name` | varchar(128) NOT NULL | tên hiển thị |
| `description` | varchar(512) NULL | |
| `kind` | int NOT NULL | `Static = 1` \| `Role = 2` — xem §3.3 |
| `role_filter` | varchar(64) NULL | bắt buộc khi `kind = Role`, NULL khi `Static` |
| `is_system` | bool NOT NULL default false | nhóm hệ thống: không cho xoá/đổi tên |
| + `AuditableEntity` | | `created_at`, `created_by`, `updated_at`, `is_deleted`, `deleted_at` |

Ràng buộc & index:

```sql
-- Tên nhóm không trùng trong số nhóm còn sống. Partial index vì dự án dùng soft delete:
-- xoá nhóm rồi tạo lại cùng tên phải được.
CREATE UNIQUE INDEX ux_notification_groups_name
  ON notification_groups (lower(name)) WHERE is_deleted = false;

-- Mỗi role chỉ có đúng một nhóm động, tránh hai nhóm cùng nghĩa.
CREATE UNIQUE INDEX ux_notification_groups_role
  ON notification_groups (lower(role_filter)) WHERE kind = 2 AND is_deleted = false;

ALTER TABLE notification_groups ADD CONSTRAINT ck_notification_groups_role_filter
  CHECK ((kind = 2 AND role_filter IS NOT NULL) OR (kind = 1 AND role_filter IS NULL));
```

> ⚠️ Cả hai partial unique index đều **không deferrable** (Postgres không hỗ trợ deferrable cho
> partial unique index). Bài học đã trả giá ở `ux_notification_templates_active_per_key`: thao tác
> nào vừa tắt bản cũ vừa bật bản mới phải **lưu hai lần riêng** trong cùng transaction, tắt trước.
> Ở đây tình huống tương tự là "đổi tên nhóm A thành tên của nhóm B vừa xoá".

### 3.3 Hai loại nhóm — và vì sao cần cả hai

| | `Static` | `Role` |
|---|---|---|
| Thành viên | liệt kê tường minh trong `notification_group_members` | suy ra lúc gửi: mọi account có `role = role_filter` |
| Ai quản lý | Admin thêm/bớt tay | tự động theo role |
| Ví dụ | "Trực sự cố cuối tuần", "Khách hàng VIP" | "Toàn bộ Manager", "Toàn bộ Admin" |

Có loại `Role` **không phải để cho đủ bộ** — nó là đường di trú cho 15 chỗ hard-code hiện tại.
Seed 4 nhóm hệ thống (`Admin`, `Manager`, `Staff`, `Customer`, `is_system = true`), rồi
`IRecipientResolver` đổi từ "resolve theo chuỗi role" sang "resolve theo nhóm". Consumer giữ nguyên
hành vi, nhưng từ đó trở đi mọi lần gửi đều đi qua cùng một khái niệm nhóm — không còn hai cơ chế
song song.

### 3.4 `notification_group_members`

| Cột | Kiểu | Ghi chú |
|---|---|---|
| `id` | uuid PK | |
| `group_id` | uuid NOT NULL | **FK → `notification_groups.id`**, `ON DELETE CASCADE` |
| `user_id` | uuid NOT NULL | AccountId; **không** đặt FK — xem §3.6 |
| `added_by` | uuid NULL | ai thêm |
| + `AuditableEntity` | | |

```sql
-- Một người chỉ vào một nhóm một lần. Partial vì soft delete: bỏ khỏi nhóm rồi thêm lại phải được.
CREATE UNIQUE INDEX ux_notification_group_members_pair
  ON notification_group_members (group_id, user_id) WHERE is_deleted = false;

-- Chiều ngược: "người này đang ở những nhóm nào" (màn hình hồ sơ người dùng).
CREATE INDEX ix_notification_group_members_user
  ON notification_group_members (user_id) WHERE is_deleted = false;
```

Đây chính là quan hệ **nhiều-nhiều giữa người và nhóm**: một người ở nhiều nhóm, một nhóm nhiều
người.

### 3.5 `notification_batches` — nội dung một lần gửi

| Cột | Kiểu | Ghi chú |
|---|---|---|
| `id` | uuid PK | |
| `type` | int NOT NULL | `NotificationTypeEnum` |
| `title` | varchar(200) NOT NULL | **nội dung ở đây, không ở từng dòng người nhận** |
| `body` | varchar(2000) NOT NULL | |
| `payload_json` | jsonb NULL | |
| `entity_type` | varchar(100) NULL | |
| `entity_id` | uuid NULL | |
| `channels` | int[] NOT NULL | các kênh dự định gửi |
| `source` | int NOT NULL | `Event = 1` (consumer) \| `Manual = 2` (admin bấm gửi) |
| `template_id` | uuid NULL | mẫu đã dùng, nếu có |
| `status` | int NOT NULL | `Pending = 1` \| `FannedOut = 2` \| `Failed = 3` |
| `recipient_count` | int NOT NULL default 0 | số người **sau khi gom trùng** |
| `notification_count` | int NOT NULL default 0 | số dòng `notifications` đã sinh |
| + `AuditableEntity` | | `created_by` = admin bấm gửi, hoặc null nếu do event |

```sql
CREATE INDEX ix_notification_batches_created_at ON notification_batches (created_at DESC);
CREATE INDEX ix_notification_batches_entity ON notification_batches (entity_type, entity_id);
```

Đây là thứ trả lời B2 và B3: nội dung viết **một lần**, và có một khoá thật để hỏi "lần gửi này ra
sao".

### 3.6 Vì sao `user_id` **không** đặt khoá ngoại

`notification_group_members.user_id` và `notifications.user_id` trỏ tới người dùng, mà bảng người
dùng ở service này chỉ là **read-model** (`account_read_models`) đồng bộ qua message bus.

Đặt FK sang read-model sẽ tạo ra một lớp lỗi khó chịu: message đồng bộ tới **sau** thao tác dùng
`user_id` (thứ tự message không bảo đảm) → insert vỡ vì vi phạm FK → MassTransit retry → có khi
retry hết lượt vẫn hỏng. Đổi lấy được gì? Rất ít, vì read-model không phải nguồn sự thật; nguồn sự
thật là `auth_db` ở service khác, FK nội bộ không bảo vệ được gì trước sai lệch xuyên service.

Cách xử lý thay thế, rẻ hơn và đúng hơn:

- Lọc `IsActive` + `IsDeleted` bằng **JOIN lúc gửi** (§4.2), chứ không dựa vào ràng buộc DB.
- Thành viên nhóm trỏ tới account đã biến mất thì **để nguyên**, chỉ không được chọn khi gửi. Xoá
  thành viên theo `AccountDeletedEvent` là tuỳ chọn, không bắt buộc.
- Endpoint đối soát `POST /api/admin/accounts/resync` (đã có) là công cụ sửa lệch.

Ngược lại, `group_id` **có** FK: cả hai bảng cùng nằm trong `notification_db`, cùng một transaction,
không có gì bất định.

### 3.7 `notification_batch_targets` — batch nhắm tới ai

| Cột | Kiểu | Ghi chú |
|---|---|---|
| `id` | uuid PK | |
| `batch_id` | uuid NOT NULL | **FK → `notification_batches.id`**, `ON DELETE CASCADE` |
| `target_kind` | int NOT NULL | `Group = 1` \| `User = 2` |
| `group_id` | uuid NULL | **FK → `notification_groups.id`**; bắt buộc khi `target_kind = Group` |
| `user_id` | uuid NULL | bắt buộc khi `target_kind = User` |
| + `AuditableEntity` | | |

```sql
ALTER TABLE notification_batch_targets ADD CONSTRAINT ck_notification_batch_targets_shape
  CHECK ((target_kind = 1 AND group_id IS NOT NULL AND user_id IS NULL)
      OR (target_kind = 2 AND user_id  IS NOT NULL AND group_id IS NULL));

CREATE INDEX ix_notification_batch_targets_batch ON notification_batch_targets (batch_id);
-- "Nhóm này đã nhận những thông báo nào" — chính là chiều 1 nhóm → nhiều thông báo.
CREATE INDEX ix_notification_batch_targets_group ON notification_batch_targets (group_id)
  WHERE group_id IS NOT NULL;
```

Cho phép cả `Group` lẫn `User` trong cùng một batch, nên "gửi cho nhóm Quản lý **và** thêm anh A"
là một lần gửi, không phải hai.

### 3.8 Sửa `notifications`

Thêm **một** cột:

| Cột | Kiểu | Ghi chú |
|---|---|---|
| `batch_id` | uuid **NULL** | FK → `notification_batches.id`, `ON DELETE SET NULL` |

```sql
CREATE INDEX ix_notifications_batch ON notifications (batch_id) WHERE batch_id IS NOT NULL;

-- Ràng buộc cứng cho B4: trong một lần gửi, mỗi người mỗi kênh đúng MỘT dòng.
-- Đây là lưới an toàn cuối; gom trùng vẫn phải làm ở tầng ứng dụng (§4.2) để lỗi hiện ra
-- trước khi chạm DB.
CREATE UNIQUE INDEX ux_notifications_batch_user_channel
  ON notifications (batch_id, user_id, channel) WHERE batch_id IS NOT NULL;
```

Nullable là **có chủ đích**, không phải lười:

- 1.282 dòng đang có không thuộc batch nào — xem §7.
- `NotificationDigestBackgroundService` tổng hợp nhiều thông báo thành một bản digest cho một
  người; nó tự sinh nội dung riêng, không thuộc lần gửi nào.
- `NotificationDispatcher` cũng tạo dòng theo từng kênh trong lúc gửi.

Ba nguồn đó **không** ép vào mô hình batch ở giai đoạn đầu (xem §11 giai đoạn C).

---

## 4. Luồng nghiệp vụ

### 4.1 Quản lý nhóm

```
POST   /api/admin/notification-groups                  tạo nhóm
PUT    /api/admin/notification-groups/{id}             đổi tên / mô tả  (chặn nếu is_system)
DELETE /api/admin/notification-groups/{id}             xoá mềm          (chặn nếu is_system)
GET    /api/admin/notification-groups                  danh sách (PaginationRequest + SortHelper)
GET    /api/admin/notification-groups/{id}             chi tiết + số thành viên
POST   /api/admin/notification-groups/{id}/members     thêm nhiều người: { userIds: [...] }
DELETE /api/admin/notification-groups/{id}/members/{userId}
GET    /api/admin/notification-groups/{id}/members     danh sách thành viên (có phân trang)
```

Quy ước bắt buộc theo `.claude/rules/tech/be.md`:

- Phân trang dùng `PaginationRequest` + `QueryableExtensions.ToPagedEntityListAsync` trong
  `SharedInfrastructure` — **không tự viết Skip/Take**.
- Controller chỉ `_mediator.Send()`.
- Mọi query thêm `.Where(x => !x.IsDeleted)` (dự án không dùng global query filter).
- `GetAllAsync()` là **sync**; `UpdateAsync`/`DeleteAsync` là **void**.

Với nhóm `Role`, endpoint thành viên trả về kết quả suy ra từ `account_read_models` và
**từ chối** thêm/bớt tay (409) — thành viên do role quyết định.

### 4.2 Gửi hàng loạt

```
POST /api/admin/notifications/broadcast
{
  "type": 20,
  "channels": [1, 2],
  "title": "Bảo trì hệ thống",
  "body":  "Hệ thống bảo trì 22:00–23:00 ngày 05/08.",
  "groupIds": ["..."],          // nhắm theo nhóm
  "userIds":  ["..."],          // và/hoặc thêm cá nhân
  "templateId": null            // hoặc dùng mẫu + sampleData
}
```

Handler chạy đúng thứ tự sau:

```
1. Validate (IValidatable) — thu thập TẤT CẢ lỗi, không fail sớm:
   groupIds ∪ userIds phải khác rỗng · channels khác rỗng · title/body trong giới hạn cột
2. BeginTransactionAsync
3. Tạo notification_batches (status = Pending)
4. Tạo notification_batch_targets cho từng group / user
5. Nở người nhận:
      users_tĩnh   = members WHERE group_id ∈ groupIds AND kind = Static AND NOT is_deleted
      users_role   = account_read_models WHERE lower(role) ∈ role_filter của các nhóm kind = Role
      users_lẻ     = userIds
      → GOM TRÙNG bằng DISTINCT user_id       ← giải B4
      → JOIN account_read_models, GIỮ LẠI is_active = true AND is_deleted = false
6. Nếu tập rỗng → rollback, trả 400 "không có người nhận hợp lệ" (KHÔNG tạo batch rỗng im lặng)
7. Sinh notifications: mỗi (user × channel) một dòng, gán batch_id
8. Cập nhật batch: recipient_count, notification_count, status = FannedOut
9. CommitTransactionAsync
```

Bước 5 là trái tim. Ba chi tiết bắt buộc, thiếu cái nào cũng thành lỗi thật:

- **Gom trùng** — người ở hai nhóm cùng được nhắm chỉ nhận một lần (B4).
- **Lọc `is_active`** — người đã nghỉ / bị đình chỉ không nhận. Chỉ đúng được nhờ bản vá read-model
  ở §1.3; trước đó cột này không bao giờ được cập nhật.
- **Rỗng thì báo lỗi, không im lặng** — đây đúng là cách lỗi cũ ẩn mình suốt: consumer gặp danh
  sách rỗng thì `log warning; return`, nhìn từ ngoài y như đã gửi thành công.

### 4.3 Fan-out đồng bộ hay chạy nền?

**Khuyến nghị: đồng bộ trong handler cho giai đoạn đầu**, và đặt sẵn ngưỡng chuyển.

Lý do: hệ thống hiện có **10 tài khoản**. 10 người × 4 kênh = 40 dòng, một transaction là xong.
Thêm một background service + bảng trạng thái + đường retry vào lúc này là phức tạp không đổi lấy
gì — trái với "Simplicity First" trong rules.

Ngưỡng chuyển sang chạy nền, ghi luôn ra đây để sau này không phải đoán:

> Khi một lần gửi vượt **~2.000 dòng** `notifications` (khoảng 500 người × 4 kênh), hoặc khi thời
> gian phản hồi `POST /broadcast` vượt 2 giây, thì đổi sang: handler chỉ tạo `batch` với
> `status = Pending` rồi trả về ngay, một `NotificationFanoutBackgroundService` nở người nhận theo
> lô. Cột `status` và `recipient_count` ở §3.5 đã chừa sẵn cho đúng bước này — không cần đổi schema.

### 4.4 Theo dõi một lần gửi

```
GET /api/admin/notifications/batches            danh sách lần gửi (phân trang)
GET /api/admin/notifications/batches/{id}       chi tiết + thống kê
```

Thống kê lấy được nhờ `batch_id`, thứ hiện nay không có:

```sql
SELECT count(*)                                        AS tong_dong,
       count(DISTINCT user_id)                         AS so_nguoi,
       count(*) FILTER (WHERE status = 2)              AS da_gui,
       count(*) FILTER (WHERE read_at IS NOT NULL)     AS da_doc,
       count(*) FILTER (WHERE status = 3)              AS that_bai
FROM notifications WHERE batch_id = @batchId;
```

---

## 5. Danh sách file phải tạo

Theo đúng quy ước đặt tên ở `.claude/rules/tech/be.md` §15.

**Domain** (`NotificationService.Domain`)

```
Entities/NotificationGroup.cs              : AuditableEntity
Entities/NotificationGroupMember.cs        : AuditableEntity
Entities/NotificationBatch.cs              : AuditableEntity
Entities/NotificationBatchTarget.cs        : AuditableEntity
Enums/NotificationGroupKindEnum.cs         Static = 1, Role = 2
Enums/NotificationBatchSourceEnum.cs       Event = 1, Manual = 2
Enums/NotificationBatchStatusEnum.cs       Pending = 1, FannedOut = 2, Failed = 3
```

> Enum bắt đầu từ **1**, không phải 0. Entity **phải** extend `AuditableEntity`.

**Infrastructure**

```
Persistence/Configurations/NotificationGroupConfiguration.cs
Persistence/Configurations/NotificationGroupMemberConfiguration.cs
Persistence/Configurations/NotificationBatchConfiguration.cs
Persistence/Configurations/NotificationBatchTargetConfiguration.cs
Persistence/Seeders/NotificationGroupSeeder.cs        4 nhóm hệ thống theo role
Migrations/<timestamp>_AddNotificationGroupsAndBatches.cs
Repositories/  (thêm DbSet + property vào INotificationUnitOfWork / UnitOfWork)
```

**Application**

```
DTOs/Response/Notification/NotificationGroupDto.cs
DTOs/Response/Notification/NotificationGroupMemberDto.cs
DTOs/Response/Notification/NotificationBatchDto.cs
DTOs/Response/Notification/NotificationGroupResponses.cs      các lớp bọc CommonResponse<>
CQRS/Query/NotificationGroup/{GetList,GetById,GetMembers}Query.cs + Handler
CQRS/Command/NotificationGroup/{Create,Update,Delete,AddMembers,RemoveMember}Command.cs + Handler
CQRS/Command/Notification/NotificationBroadcastCommand.cs + Handler
CQRS/Query/Notification/{NotificationBatchGetList,NotificationBatchGetById}Query.cs + Handler
Services/IRecipientResolver.cs        thêm GetGroupRecipientsAsync(IEnumerable<Guid> groupIds, ...)
```

**Api**

```
Controllers/AdminNotificationGroupsController.cs
Controllers/AdminNotificationsController.cs    (broadcast + batches) — hoặc thêm vào controller sẵn có
```

---

## 6. Ảnh hưởng lên code hiện có

Toàn bộ hệ thống chỉ có **4 nơi** INSERT vào `notifications` — đã kiểm chứng bằng grep:

| Nơi | Số chỗ gọi | Kế hoạch |
|---|---|---|
| `NotificationWriter.WriteAsync` | 20 lời gọi / 13 file consumer | **Chỉ sửa 1 helper.** Thêm tham số `batchId` tuỳ chọn; consumer không phải sửa |
| `CreateNotificationCommandHandler` | 1 | Giữ nguyên (gửi 1 người). Có thể để `batch_id = NULL` hoặc tạo batch 1 target |
| `NotificationDigestBackgroundService` | 1 | Giữ nguyên — bản digest không thuộc lần gửi nào |
| `NotificationDispatcher` | 1 | Giữ nguyên |

Điểm đáng giá nhất của thiết kế này: **15 chỗ broadcast theo role đều đi qua đúng một helper**, nên
thêm khái niệm batch không đụng vào 13 file consumer.

`IRecipientResolver.GetActiveByRoleAsync` **giữ nguyên chữ ký** ở giai đoạn A và B; chỉ đổi phần
ruột để đọc qua nhóm `Role`. Consumer không biết gì về thay đổi này.

---

## 7. Di trú dữ liệu đang có

1.282 dòng `notifications` hiện tại không thuộc batch nào. Hai phương án:

| | Phương án A — để `batch_id = NULL` | Phương án B — mỗi dòng cũ một batch |
|---|---|---|
| Đúng dữ liệu | ✅ không bịa | ✅ không bịa |
| Chi phí | 0 | +1.282 dòng batch |
| Màn hình "lần gửi" | không thấy dữ liệu cũ | thấy, nhưng mỗi lần gửi chỉ 1 người |

**Khuyến nghị: phương án A.** Dữ liệu cũ không có thông tin để gom thành lần gửi — gom theo
`(type, entity_id, giây)` là **suy đoán**, và §1.2 đã cho thấy nó gom nhầm (50 dòng cùng một giây
cùng một entity thực chất là rác test tải). Bịa ra nhóm không có thật trong dữ liệu lịch sử tệ hơn
là thừa nhận không có.

Màn hình danh sách lần gửi ghi rõ: *"chỉ hiển thị các lần gửi từ 02/08/2026"*.

> ❌ **Tuyệt đối không** gom dòng cũ theo thời gian rồi gán chung `batch_id`. Sẽ ra những "lần gửi"
> chưa từng tồn tại, và không có cách nào phát hiện sau này.

Migration phải thoả checklist ở rules §14: có `Down()` chạy được, cột thêm vào bảng có dữ liệu phải
nullable hoặc có default, và đã test rollback → apply lại.

---

## 8. Frontend

| Màn hình | Việc |
|---|---|
| Sidebar → mục mới "Nhóm nhận thông báo" | danh sách nhóm + phân trang (`useUrlFilters` + `DataPagination`) |
| Dialog tạo/sửa nhóm | tên, mô tả, loại nhóm (Tĩnh/Theo role) |
| Màn hình thành viên | thêm/bớt người, tìm kiếm; nhóm `Role` thì chỉ xem |
| "Gửi thông báo" — sửa lại | đổi ô chọn **một** người thành chọn **nhiều nhóm + nhiều người**, có xem trước số người nhận |
| "Lịch sử gửi" | danh sách batch + thống kê đã gửi / đã đọc / thất bại |

Bám quy ước FE: enum dùng `as const` object (không dùng `enum` của TypeScript), nhãn tiếng Việt để
ở `shared/constants/`, gọi API qua `services/` → hook TanStack Query, không gọi thẳng trong component.

> ⚠️ Có sẵn một cái bẫy: khoảng **15 dropdown** hiện đang gọi `pageSize: 100` rồi coi kết quả là
> đầy đủ. Ô chọn người nhận **không được** làm theo kiểu đó — quá 100 người là âm thầm mất người.
> Dùng tìm kiếm phía server hoặc cuộn vô hạn.

---

## 9. Kiểm thử

**Unit (bắt buộc, mock `IUnitOfWork`)**

- Gom trùng: người ở 2 nhóm cùng được nhắm → đúng **1** dòng mỗi kênh (B4).
- Lọc người không hoạt động: `is_active = false` không lọt vào danh sách.
- Tập người nhận rỗng → 400, và **không** tạo batch mồ côi.
- Nhóm `Role` → resolve đúng từ `account_read_models`, không phải từ bảng thành viên.
- Nhóm hệ thống: đổi tên/xoá → 409.
- Thêm thành viên trùng → không tạo dòng thứ hai (chạm partial unique index).
- `recipient_count` / `notification_count` khớp số dòng thực sinh.

**Integration**

- Gửi cho 2 nhóm giao nhau → đếm dòng trong DB đúng bằng `|hợp| × số kênh`.
- Xoá nhóm → `ON DELETE CASCADE` dọn thành viên, batch cũ **vẫn còn** (lịch sử không mất).
- Migration rollback → apply lại, không lỗi.

**Bất biến DB — kiểm sau mỗi kịch bản**

```sql
-- Phải luôn ra 0
SELECT count(*) FROM (
  SELECT batch_id, user_id, channel FROM notifications
  WHERE batch_id IS NOT NULL GROUP BY 1,2,3 HAVING count(*) > 1) t;

-- Batch đã fan-out mà không có dòng nào — phải luôn ra 0
SELECT count(*) FROM notification_batches b WHERE b.status = 2
  AND NOT EXISTS (SELECT 1 FROM notifications n WHERE n.batch_id = b.id);
```

Cổng chất lượng: BE ≥ 80% line coverage, `dotnet test` xanh toàn bộ 6 service + shared.

---

## 10. Lộ trình — 3 giai đoạn

| GĐ | Task (overall.md §17) | Công | Nội dung | Giá trị thu được ngay | Rủi ro |
|---|---|---|---|---|---|
| **A** | `NOTI4-01..05` | **3.5d** | `notification_groups` + `notification_group_members` + CRUD + seed 4 nhóm hệ thống + `IRecipientResolver` đọc qua nhóm | Admin quản lý được nhóm; 15 chỗ hard-code role thành dữ liệu | Thấp — chưa đụng `notifications` |
| **B** | `NOTI4-06..10` | **4.5d** | `notification_batches` + `notification_batch_targets` + `notifications.batch_id` + API broadcast + permission | **Giải trọn B1–B4.** Gửi hàng loạt thật, nội dung lần gửi lưu một lần, thống kê được | Trung bình — thêm cột vào bảng nóng, nhưng nullable |
| **FE** | `NOTI4-11..15` | **5.5d** | Màn hình nhóm · thành viên · gửi nhiều nhóm · lịch sử gửi + test + doc | Dùng được thật, không chỉ qua API | Thấp |
| **C** | *(chưa lập task)* | ~2d | Bỏ `title`/`body`/`payload_json` khỏi `notifications`, đọc qua batch | Hết nhân bản nội dung hoàn toàn | **Cao** — đổi đường đọc nóng nhất; chỉ làm khi đo được lợi ích |

**Khuyến nghị: làm A và B. Hoãn C** cho tới khi có số đo thật. C tiết kiệm dung lượng nhưng bắt
`GET /api/notifications` (truy vấn nóng nhất) phải JOIN thêm một bảng, và ép ba nguồn INSERT còn lại
ở §6 vào mô hình batch mà chúng không tự nhiên thuộc về. Với quy mô hiện tại (1.282 dòng), đổi chác
này chưa đáng.

Sau A + B thì cả hai câu hỏi ban đầu đều đã có câu trả lời "rồi": có nhóm quản lý được, và có quan
hệ DB đúng cho cả hai chiều.

---

## 11. Rủi ro & bẫy đã biết

| Rủi ro | Cách chặn |
|---|---|
| Người ở 2 nhóm nhận trùng | `DISTINCT` ở tầng ứng dụng **+** unique index `(batch_id, user_id, channel)` |
| Gửi cho người đã nghỉ / bị khoá | JOIN `account_read_models` lọc `is_active` — chỉ đúng nhờ bản vá §1.3 |
| Read-model lệch trở lại | `POST /api/admin/accounts/resync` (đã có); nên có cảnh báo khi số dòng lệch với `auth_db` |
| Partial unique index không deferrable | Thao tác đụng khoá phải lưu hai lần riêng trong cùng transaction — như `NotificationTemplate` đã làm |
| Tập người nhận rỗng bị bỏ qua im lặng | Trả 400 tường minh, không `log warning; return` |
| Xoá nhóm làm mất lịch sử gửi | `batch_targets.group_id` là FK nhưng batch **không** cascade theo nhóm; nhóm xoá mềm |
| Dropdown chọn người nhận cắt ở 100 | Tìm kiếm phía server, không `pageSize: 100` |
| Fan-out lớn làm treo request | Ngưỡng ~2.000 dòng → chuyển sang background (§4.3) |
| Bịa batch cho dữ liệu cũ | Phương án A ở §7 — để NULL, ghi rõ trên UI |

---

## 12. Cần chốt trước khi code

1. **Nhóm lồng nhau?** (nhóm chứa nhóm). Kế hoạch này **không** hỗ trợ — nó kéo theo phát hiện chu
   trình và đệ quy lúc nở người nhận. Nếu cần thì phải bàn riêng.
2. **Ai được tạo nhóm?** Kế hoạch mặc định **Admin**. Nếu Manager cũng được thì cần bàn phạm vi
   nhìn thấy (Manager có thấy nhóm của Manager khác không).
3. **Người dùng tự vào/ra nhóm được không?** Mặc định **không** — chỉ Admin. Nếu có thì cần thêm
   khái niệm nhóm mở và endpoint tự đăng ký.
4. **Batch có lịch gửi không?** Kế hoạch này gửi ngay. Muốn hẹn giờ thì thêm `scheduled_at` vào
   `notification_batches` — cột này thêm sau cũng được, không phá schema.
5. **Có tôn trọng `notification_preferences` / quiet hours khi gửi hàng loạt không?** Cần chốt:
   thông báo bảo trì hệ thống thì nên vượt quiet hours hay không. Hiện `NotificationDispatcher` đã
   lọc theo preference ở bước gửi xuống kênh — cần xác nhận broadcast cũng đi qua đó.

---

## Phụ lục — lệnh đo lại hiện trạng

```bash
# Số bảng của NotificationService
docker exec solar-postgres psql -U postgres -d notification_db -c "\dt"

# Đối chiếu read-model với nguồn sự thật (phải khớp 100%)
docker exec solar-postgres psql -U postgres -d auth_db -t -A -F'|' -c \
  "SELECT a.id, lower(a.email), COALESCE(r.name,''), (a.status IN (1,2) AND NOT a.is_deleted)::text, a.is_deleted::text
   FROM accounts a LEFT JOIN roles r ON r.id=a.role_id ORDER BY a.id;" > /tmp/auth.txt
docker exec solar-postgres psql -U postgres -d notification_db -t -A -F'|' -c \
  "SELECT id, email, role, is_active::text, is_deleted::text FROM account_read_models ORDER BY id;" > /tmp/read.txt
diff /tmp/auth.txt /tmp/read.txt && echo "khớp"

# Số người mỗi nhóm broadcast hiện resolve được
docker exec solar-postgres psql -U postgres -d notification_db -c \
  "SELECT count(*) FROM account_read_models WHERE NOT is_deleted AND is_active AND lower(role)='admin';"

# Xác nhận NotificationService vẫn chưa có khoá ngoại nào
grep -rn "HasOne\|HasMany\|WithMany\|ForeignKey" \
  services/NotificationService/src/NotificationService.Infrastructure/Persistence/Configurations/
```
