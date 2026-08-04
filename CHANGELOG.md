# Changelog

Tuân theo [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) format.
Versions tuân theo [SemVer](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- **Xem nhanh thành viên từng nhóm ngay trên form gửi hàng loạt** (`GroupMemberPeek.tsx`).

  Ô chọn nhóm trước đây chỉ hiện tên và một con số — admin sắp gửi cho "Toàn bộ Khách hàng" không
  biết 2 người đó là ai nếu không mở sang màn hình khác, mà gửi thông báo là việc **không thu hồi
  được**. Nay mỗi nhóm có nút mở/đóng, mặc định đóng và **chỉ gọi API khi mở**.

  Nút mở đặt **ngoài** `<label>`: để trong thì bấm xem sẽ tick luôn ô chọn nhóm. Người đã ngừng hoạt
  động **vẫn hiện** (làm mờ) kèm câu giải thích vì sao số dòng nhiều hơn con số người nhận — ẩn đi
  thì admin không hiểu hai số lệch nhau ở đâu, và cũng không biết để dọn.

- **Tuỳ chọn "dùng mẫu" khi gửi thông báo hàng loạt thủ công.**

  | | `useTemplate = false` (mặc định) | `useTemplate = true` |
  |---|---|---|
  | Nội dung gửi đi | Chữ admin gõ, y nguyên | Dispatcher render mẫu `(Loại × Kênh)` với biến admin điền |
  | `title`/`body` | Chính là nội dung | **Nội dung dự phòng** cho kênh không có mẫu |

  **Vì sao render lúc gửi chứ không đổ sẵn chữ vào ô soạn:** mẫu khoá theo cặp `(Loại × Kênh)` và
  bản SMS được nén ngắn lại (tính tiền theo đoạn), nên một lần gửi 3 kênh cho ra **3 nội dung khác
  nhau** — một ô nhập duy nhất không tạo ra được ba bản đó.

  **Vì sao thêm cờ `use_template` chứ không suy ra từ `template_id`:** cột `template_id` đã có sẵn
  nhưng chỉ chứa **một** id, trong khi một lần gửi 3 kênh dùng **3 mẫu**. Không có "một" template id
  để ghi. Migration `AddNotificationBatchUseTemplate`, mặc định `false` nên các lần gửi cũ giữ
  nguyên hành vi.

  Kèm ba lớp để admin không gửi mù:

  1. **`POST /api/admin/notifications/broadcast/template-preview`** (mới) — trả **một dòng mỗi
     kênh**, dựng model **đúng khuôn** `NotificationDispatcher.BuildTemplateModel` nên chữ xem trước
     bằng đúng chữ gửi thật. Trực tiếp rút từ bài học "xem trước thấy đúng nhưng gửi đi lại khác".
  2. **`missingVariables` từng kênh** — admin thấy chỗ trống *trước khi* bấm gửi.
  3. **Chặn biến lạ khi gửi** — 400 kèm danh sách biến hợp lệ, cùng cơ chế với trình soạn mẫu.

  Giao diện: ô điền biến lấy từ **chính mẫu của các kênh đang chọn**, không phải toàn bộ khoá payload
  của loại đó — loại `System` khai 5 khoá của bản tin gom nhưng mẫu của nó chuyển tiếp nguyên văn nên
  không cần ô nào.

### Changed

- **Sprint 6.5 đợt 2 — ba thay đổi hợp đồng event, đều tương thích ngược.**

  | Event | Thêm | Vì sao |
  |---|---|---|
  | `BatteryAnomalyDetectedEvent`<br>`BatteryAnomalyWarningDetectedEvent` | `AnomalyTypeName`<br>`SeverityName` | `AnomalyTypeEnum`/`AlertSeverityEnum` thuộc `BatteryService.Domain` nên subscriber **không tham chiếu được**, chỉ nhận số trần ⇒ thông báo gửi khách ghi *"Loại: 4 — Mức độ: 3"* |
  | `SlaWarningEvent`<br>`SlaBreachedEvent` | `Code` | Payload chỉ có `TicketId` (GUID) nên thông báo vỡ SLA **không nhắc được ticket nào** |

  Cả ba trường đều **nullable / mặc định rỗng**: event cũ đã nằm trong Outbox và hàng đợi
  deserialize ra `null`, phía nhận tự lùi — `BatteryAnomalyLabels` trả về con số, câu SLA lược phần
  mã đi thay vì hiện `"Ticket  "`. Không cần dừng hệ thống để nâng cấp.

  Vì sao **không** tự dựng bảng tra số ở phía nhận: BatteryService chèn thêm một giá trị vào giữa
  enum là mọi nhãn dịch lệch — đúng tai nạn `NotificationTypeEnum` đã dính khi module Blog chiếm mất
  25/26. Bên **sở hữu** enum gửi kèm tên, phía nhận tra **theo tên**, là chỗ duy nhất luôn đúng.
  Cùng khuôn `OldStatusName`/`NewStatusName` của `TicketStatusChangedEvent`.

### Removed

- **`NotificationTypeEnum.AdminInvite = 13` — gỡ hẳn** khỏi enum, `NotificationCategoryMap`, ma trận
  kênh, danh mục template, và cả frontend lẫn mobile.

  Thư mời quản trị đi thẳng `AuthService → EmailService` (`SendAdminInviteEvent` →
  `SendAdminInviteConsumer`), **không qua NotificationService**. Người được mời **chưa có tài khoản**
  nên không thể nhận thông báo in-app, và nối consumer vào đây sẽ **gửi trùng email**. Ở đây nó là
  enum không có producer: không consumer nào ghi, không dòng `notifications` nào mang type 13, nhưng
  vẫn chiếm một ô trong ma trận kênh và một template trong DB — người vận hành sửa template đó mãi
  mà chẳng đổi được gì.

  Đây đúng thứ mà ghi chú NOTI-04 trong chính file enum đã chốt tránh ("không đẻ enum không
  producer"), chỉ là lúc đó bỏ sót dòng này. **Số 13 để trống vĩnh viễn** — dữ liệu cũ có thể còn
  tham chiếu.

### Fixed

- **Gửi hàng loạt thủ công bị template đè mất nội dung admin gõ.**

  Màn hình gửi hàng loạt cho chọn **bất kỳ** loại thông báo nào (loại quyết định nhóm tuỳ chọn nhận
  tin nên không ép về mỗi `System` được). Dòng sinh ra ở trạng thái `Pending` nên **có** đi qua
  dispatcher, và dispatcher tra template theo `(Type × Channel)`. Chọn "Ticket mới" rồi gõ tay tiêu
  đề thì template `TicketCreated` khớp và render — nhưng payload của một lần gửi tay **không có**
  `code`, nên ra `"Ticket mới "` với chỗ trống, chữ admin vừa gõ **biến mất sạch**.

  Đo trên hệ thống thật: admin gõ `"KTMPL Tiêu đề admin tự gõ"`, người nhận thấy `"Ticket mới "`.

  Lỗi có từ khi có tính năng gửi hàng loạt và đã âm thầm áp cho **Email/Push/SMS** —
  `RenderContentAsync` chạy cho mọi kênh. Riêng InApp thì kết quả render vốn bị vứt đi nên không ai
  thấy; tới khi InApp ghi ngược nội dung vào dòng notification thì nó lộ ngay trên feed.

  `RenderContentAsync` nay bỏ qua template khi dòng thuộc một lần gửi có `Source = Manual`. Nội dung
  do người viết ra là thứ có thẩm quyền, không có gì để khuôn mẫu hoá. Lần gửi sinh **tự động từ sự
  kiện** vẫn qua template như thường — có test riêng khoá đúng ranh giới đó.

- **`SlaWarningEvent.StaffId` luôn `null`** — cảnh báo sắp vỡ SLA chỉ tới Manager, **không tới Staff
  đang phụ trách**.

  `SlaTimerBackgroundService` đọc `timer.Ticket?.Assignments.FirstOrDefault(...)` nhưng truy vấn chỉ
  có `.Include(t => t.Ticket)`, không `ThenInclude(Assignments)`. Dự án không bật lazy loading và
  `Assignments` khởi tạo sẵn là danh sách rỗng ⇒ `FirstOrDefault` luôn trả `null`, âm thầm, không
  lỗi, không log. Đúng thứ mà NOTI-05 (#676) thêm trường `StaffId` để chữa: trường đã thêm từ
  Sprint 6.2, dữ liệu thì chưa bao giờ được nạp. Tìm thấy khi rà ba chỗ phát event SLA để điền `Code`.

- **Bản tin gom (digest) có thể vượt giới hạn cột `body varchar(2000)`.** Mỗi mục con được phép dài
  2000, mà cột đích cũng chỉ 2000 — chỉ cần **hai mục dài** là Postgres ném lỗi và hỏng cả vòng gom.
  `BuildBody` nay dựng dần theo dòng, dừng khi dòng kế tiếp làm vượt trần, luôn chừa chỗ cho dòng
  "… và N thông báo khác", và cắt cứng ở cuối làm chốt chặn. Cắt ở **ranh giới dòng** chứ không giữa
  câu. Chưa từng nổ vì hệ thống chưa sinh bản tin gom nào — bẫy chờ sẵn, không phải chuyện không xảy ra.

- **Thông báo pin hiển thị số thay vì chữ.** Template và thân tin nhắn ghi "Loại: 4 — Mức độ: 3".
  Nay `BatteryAnomalyLabels` quy tên enum về tiếng Việt (`Overheat` → *Quá nhiệt*, `Critical` →
  *Nghiêm trọng*); tên lạ ⇒ hiện chính tên đó (tiếng Anh, vẫn hiểu được) chứ không trơ ra số.
  Có test khoá: template pin **không được** dùng lại hai khoá số `{{anomalyType}}`/`{{severity}}` —
  chúng vẫn hợp lệ nên bộ kiểm tên biến không chặn, chỉ test này chặn.

- **Thông báo SLA không nhắc được mã ticket.** Nay tiêu đề và thân đều mang `{{code}}`.

- **Sprint 6.5 `#NOTI5-01..12` — Template thông báo: sáu lỗi im lặng.** Xuất phát từ câu hỏi
  "template đang được áp dụng vào đâu". Khảo sát ra đúng một điểm dùng lúc chạy
  (`NotificationDispatcher.RenderContentAsync`), rồi lộ ra sáu lỗi độc lập — **không lỗi nào có
  log, metric hay test bắt được**.

  1. **Template gọi sai tên biến trên diện rộng.** `{{ticketCode}}` trong khi consumer ghi khoá
     `code`; `{{serialNumber}}` trong khi consumer ghi `assetSerialNumber`; `{{threshold}}` ↔
     `thresholdValue`; cộng sáu biến **không tồn tại ở bất kỳ loại nào** (`customerName`,
     `slaDeadline`, `minutesRemaining`, `senderName`, `preview`, `displayName`).
     Handlebars gặp biến lạ **render ra chuỗi rỗng chứ không ném**, nên 37 thông báo
     `TicketCreated` và 1.229 thông báo pin đã gửi đi với chỗ trống giữa câu.
  2. **Kênh InApp render xong rồi vứt kết quả đi.** `InAppChannel` dùng `request.Title`/`Body`
     **0 lần** — nó chỉ đặt `Status = Sent` rồi đẩy realtime. Tức 548/1285 dòng (43%) tốn một
     truy vấn và một lượt render mỗi dòng mà không ai thấy, còn 33 template InApp thì sửa được,
     xem trước được, gửi thử được, nhưng **sửa xong không đổi được chữ nào** trên màn hình.
  3. **`TicketMerged = 27` trùng nguyên vẹn `ChatEscalatedToAdmin = 27`.** Hệ quả:
     `ToString()` chỉ trả về một tên, và khoá duy nhất `(type, channel)` của bảng template khiến
     hai loại thông báo này **không thể** có template riêng. Đổi sang **34**, hoàn tất `GH-83` mà
     mobile đã chốt từ trước. An toàn vì chưa từng có dòng `notifications` nào mang type 27.
  4. **`NotificationCategoryMap` chưa từng khai `TicketMerged`** — nó ăn theo nhóm `Sla` của
     `ChatEscalatedToAdmin` nhờ lỗi #3, nên thông báo "ticket đã gộp" bị xếp nhầm nhóm SLA và tuỳ
     chọn nhận thông báo theo nhóm áp sai. **Test bao vẫn xanh suốt thời gian đó.** Chỉ lộ ra khi
     tách số ở #3.
  5. **Template lệch type đúng 2 bậc.** Module Blog `GH-671` chiếm giá trị 25/26 đẩy nhóm sau lên
     hai, nhưng các dòng seed cũ giữ nguyên số ⇒ type 30 (`TicketReopened`) mang câu
     "Cảnh báo pin", type 25 (`BlogGenerationCompleted`) mang câu của `ChatEscalatedToAdmin`.
  6. **Ma trận kênh lệch consumer.** `BatteryAnomalyDetectedConsumer` gửi bằng
     `NotificationWriter.AllChannels` (có SMS) nên **98 tin SMS đã gửi**, trong khi ma trận không
     khai SMS — mà seeder lại dựng template theo ma trận ⇒ SMS không template nào phủ.

  **Căn nguyên:** không có hợp đồng nào giữa bên ghi payload và bên đọc payload. Consumer dựng
  payload bằng anonymous object (không có kiểu để phản chiếu), người soạn template phải **tự đoán**
  tên khoá. Màn hình xem trước lại nhận dữ liệu mẫu **do chính client gõ vào**, nên gõ
  `{"ticketCode":"TK-1"}` là xem trước hiện ra đẹp đẽ trong khi gửi thật ra chỗ trống.

  **Thứ tự thi công là bắt buộc, không phải tuỳ chọn:** lỗi #2 đang *che* lỗi #1 trên kênh InApp.
  Nếu chỉ làm mỗi việc "cho InApp dùng template" mà không sửa #1 trước thì 1.229 thông báo pin sẽ
  hiện "Bất thường pin " với chỗ trống — biến một lỗi vô hại thành lỗi nhìn thấy được.

### Added

- **`NotificationTemplateVariables`** — danh mục biến hợp lệ theo từng loại thông báo, trích từ
  chính code sinh payload. Là nguồn sự thật cho validate, xem trước, và gợi ý trên giao diện.
- **`TemplateVariableGuard`** — bóc `{{bien}}` (bỏ qua chú thích, thẻ đóng, partial, tên helper),
  đối chiếu danh mục, và **gợi ý tên đúng** bằng quan hệ chứa nhau + khoảng cách Levenshtein
  (`{{serialNumber}}` → gợi ý `{{assetSerialNumber}}`). Nối vào cả `create` lẫn `revise`; nhánh
  `revise` lấy type từ **bản gốc** vì người sửa không truyền type lên. Sai biến ⇒ **400 kèm gợi ý**,
  chặn chứ không cảnh báo: lưu xong là có hiệu lực ngay cho mọi thông báo của cặp đó.
- **`GET /api/admin/notification-templates/variables`** — biến dùng được cho từng loại.
- **`GET /api/admin/notification-templates/coverage`** — độ phủ template tính theo **dữ liệu thật
  đã sinh** (không theo ma trận cấu hình, vì chính hai thứ đó từng lệch nhau), kèm danh sách biến
  hỏng của từng template.
- **Seeder tự hội tụ** bản template đã trôi khỏi danh mục, theo luật cố ý hẹp: seeder-origin khác
  danh mục ⇒ thay · người vận hành soạn mà hỏng biến ⇒ thay (bản hỏng **vẫn nằm trong lịch sử phiên
  bản**) · người vận hành soạn mà biến hợp lệ ⇒ **không đụng tới**. Tự dừng sau khi hội tụ.
- **InApp ghi ngược** nội dung đã render vào chính dòng notification, kèm ba chốt: chỉ ghi một lần
  (dispatcher chỉ nhặt `Pending` + chốt idempotent) · render rỗng thì giữ nguyên nội dung gốc ·
  **cắt theo giới hạn cột** — `title_template` tối đa 500 và `body_template` tối đa 4000 trong khi
  cột `title` chỉ 200 và `body` chỉ 2000; không cắt thì Postgres ném lỗi và dòng đó kẹt retry
  vĩnh viễn.
- **3 test bao chống trôi hợp đồng**: template seed chỉ dùng biến có thật · mọi khoá khai báo đều
  xuất hiện ở một consumer/background job · mọi khoá consumer ghi đều đã khai báo. Cộng test
  "không có hai type nào trùng giá trị".

  560/560 unit test xanh. Đã **tái tạo lại từng bug** để chứng minh test bắt đúng chỗ, không test
  nào đỏ oan — chi tiết ở `overall.md` §17.6.5.4.

### Security

- **`#AUTH-05` (P0) — CORS: thay `AllowAll` bằng whitelist.** Trước đây `AddCORS` đặt cứng
  `SetIsOriginAllowed(origin => true)` kèm `AllowCredentials()` — bất kỳ website nào trên Internet
  cũng gọi được API bằng cookie/credential của user đang đăng nhập.
  Nay đọc `Cors:AllowedOrigins` (mảng) từ config; policy đổi tên `AllowAll` → `AppCors`
  (`AddCORS.PolicyName`) vì tên cũ gây hiểu nhầm. Cập nhật 7 call site `app.UseCors(...)`.
  - **Development** để trống danh sách → vẫn cho mọi origin, có in cảnh báo.
  - **Production** để trống danh sách → **service TỪ CHỐI KHỞI ĐỘNG**. Cố ý: thà không lên còn hơn
    lên với CORS mở toang; chỉ log cảnh báo thì sẽ không ai đọc.
  - Origin có dấu `/` cuối được chuẩn hoá (`WithOrigins` so khớp chuỗi nguyên văn, dễ trượt im lặng).
  - ⚠️ **CÒN TREO:** danh sách domain production **chờ Leader chốt**. Placeholder đã để sẵn (comment)
    trong `.env.Docker`. Đây là phần duy nhất của `#AUTH-05` chưa xong — cơ chế đã hoàn tất và có
    5 test phủ (`CorsExtensionsTests`), trong đó có test khẳng định Production thiếu config thì ném lỗi.


> **Dashboard aggregate endpoints** theo yêu cầu FE (FE đang tự đếm KPI trên 1 trang list → sai số khi vượt pageSize). Docs chi tiết: `docs/api-ticket.md` / `docs/api-battery.md` / `docs/api-auth.md` (changelog 2026-07-07). Issue/PR number gán khi ship.

### Added

- `GET /api/tickets/dashboard/stats` (Manager/Admin) — snapshot KPI ticket toàn hệ thống: total/open, SLA summary + compliance, countByStatus (zero-fill 14 status), countByPriority, trend tạo mới 7 ngày (UTC), workload theo staff.
- `GET /api/staff/tickets/dashboard/stats` (Staff) — snapshot KPI per-staff từ JWT: open/resolved, near-breach ≤25% / breached / paused / slaMonitored, slaRisk, countByStatus, trend 7 ngày.
- `GET /api/sites/dashboard/stats` (Admin/Manager) — snapshot toàn bộ site: total/active, tổng pin (khớp battery stats), avg health (công thức dùng chung `SiteHealthCalculator` với per-site dashboard), at-risk < 80.
- `GET /api/admin/accounts/stats` (Admin/Manager) — total account + countByRole (zero-fill theo bảng Roles).
- `GET /api/staff/tickets/me`: thêm query param `SlaOpen` (filter server-side nhóm ticket đang theo dõi SLA — bảng SLA Monitor hết cap 100) + `SortBy=slaRemaining` (sort theo dueAt tăng dần, ticket không timer xếp cuối).
- `AccountDto.isGoogleLinked` (bool) — `GET /api/auth/me` và mọi endpoint trả AccountDto; FE màn Cài đặt toggle nút Liên kết Google.
- `KbArticleSuggestDTO.isInternalOnly` (bool).

### Added

- **Soạn thảo notification template từ giao diện quản trị — `POST` / `PUT` / `DELETE` / `GET {id}`.** Trước đó controller chỉ có 4 endpoint đọc-và-bật; nội dung template **chỉ** đến từ seeder, mà seeder idempotent theo cặp `(Type × Channel)` nên sửa catalog rồi deploy lại cũng không ghi đè bản đã có — cách duy nhất để đổi một câu chữ là chạy SQL tay. Mục tiêu ghi trong chính XML doc của tính năng ("có template trong DB thì người vận hành sửa được ngay, khỏi build lại") chưa bao giờ đạt được, và cả cơ chế phiên bản (cột `Version`, partial unique index, endpoint `activate`, nút "Kích hoạt" trên giao diện) là **code chết** vì không gì tạo ra được phiên bản thứ hai. Đo trên DB trước khi sửa: 82/82 dòng đều `v1`, 0 dòng inactive, `updated_at` null toàn bộ ⇒ nút "Kích hoạt" chưa từng render lần nào.
  - **Toàn bộ controller chuyển sang CQRS đúng quy ước BE** (`.claude/rules/tech/be.md` §3/§6): 3 Query + 5 Command + 8 handler, controller chỉ gán claim rồi `_mediator.Send()`, validate hình thức qua `IValidatable`/`ValidationBehavior` (thu thập **tất cả** lỗi, không dừng ở lỗi đầu), validate ngữ nghĩa (cú pháp Handlebars) trong handler vì `ValidateAsync` không nhận được `ITemplateRenderer` qua DI.
  - **Sửa = sinh phiên bản mới, không ghi đè** — bản cũ giữ lại để quay lui. **Không nhận `type`/`channel` khi sửa**: cho đổi cặp là phá chuỗi phiên bản của cả hai cặp.
  - **Chặn xoá bản đang dùng (409)** — cặp mất bản active thì dispatcher lặng lẽ rơi về chuỗi hardcode trong consumer: thông báo vẫn gửi nhưng mất nội dung tuỳ biến và không ai hay.
  - **Version tính trên cả bản đã xoá mềm**: `ux_notification_templates_type_channel_version` không lọc `is_deleted`, dùng lại số version của bản đã xoá sẽ vi phạm khoá.
  - ⚠️ **Sửa một lỗi tiềm ẩn trong `activate` cũ:** nó tắt bản cũ và bật bản mới trong **một** lần `SaveChanges`. `ux_notification_templates_active_per_key` là partial unique index không deferrable, Postgres kiểm ngay từng câu lệnh, mà thứ tự UPDATE trong một lần `SaveChanges` do EF quyết định — bật trước tắt là vi phạm khoá. Chưa ai gặp vì chưa bao giờ có bản inactive để activate. Nay tắt bản cũ được lưu ở một lần riêng, **trước**, trong cùng transaction; `revise` dùng cùng khuôn.
  - 4 action code audit mới (`TemplateCreated` Info · `TemplateRevised` / `TemplateActivated` / `TemplateDeleted` Warning), ghi trong cùng transaction với thay đổi nghiệp vụ. `activate` trước đây **không ghi audit gì cả**.
  - **FE:** nút "Tạo mẫu", dialog soạn thảo dùng chung cho tạo/sửa (khoá loại–kênh khi sửa, đếm ký tự, bóc biến `{{...}}` ngay lúc gõ), nút Sửa/Xoá trên từng dòng, hộp xác nhận xoá, làm mờ các phiên bản cũ để không nhầm với bản đang chạy.

### Removed

- **Bỏ hoàn toàn `locale` khỏi notification template — hệ thống tiếng Việt only.** Gỡ cột `notification_templates.locale`, toàn bộ 13 bản dịch `en-US` trong `NotificationTemplateCatalog`, nhánh chọn ngôn ngữ trong `NotificationDispatcher` (`ResolveLocaleAsync` + bước lùi về locale mặc định), `NotificationDispatchOptions.DefaultLocale`, và `AccountReadModel.PreferredLocale` — cột cuối này chỉ có đúng một người dùng là hàm chọn locale, và trên thực tế chưa bao giờ có giá trị (không consumer nào ghi vào, 100% dòng đang `null`). Khoá nghiệp vụ của template rút từ bộ ba `(Type × Channel × Locale)` xuống **cặp `(Type × Channel)`**; seeder và endpoint activate đổi theo.
  - **Migration `20260802113005_RemoveTemplateLocale` phải sửa tay, bản EF sinh tự động KHÔNG chạy được.** EF drop cột rồi tạo unique index `(type, channel, version)`, nhưng mỗi cặp đang có 2 dòng `version = 1` (một vi-VN, một en-US) ⇒ **39/121 dòng trùng khoá**, index dựng thất bại. Đã thêm `DELETE FROM notification_templates WHERE locale IS DISTINCT FROM 'vi-VN'` chạy **trước** `DropColumn`. Xoá cứng chứ không soft-delete: index đó không lọc `is_deleted` nên đánh dấu xoá vẫn trùng khoá. Kết quả trên DB thật: 121 → **82 dòng**, 0 cặp trùng.

### Changed

- **`GET /api/admin/notification-templates` trả `type`/`channel` dạng SỐ** thay vì tên enum (`8` thay cho `"SlaBreached"`). Tên enum là tiếng Anh, dán thẳng lên màn hình tiếng Việt thì sai — việc dịch thuộc về client. Endpoint này giờ giống mọi DTO notification khác, hết ngoại lệ. `POST .../{id}/preview` đổi theo cho đồng bộ. ⚠️ **Breaking cho FE:** đã cập nhật kèm — thêm `shared/constants/notificationLabels.ts` (`Record<Enum, string>` không dùng `Partial`, thiếu nhãn là lỗi lúc build), bỏ cột "Ngôn ngữ" và bộ lọc locale khỏi màn hình quản trị. Đáng chú ý: dialog xem trước đang so `template.channel === "Email"` bằng **chuỗi** — nay đổi sang so với enum số, nếu bỏ sót thì nút "Gửi thử" sẽ tắt vĩnh viễn ở mọi kênh.

### Added

- **Sprint 6.4 — Nhóm người nhận & gửi thông báo hàng loạt (`#1006..#1020`, 15/15 task DONE).** Trước sprint này hệ thống chỉ gửi được cho **đúng một người mỗi lệnh** (`CreateNotificationCommand` có duy nhất một `Guid UserId` — báo cho 20 người phải bấm 20 lần, và 20 lần đó không có gì nối lại thành một sự kiện). "Nhóm" chỉ là 4 chuỗi role **viết cứng tại 15 chỗ** trong code: không tạo, không sửa, không đặt tên, không nhìn thấy từ giao diện được.
  - **4 bảng mới + 1 cột.** `notification_groups` · `notification_group_members` (nhiều-nhiều người ↔ nhóm) · `notification_batches` (nội dung một lần gửi, lưu **một lần**) · `notification_batch_targets` (nhiều-nhiều lần gửi ↔ nhóm) · `notifications.batch_id`. Đây là **lần đầu tiên** NotificationService có khoá ngoại — trước đó toàn service **0 FK, 0 navigation property**.
  - **Bất đối xứng khoá ngoại là có chủ đích:** `group_id` **có** FK (cùng database, cùng transaction); `user_id` **không** FK vì trỏ sang read-model đồng bộ qua message bus — message tới *sau* thao tác dùng `user_id` sẽ làm insert vỡ rồi retry, mà nguồn sự thật ở `auth_db` service khác nên FK nội bộ không bảo vệ được gì trước sai lệch xuyên service. `batch_targets.group_id` dùng **RESTRICT chứ không CASCADE**: xoá nhóm không được xoá lịch sử đã gửi cho nhóm đó.
  - **11 endpoint mới**, trong đó `POST /api/admin/notifications/broadcast/preview` **không gửi gì** — chỉ trả số người nhận sau khi gom trùng. Phải có endpoint riêng vì cộng `memberCount` từng nhóm ở phía client là **sai** khi các nhóm giao nhau; và nó dùng **đúng đoạn logic** của lần gửi thật nên hai con số không thể lệch.
  - **Ba luật ở bước nở người nhận**, thiếu cái nào cũng hỏng theo kiểu *im lặng*: (1) **gom trùng** — người ở hai nhóm cùng được nhắm chỉ nhận một lần, hai lớp bảo vệ là `DISTINCT` ở tầng ứng dụng **và** unique index `(batch_id, user_id, channel)` ở DB; (2) **lọc người còn hoạt động** bằng JOIN read-model — chỉ đúng được nhờ bản vá 02/08/2026; (3) **tập rỗng trả 400 tường minh**, không tạo lần gửi mồ côi và không "ghi log rồi lặng lẽ trả về" — đó đúng là cách lỗi read-model ẩn mình suốt một thời gian dài.
  - **`batch_id` nullable có chủ đích.** 1.282 dòng có trước sprint này không thuộc lần gửi nào: dữ liệu cũ không mang thông tin để gom, và gom theo `(type, entity_id, giây)` là **suy đoán đã chứng minh sai** (cùng một `entity_id` có tới 50 dòng trong một giây). Thà thiếu còn hơn bịa ra lần gửi chưa từng tồn tại.
  - **Đi chệch kế hoạch có chủ đích ở NOTI4-05:** `GetActiveByRoleAsync` **giữ nguyên** cách đọc thẳng read-model thay vì định tuyến qua nhóm `Role`. Lý do kế hoạch đưa ra ("biến 15 chỗ hard-code thành dữ liệu") khi thi công hoá ra **không có thật** — nhóm `Role` gắn cứng đúng một tên role nên đổi định tuyến vẫn phải sửa code, chỉ thêm một tầng gián tiếp; đổi lại cái giá thì có thật là mọi thông báo tự động phụ thuộc vào 4 dòng seed. Chi tiết: `overall.md` §17.6.4.2.
  - **Hoãn có chủ đích:** bỏ `title`/`body`/`payload_json` khỏi `notifications` để đọc qua batch — nó bắt `GET /api/notifications` (truy vấn nóng nhất) phải JOIN thêm. Sprint này giải quyết **truy vết và gom nhóm**, chưa giải quyết **trùng lặp dung lượng**.
  - **Lỗi phát hiện trong lúc thi công:** gửi hàng loạt trả HTTP 500 vì handler gọi `UpdateAsync` trên entity vừa `AddAsync` — EF chuyển `Added` → `Modified` nên **bỏ hẳn lệnh INSERT** batch, kéo theo target vi phạm khoá ngoại. Unit test không bắt được vì mock không mô phỏng `EntityState`; chỉ lộ khi chạy trên PostgreSQL thật. Đã sửa và bổ sung test khoá **hành vi** (không đụng `UpdateAsync` lên bản ghi vừa tạo).
  - **Permission mới (4):** `notification.group_view` · `notification.group_manage` · `notification.broadcast` · `notification.batch_view`. Manager chỉ có 2 quyền đọc.
  - **Frontend:** 3 màn hình mới (Nhóm nhận thông báo · Lịch sử gửi · Gửi thông báo viết lại). Ô chọn người nhận tìm kiếm **phía server** — cố ý không dùng kiểu `pageSize: 100` rồi lọc ở client như một số dropdown cũ, vì quá 100 tài khoản là âm thầm mất người.
  - **Bộ E2E chạy lại được:** `tools/e2e/notification-groups.sh` — **104 phép kiểm** phủ xác thực/phân quyền, CRUD nhóm, thành viên, xem trước, validate, gửi thật, lịch sử, phân trang biên, xoá nhóm, **unique index ở DB**, **tạo trùng tên đồng thời**, và 10 bất biến DB. Tự dọn qua bẫy `EXIT` và **tự kiểm rò rỉ** (mục Y): chụp ảnh 7 bảng nghiệp vụ lúc bắt đầu rồi đòi trở về đúng con số đó lúc kết thúc — chính phép kiểm này bắt được lớp lỗi "script tạo dữ liệu nhưng quên dọn", vốn chỉ lộ ra ở lần chạy sau dưới dạng một phép kiểm khác hẳn bị hỏng. ⚠️ `notification_audit_logs` **cố ý KHÔNG dọn**: bảng có trigger `append_only_soft` chặn cả DELETE lẫn UPDATE — vết audit là bất biến theo thiết kế, nên mức tăng của nó được **báo ra** thay vì lờ đi. Chạy nhiều lần liên tiếp cho cùng kết quả và trả bảng nghiệp vụ về đúng nền.
  - **Kiểm chứng:** 532 test NotificationService (25 mới) · 2.782 test toàn hệ thống · FE sạch cả 4 cổng. Đầu-cuối trên docker: 2 nhóm giao nhau → mỗi người đúng 1 dòng/kênh; 2 bất biến DB = 0. Evidence: `notification-test-evidence/20260803-notification-groups/`. 📄 Kế hoạch: [`notigroup.md`](notigroup.md) · `overall.md` §17 Sprint 6.4.

### Fixed

- **Read-model tài khoản của NotificationService thiếu 8/10 người → thông báo gửi cho nhóm Admin không tới ai.** `RecipientResolver.GetActiveByRoleAsync` là nguồn duy nhất quyết định "gửi cho nhóm Manager/Admin" gồm những ai, và nó đọc bảng `account_read_models`. Bảng đó chỉ có **2 dòng** trong khi `auth_db` có **10 tài khoản**, và **không có Admin nào** — nên mọi consumer gọi `GetActiveByRoleAsync("Admin")` đều rơi vào nhánh `if (recipientIds.Count == 0) { log warning; return; }`. Nhìn từ ngoài y hệt như đã gửi thành công.
  - **Bốn lỗi độc lập, không phải một.** (1) `AuthDataSeeder` ghi thẳng `DbContext`, không đi qua handler nào nên không phát event ⇒ 6 tài khoản seed không bao giờ vào read-model. (2) `ChangeAccountRoleCommandHandler` **không phát event nào** ⇒ đổi role xong read-model giữ role cũ vĩnh viễn, thông báo theo nhóm gửi sai người. (3) `ChangeAccountStatusCommandHandler` cũng **không phát gì**; `AccountStatusChangedEvent` có sẵn trong SharedContracts và cả TicketService lẫn BatteryService đều đã viết consumer cho nó, nhưng **không nơi nào trong code chạy thật publish event đó** — 3 consumer ở 2 service khác là code chết. (4) `DeactivateMeCommandHandler` và `ReactivateVerifyCommandHandler` cũng không phát gì; riêng (4) khiến tài khoản khôi phục xong **vĩnh viễn** không nhận được thông báo nữa.
  - **Cách sửa:** thêm `AccountSyncSnapshotEvent` — ảnh chụp trạng thái hiện tại, tách hẳn khỏi nhóm event vòng đời. Phải là event mới chứ không tái dùng `AccountActivatedEvent`, vì `AccountActivatedConsumer` ghi welcome notification: dùng lại nó để đồng bộ sẽ đẻ ra welcome giả cho người đã đăng ký từ lâu mỗi lần đối soát. Phát từ 5 chỗ (đổi role, đổi trạng thái, tự vô hiệu hoá, khôi phục, seeder) qua Outbox nên atomic với dữ liệu nghiệp vụ. Consumer `AccountSnapshotSyncConsumer` upsert toàn bộ trường.
  - **Thêm `POST /api/admin/accounts/resync`** (Admin) — phát lại snapshot cho toàn bộ hoặc một tài khoản. Mỗi service một database nên NotificationService không thể tự đối soát; bắt buộc AuthService phát lại. Gọi lại bao nhiêu lần cũng được.
  - **Ngữ nghĩa `IsActive` gom về một quy tắc dùng chung** `AccountStatusEnumExtensions.IsNotifiable()`: `Active` hoặc `Locked`. `Locked` là khoá **tạm** do sai mật khẩu 5 lần, tự hết hạn — coi nó là ngừng nhận thì một Manager gõ nhầm mật khẩu sẽ không nhận được email/SMS cảnh báo SLA P1, thiệt hại thật mà không được gì. Hệ quả tốt kèm theo: cặp chuyển `Active ↔ Locked` không làm đổi giá trị này, nên `LoginCommandHandler` (đường nóng nhất hệ thống) và `UnlockAccountCommandHandler` **không cần sửa** mà read-model vẫn không lệch.
  - **Chống message về ngược thứ tự:** thêm cột `account_read_models.last_snapshot_at_utc` (nullable), consumer bỏ qua snapshot có mốc `<=` mốc đã áp. Không dùng `last_synced_at_utc` để so được vì cột đó ghi thời điểm *consume* và bị cả 3 consumer vòng đời ghi chung, nên luôn mới hơn mốc của event và sẽ loại nhầm mọi snapshot hợp lệ.
  - **Kiểm chứng trên môi trường đang chạy:** read-model từ 2 → **10 dòng**, `diff` từng dòng với `auth_db` trên cả 5 trường → **khớp 100%**. Chạy `resync` thêm 2 lần → vân tay MD5 của bảng **không đổi**. Nhóm resolve được: Admin `0 → 1`, Staff `0 → 3`, Manager+Admin `1 → 2`. Bắn một `IotDeviceWentOfflineEvent` thật qua RabbitMQ: trước đây chỉ Manager nhận (2 dòng), nay **Manager + Admin đều nhận, đủ 4 dòng**. Đổi role Staff→Manager qua API → read-model đổi theo; `Locked` → `is_active` vẫn `true`; `Suspended` → `false`; `Active` → `true`. Đã trả tài khoản thử và dữ liệu thử về đúng nguyên trạng (`auth_db` không đổi, `notifications` vẫn 1.282 dòng).
  - 22 test mới; toàn bộ **2.762** unit test của 6 service + shared xanh.
  - 📄 Kế hoạch nhóm người nhận & quan hệ DB cho gửi hàng loạt: [`notigroup.md`](notigroup.md).
- **Thứ tự sắp xếp không toàn phần → một dòng có thể lọt qua 2 trang hoặc biến mất.** 25/38 truy vấn có phân trang chỉ `ORDER BY` theo cột **không duy nhất** (`CreatedAt`, `OccurredAt`, `DetectedAt`, `StartedAt`…). Khi hai dòng bằng nhau ở khoá đó, Postgres được phép trả thứ tự khác nhau giữa các lần chạy — người dùng bấm sang trang sau có thể thấy lại dòng cũ, hoặc mất hẳn một dòng. Nay mọi truy vấn kết thúc bằng `.ThenBy(Id)` (saga dùng `CorrelationId` vì không kế thừa `AuditableEntity`).
  - **Không phải lý thuyết:** `/api/admin/sagas/alert-ticket` có **3 mốc `StartedAt` trùng** trong dữ liệu seed (6 dòng dính trùng). Trước fix, cùng một truy vấn `pageNumber=1&pageSize=3` chạy 2 lần trả về **hai tập dòng khác nhau**. Sau fix: duyệt 37 trang thu đủ 109 dòng, **0 trùng, 0 sót**, thứ tự ổn định giữa các lần chạy.
  - Ngoại lệ có chủ đích: `GET /api/ambient/readings/history` không thêm gì — `AmbientReading` là hypertable TimescaleDB không có `Id`, khoá chính `(Time, SiteId)` mà query đã lọc cứng một `SiteId` nên `Time` tự nó đã duy nhất. Đã ghi chú tại chỗ để không ai "sửa" nhầm.
- **Tràn số nguyên khi phân trang → HTTP 500 trên toàn hệ thống.** `(pageNumber - 1) * pageSize` chạy bằng `int`: `?pageNumber=300000000&pageSize=10` quấn thành `-1294967306`, Postgres ném `2201X: OFFSET must not be negative`. Tái hiện được trên **7 endpoint đang chạy** (`/api/admin/accounts`, `/api/admin/tickets`, `/api/battery-types`, `/api/sites`, `/api/knowledge-base`, `/api/notifications`, `/api/blog`). Nay ép `long` trước khi nhân trong `QueryableExtensions.ToPagedEntityListAsync`, và trang vượt quá dữ liệu trả `200` + `items: []` thay vì chạm DB. Vì mọi endpoint đã được gom về helper này (xem mục Changed), một lần sửa vá hết.

### Changed

- **Gom toàn bộ phân trang về helper dùng chung `SharedInfrastructure.Extensions.QueryableExtensions.ToPagedEntityListAsync`.** Trước đây **38 điểm** trên 6 service (AuthService, BatteryService, TicketService, NotificationService, AuditAggregatorService, FileStorageService) tự viết `.Skip((page-1)*size).Take(size)` rồi tự dựng `PaginationResponse` — helper shared tồn tại nhưng **0 nơi dùng**. Hệ quả: lỗi tràn int ở trên phải vá 38 chỗ, và mỗi lần dựng `PaginationResponse` bằng tay là một cơ hội gán nhầm `TotalItems` / quên `PageNumber` đã kẹp. Nay không còn `.Skip()` thủ công nào cho truy vấn DB.
  - Thêm `PaginationResponseExtensions.Map/WithItems` (SharedContracts) cho handler phải map ở client — mapper là method call nên EF không dịch được sang SQL (`Mapper.ToDto(x)`), hoặc phải truy vấn phụ để làm giàu dữ liệu (reaction của chat, chat chưa đọc của ticket).
  - `IAlertTicketSagaQueryService.QueryAsync` đổi kiểu trả về từ tuple `(Items, Total)` sang `PaginationResponse<AlertTicketSagaDTO>` — đây là hợp đồng **nội bộ TicketService**, không phải API công khai.
  - **Không đổi hợp đồng HTTP:** đã so từng byte 51 response trước/sau refactor — trùng khớp tuyệt đối (3 file chênh lệch được chứng minh là do chính script test đăng nhập ghi `lastLoginAt` + sinh audit log, tái hiện y hệt khi chạy 2 lần trên cùng một bản code). Các endpoint tự kẹp `pageSize` riêng (ambient 1000, environmental-incidents 200, saga 200) giữ nguyên trần cũ vì kẹp trước khi gọi helper.
- `GET /api/admin/notification-templates`: **thêm phân trang**. Nhận thêm `pageNumber` (mặc định `1`) và `pageSize` (mặc định `10`, trần `100` — kẹp bằng `PaginationRequest` dùng chung). Sắp xếp bổ sung `ThenBy(id)` làm chốt chặn cho thứ tự toàn phần, tránh một dòng lọt qua 2 trang hoặc biến mất khi Postgres đổi thứ tự giữa các lần chạy. Trang vượt quá dữ liệu trả `200` với `items: []` (không phải `404`), và `pageNumber` giữ nguyên giá trị client gửi. ⚠️ **Breaking cho FE:** `data` đổi từ `object[]` thành `PaginationResponse<NotificationTemplateDto>` — đọc `data.items` thay cho `data`. Lý do: seed đủ 32 type × kênh × locale đã cho 121 dòng, trả hết một lượt vừa nặng vừa không đọc nổi.
- `POST /api/knowledge-base/references`: nới guard trạng thái — state `Resolved` cho phép gán 2 type after-resolve (`GeneratedAfterResolve`, `ProvidedToCustomer`); chặn bài `isInternalOnly` với type `ProvidedToCustomer` (`422`); chuẩn hóa status code — state lock đổi `403` → **`409`**, `403` chỉ còn cho lỗi quyền. ⚠️ **Breaking cho FE** nào đang bắt riêng mã `403` của state lock.

## [1.6.0] — 2026-06-18 (Sprint additional-auth)

> Sprint **AuthService Security Hardening** — 76/90 task `#AUTH-01..90` (`#349..#438`) merged qua PR #446 + #441. 14 task defer/skip có ghi rõ ở §69 `overall.md`. Reference: `issue-authservice.md` 88 issue audit gốc.

### Security — Phase A P0 (9 task)

- **#AUTH-01** (`#349`) Hash refresh token DB (SHA-256) — migration `HashRefreshTokens` + backfill, sửa `AuthTokenIssuer`/`RefreshTokenCommandHandler`.
- **#AUTH-02** (`#350`) 2FA Disable require verify TOTP/password.
- **#AUTH-03** (`#351`) Google OAuth `state` CSRF validation (Redis key TTL 10 phút).
- **#AUTH-04** (`#352`) JWT `ValidateToken` enable issuer/audience/lifetime.
- **#AUTH-06** (`#354`) Reset token single-use enforcement.
- **#AUTH-07** (`#355`) Migration `FixEmailUniqueIndexFilter` — unique index filter `is_deleted = false`.
- **#AUTH-08** (`#356`) Logout invalidate pending 2FA challenge token.
- **#AUTH-09** (`#357`) OTP/Reset/2FA constant-time compare — `CryptographicOperations.FixedTimeEquals` ở 4 handler.
- **#AUTH-10** (`#358`) HTML sanitize `FullName`/`PendingEmail` ở email template.

### Security — Phase B P1 (7 task)

- **#AUTH-11** (`#359`) Implement `IJwtHelper.IsTokenValid()` + blacklist check.
- **#AUTH-12** (`#360`) RefreshToken cross-check IP/UA (device binding).
- **#AUTH-13** (`#361`) ForgotPassword per-email rate limit (Redis `otp_attempts:{emailHash}`).
- **#AUTH-15** (`#363`) JWT permission claims revoke realtime — subscribe `PermissionsChangedEvent` + `jti` blacklist Redis.
- **#AUTH-16** (`#364`) `PermissionResolver` cache với event invalidation.
- **#AUTH-17** (`#365`) Login enumeration attack — uniform audit log + timing jitter `Task.Delay(100,200)`.
- **#AUTH-54** (`#402`) Token Revocation List (TRL) — `POST /api/auth/revoke` + Redis `revoked_jti:{jti}`.

### Logic/Edge fixes — Phase C (11 task)

- **#AUTH-18..28** (`#366..#376`) Account Status semantics (Locked vs Inactive), failed login OTP reset rule, Google OAuth email mismatch policy, Google token timeout/retry, 2FA lazy re-encrypt recovery, ChangePassword check OldPassword != NewPassword, ChangeEmail Redis lock, Register PG 23505 unique violation parse, AcceptInvite validate expiry, VerifyOtp off-by-one fix, RefreshToken rotation `OriginalIssuedAt`.

### Audit + GDPR (2 task)

- **#AUTH-29** (`#377`) Migration `AuditLogAppendOnlyTrigger` — PG trigger `BEFORE UPDATE/DELETE ON auth_audit_logs RAISE EXCEPTION`. Sprint audit Phase 1 sẽ upgrade lên soft mode.
- **#AUTH-30** (`#378`) DeleteAccount cascade + anonymize PII (soft-delete + 90 ngày retention window, hard-delete qua `#AUTH-42`).

### Operational hardening — Phase D (15 task)

- **#AUTH-31** (`#379`) `PendingEmailCleanupBackgroundService` daily 02:00 UTC.
- **#AUTH-32** (`#380`) `RefreshTokenExpirationDays` read từ `JwtSettings`.
- **#AUTH-35** (`#383`) `GenericRepository.GetAllAsync()` default `AsNoTracking()` + opt-in tracking overload.
- **#AUTH-36** (`#384`) MediatR `ValidationBehavior` chạy TRƯỚC handler.
- **#AUTH-37** (`#385`) `OutboxRelayBackgroundService` honor `CancellationToken` + flush trước shutdown.
- **#AUTH-38** (`#386`) Inject `ISystemClock`/`SystemClock` toàn AuthService.
- **#AUTH-39** (`#387`) Email/PhoneNumber normalization (trim + lowercase + E.164).
- **#AUTH-40** (`#388`) Token introspection endpoint `POST /api/auth/introspect` (RFC 7662).
- **#AUTH-41** (`#389`) Concurrent session limit per account (`MaxConcurrentSessionsPerAccount=5`).
- **#AUTH-42** (`#390`) `AccountHardDeleteBackgroundService` daily 03:00 UTC drop `is_deleted=true AND deleted_at < now - 90d`.
- **#AUTH-43** (`#391`) `LockoutReconcileBackgroundService` mỗi 5 phút auto-unlock.
- **#AUTH-44** (`#392`) Session Device ID tracking + per-device revoke.
- **#AUTH-45** (`#393`) Backup code recovery rate limit (5 attempts/15min).
- **#AUTH-46** (`#394`) Email change rate limit (`PolicyAuthOtp` 3/min).
- **#AUTH-49** (`#397`) `OtpCleanupBackgroundService` daily clear expired OTP.

### Missing features (8 task)

- **#AUTH-50** (`#398`) Account reactivation sau soft-delete (90d window, verify email OTP).
- **#AUTH-52** (`#400`) Suspicious login alert — publish `SuspiciousLoginDetectedEvent` → email.
- **#AUTH-53** (`#401`) Password strength policy configurable (`PasswordPolicy:{...}`).
- **#AUTH-55** (`#403`) Admin forced logout endpoint.
- **#AUTH-57** (`#405`) Admin account unlock endpoint.
- **#AUTH-58** (`#406`) SMS OTP fallback cho 2FA — integration `SendSmsCommand` qua SmsService.
- **#AUTH-59** (`#407`) JWT `kid` header + key rotation (current + previous).
- **#AUTH-62** (`#410`) GDPR data export endpoint `GET /api/accounts/me/export`.

### Code quality + Ops (14 task)

- **#AUTH-60** (`#408`) Health checks `/health`/`/ready`/`/live` (DB + Redis + RabbitMQ).
- **#AUTH-65** (`#413`) Optimistic concurrency Account (`RowVersion` shadow property xmin) + retry.
- **#AUTH-66** (`#414`) `IValidateOptions<JwtSettings>` ValidateOnStart + `[Required]` annotation.
- **#AUTH-67** (`#415`) Idempotency middleware dedupe verification + integration test.
- **#AUTH-68** (`#416`) `LoginCommandHandler` set `LastLoginIp`/`LastLoginAt` sau pass 2FA.
- **#AUTH-69** (`#417`) Migration `MakeAccountRoleIdNullable` — `Account.RoleId` → `Guid?`.
- **#AUTH-70** (`#418`) `PasswordHasher` Singleton → Scoped (+ AUTH-83 combined).
- **#AUTH-72** (`#420`) `IJwtHelper.IsTokenValid()` implementation (giữ thay xoá — spec literal cho phép).
- **#AUTH-74** (`#422`) `OtpHelper.GenerateOtp` dùng `RandomNumberGenerator.GetInt32`.
- **#AUTH-75** (`#423`) Migration `AddAccountEmailIsDeletedIndex` — composite index `(email, is_deleted)`.
- **#AUTH-76** (`#424`) `GlobalExceptionMiddleware` mask stacktrace + PII redact (Production env).
- **#AUTH-77** (`#425`) `CorrelationIdMiddleware` end-to-end — propagate qua MassTransit header.
- **#AUTH-78** (`#426`) Prometheus metric: `auth_login_total{result}`, `auth_2fa_challenge_total{result}`, `auth_otp_usage_total`.
- **#AUTH-79** (`#427`) Refresh token reuse detection — publish `RefreshTokenReuseDetectedEvent`.
- **#AUTH-80** (`#428`) ClockSkew unify middleware + helper (`TimeSpan.Zero` cả 2 chỗ).
- **#AUTH-82** (`#430`) Lockout reconcile grace period — implicit 5 phút (AUTH-43 cycle).
- **#AUTH-83** (`#431`) `PasswordHasher` Scoped — combined với AUTH-70.

### Test gap — Phase F (7 task)

- **#AUTH-84** (`#432`) `EmailChangeCommandHandlerTests.cs` — 11 Fact tests (happy + race + OTP fail).
- **#AUTH-85** (`#433`) `AcceptInviteCommandHandlerTests.cs`.
- **#AUTH-86** (`#434`) `GoogleCallbackCommandHandlerTests.cs` — happy + state mismatch + email mismatch + timeout.
- **#AUTH-87** (`#435`) ChangePassword test — verify revoke sessions (direct via `RefreshToken.Status` + `ITokenRevocationStore`).
- **#AUTH-88** (`#436`) Outbox publish loop integration test — TestContainers Postgres + InMemory MassTransit.
- **#AUTH-89** (`#437`) `PermissionResolver` perf test — 1000 concurrent call, p99 < 50ms.
- **#AUTH-90** (`#438`) `ChangePasswordCommandHandler` dedicated test — old pwd check + revoke + audit log.

### Migrations

5 migration mới (rollback test PASS):

1. `20260618023820_HashRefreshTokens` (`#AUTH-01`)
2. `20260618022148_FixEmailUniqueIndexFilter` (`#AUTH-07`)
3. `20260618034223_AuditLogAppendOnlyTrigger` (`#AUTH-29`)
4. `20260618065708_AddAccountEmailIsDeletedIndex` (`#AUTH-75`)
5. `20260618071225_MakeAccountRoleIdNullable` (`#AUTH-69`)
6. `20260618032425_AddOriginalIssuedAtToRefreshToken` (`#AUTH-28`)

### Background services mới

4 hosted service đều honor `CancellationToken` graceful shutdown:

1. `PendingEmailCleanupBackgroundService` daily 02:00 UTC (`#AUTH-31`)
2. `LockoutReconcileBackgroundService` mỗi 5 phút (`#AUTH-43`)
3. `OtpCleanupBackgroundService` daily (`#AUTH-49`)
4. `AccountHardDeleteBackgroundService` daily 03:00 UTC (`#AUTH-42`)

### Followup P1+P2+P3 — 2026-06-19 (sau merge PR #446)

#### P1
- **#AUTH-14** (`#362`) Giảm OTP TTL 10 → 5 phút ở 4 handler:
  - `ForgotPasswordCommandHandler.cs:20`
  - `ResendResetOtpCommandHandler.cs:17`
  - `ReactivateRequestCommandHandler.cs:18`
  - `ChangeEmailCommandHandler.cs:20`
  - `EmailReserveTtl` Redis lock cũng align 5p (`ChangeEmailCommandHandler.cs:25`)
  - 4 test assertion updated (`PasswordResetCommandHandlerTests`, `ResendResetOtpCommandHandlerTests`, `EmailChangeCommandHandlerTests`)
  - 3 doc comment cập nhật TTL (PendingEmailCleanup, ConfirmEmailChange, AccountsController)
- **#AUTH-81** (`#429`) Async/await audit + Roslyn analyzer:
  - Audit kết quả: **0 violation** trong toàn AuthService `src/` + `tests/` cho 5 pattern (`.Result`, `.Wait()`, `GetAwaiter().GetResult()`, `async void`, `throw ex;`). Code đã clean.
  - Thêm `services/AuthService/Directory.Build.props` — `Microsoft.VisualStudio.Threading.Analyzers` v17.10.48 + `<EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>` cho tất cả 6 project con (Api/App/Domain/Infra + 2 test project).
  - Thêm 4 rule severity = `error` trong `.editorconfig` root: VSTHRD002 (sync wait), VSTHRD100 (async void), VSTHRD110 (unobserved task), CA2200 (throw ex preserve stack).
  - Silence VSTHRD200 (Async suffix) — tương thích ASP.NET Controller convention.
  - Build verify: 0 error, 5 warning pre-existing (không liên quan), 546/546 unit test PASS.

#### P2
- **#AUTH-33** (`#381`) LastLoginIp semantic — remove update LastLoginAt/LastLoginIp trong `RefreshTokenCommandHandler.cs:171-173`. Refresh token ≠ login event; field "last login" giờ semantic chuẩn (Password+2FA / Google / Invite accept). Audit history giữ ở LoginAttempt + audit_logs độc lập.
- **#AUTH-34** (`#382`) Concurrency retry wire — `ChangePasswordCommandHandler` + `ChangeAccountRoleCommandHandler` wrap business logic + SaveChanges trong `ConcurrencyRetryHelper.ExecuteAsync` (3 attempts, reload entity giữa retries). Thêm `IGenericRepository.ReloadAsync` API trong SharedKernels. `auditPublished` flag tránh double-audit khi retry. Returns 409 nếu account becomes invalid sau reload. 2 unit test mới (retry-success + invalid-after-reload).

#### P3
- **#AUTH-47** (`#395`) **Account Merge/Consolidation** — Implement đầy đủ.
  - Entity `AccountMergeLog` (immutable audit + JSON snapshot secondary + JSON conflict resolution), Account thêm fields `MergedIntoId`/`MergedAt`, migration `AddAccountMergeLog`.
  - Endpoint `POST /api/admin/accounts/{primaryId}/merge` (Admin-only, body: `secondaryAccountId` + `reason`).
  - Logic: revoke active RT của secondary → transfer GoogleId/Profile/StaffProfile sang primary CHỈ KHI primary chưa có (primary thắng conflict) → tombstone secondary (soft-delete + anonymize email `merged-{guid}@anonymized.local` tránh unique index violation) → insert AccountMergeLog → publish `AccountMerged` + meta-audit `AccountDeleted` → post-commit Redis TRL bulk revoke.
  - Audit action mới: `AccountMerged=130`, `AccountMergeRejected=131`.

- **#AUTH-48** (`#396`) **Trusted Device whitelist** — Implement đầy đủ.
  - Entity `TrustedDevice` (DeviceFingerprintHash SHA-256 + IpPrefix /24 IPv4 hoặc /64 IPv6 + Label + ExpiresAt + UsageCount + RevokedAt), migration `AddTrustedDevice`. Composite unique index `(AccountId, DeviceFingerprintHash)` filter active.
  - Helper `TrustedDeviceFingerprintHelper` (compute fingerprint, ipPrefix, auto-gen label "Chrome on macOS").
  - `Verify2FALoginCommand` thêm `TrustDevice: bool` + `TrustDeviceLabel: string?`. TTL 30 ngày. KHÔNG cấp lúc verify qua backup code path (emergency code không trust device).
  - `LoginCommandHandler` match active trusted device → skip 2FA challenge, issue tokens trực tiếp, audit `LoginWithTrustedDevice`. Metric `auth_login_total{result=success_trusted_device}` + `auth_2fa_challenge_total{result=skipped_trusted_device}`.
  - 3 endpoint user: `GET/DELETE/{id}/DELETE-all /api/accounts/me/trusted-devices`.
  - Auto-revoke khi `ChangePassword` + `Disable2FA` (qua `RevokeAllTrustedDevicesCommand` injected qua IMediator).
  - Audit action mới: `TrustedDeviceAdded=110`, `TrustedDeviceRevoked=111`, `TrustedDeviceAllRevoked=112`, `LoginWithTrustedDevice=113`.

- **#AUTH-51** (`#399`) **Cross-Device 2FA Confirmation** — Implement đầy đủ.
  - Redis-backed store `ITwoFactorCrossDeviceConfirmStore` (key `2fa:confirm-token:{token}`, TTL 10 phút, single-use).
  - 2 endpoint: `POST /api/auth/2fa/cross-device-confirm/request` (Device A initiate — sinh secret + 32-byte hex token, publish `SendTwoFactorCrossDeviceConfirmEmailEvent` qua Outbox, trả về OtpAuth URI + secret cho FE hiển thị QR), `POST /api/auth/2fa/cross-device-confirm` (Device B verify TOTP + enable 2FA, anti-stolen-link check: token chỉ confirm được bởi cùng AccountId).
  - SharedContract event `SendTwoFactorCrossDeviceConfirmEmailEvent` (ToEmail/FullName/ConfirmUrl/ExpiresInMinutes).
  - Audit action mới: `TwoFactorSetupCrossDeviceRequested=120`, `TwoFactorSetupCrossDeviceConfirmed=121`, `TwoFactorSetupCrossDeviceExpired=122`.

### Schema changes — followup

3 migration mới:
1. `AddTrustedDevice` — bảng `trusted_devices` + 2 index.
2. `AddAccountMergeLog` — bảng `account_merge_logs` + 2 column mới `accounts.merged_into_id`/`merged_at`.

### Deferred / Skipped (5 task còn lại — chốt final 2026-06-19)

- **P0 pending:** `#AUTH-05` CORS whitelist (Leader chốt domain trước go-live).
- **P1 defer:** `#AUTH-64` KYC recovery (scope lớn — sprint riêng, mitigate bằng admin-side reset).
- **P2 skip/defer:** `#AUTH-63` Multi-tenancy (single-tenant scope), `#AUTH-71` HTTPS redirect Docker (cloud-native TLS termination ở reverse proxy — runbook chưa viết).
- **Huỷ bỏ hoàn toàn (cancelled 2026-06-23):** `#AUTH-61` API versioning + `#AUTH-73` Error code catalog — xoá task definition (overall.md) + issue GitHub #409/#421. Không nằm trong scope capstone (single-version + FE parse theo HTTP status đủ).
- **P3 defer:** `#AUTH-56` Notification preferences (cross-service impact, hard-code default đủ cho capstone).

### Deviations (đã document trong overall.md §69)

- `#AUTH-21` Manual retry thay Polly (tránh thêm dependency cho 1 endpoint Google OAuth).
- `#AUTH-30` Hard-delete defer qua `#AUTH-42` (giữ 90 ngày cho AUTH-50 reactivation window).
- `#AUTH-82` Grace period implicit 5 phút (qua AUTH-43 cycle), không strict 1s.
- `#AUTH-87` Revoke trực tiếp qua `RefreshToken.Status` + `ITokenRevocationStore`, không qua Mediator command.
- `#AUTH-88` InMemory MassTransit thay RabbitMQ TestContainer (Outbox behavior vẫn end-to-end verified).

## [1.5.0] — 2026-07-26 (Sprint 5B)

### Added — Alert–Ticket Saga (BR P0 release gate)

- **#236** Saga contracts (`SharedContracts/Saga/AlertTicket/*`): 7 records + `BatteryAnomalyDetectedV2Event`.
- **#236** TicketService migration `AddAlertTicketSagaFoundation` — bảng `alert_ticket_saga_states`,
  unique filtered index `ux_tickets_origin_alert_id`, partial unique guard
  `ux_tickets_active_auto_per_asset_category`.
- **#236** BatteryService migration `AddAlertTicketLinkIndex` — non-unique filtered
  index `ix_alerts_ticket_id_filtered`.
- **#235** TicketService migration `AddQuartzPersistenceSchema` — 11 bảng `qrtz_*`
  theo official Quartz.NET PostgreSQL DDL.
- **#237** `AlertTicketSagaStateMachine` (MassTransit) — state machine
  Initial→TicketRequested→TicketProvisioned→AlertLinkRequested→Completed/Failed,
  PostgreSQL `xmin` optimistic concurrency, persistent Quartz timeout, bounded retry.
- **#238** Saga participants: `CreateTicketFromAlertConsumer` (TicketService),
  `LinkAlertToTicketConsumer` (BatteryService).
- **#238** `BatteryAlertEscalationRequestedEvent` tách khỏi `BatteryAnomalyDetectedEvent`.
- **#238** NotificationService consumers: `BatteryAlertEscalationRequestedConsumer`,
  `AlertTicketSagaFailedConsumer` + email templates + 2 enum value
  (`BatteryAlertEscalationPending=16`, `AlertTicketSagaFailed=17`).
- **#238** Feature flags `AlertTicketDispatchEnabled` + `AlertTicketSagaEnabled` cho cutover.
- **#239** Admin endpoints `/api/v1/admin/sagas/alert-ticket{,/{alertId}{,/reprocess}}`
  với `Idempotency-Key` requirement cho reprocess.
- **#239** `/api/ticket/health/saga` endpoint.
- **#239** 8 Prometheus metrics: `saga_alert_ticket_started/completed/failed/active/duration/timeout/redelivery/reprocessed`.
- **#239** 3 runbooks (`docs/runbooks/{08-saga-failed,09-saga-stuck,10-saga-duplicate-canonical}.md`).
- **#239** ADR-018: Orchestrated Alert–Ticket Saga.
- **#241** AuthService data migration `SeedSagaPermissions` — seed
  `ticket.saga.view` (Admin + Manager) + `ticket.saga.reprocess` (Admin only).
- **#241** `PermissionsChangedEvent` for cross-service cache invalidation.

### Added — Messaging hardening (#235)

- Tách `IIntegrationEventOutboxWriter` (in-transaction write) khỏi
  `IIntegrationEventTransport` (post-commit publish) — DI split.
- NuGet packages: `MassTransit.EntityFrameworkCore` 8.4.1, `MassTransit.Quartz` 8.4.1,
  `Quartz.AspNetCore` 3.14.0, `Quartz.Extensions.Hosting` 3.14.0, `Quartz.Serialization.Json` 3.14.0.

### Changed

- **#233** Battery scope cleanup — Energy/CO2 analytics loại bỏ permanent. ADR-017 merged.
- **#233** Pre-commit hook `energy-co2-scope-guard` thêm vào `.pre-commit-config.yaml`.
- **#234** BatteryService entity `Site` bỏ field `CapacityKw` + DTO + validation
  + seed + Mapper + Controller XML docs. Migration `RemoveSiteCapacityKw` (Up/Down + rollback).
- **#238** `BatteryAnomalyDetectedConsumer` (TicketService) mark `[Obsolete]` —
  Saga state machine giờ handle anomaly events.

### Deprecated

- `BatteryAnomalyDetectedConsumer` (TicketService) — sẽ remove ở Sprint 6 sau khi
  Saga stable, không có rollback nào trong window cutover.

### Migration order (Sprint 5B — bắt buộc tuần tự)

1. BatteryService `RemoveSiteCapacityKw` (`#234`)
2. BatteryService + TicketService `AddDurableMessagingFoundation` (deferred — pending)
3. TicketService `AddQuartzPersistenceSchema` (`#235`)
4. Preflight data cleanup (runbook `10-saga-duplicate-canonical.md`)
5. TicketService `AddAlertTicketSagaFoundation` (`#236`)
6. BatteryService `AddAlertTicketLinkIndex` (`#236`)
7. AuthService `SeedSagaPermissions` (`#241`) — khác DB, có thể chạy song song.

## [1.4.0] — 2026-07-19 (Sprint 5)

- Ticket SLA timer + escalation flow (`#150`/`#151`).

## [1.0.0] — 2026-05-24 (Sprint 1)

- Khởi tạo monorepo + base services.

[1.6.0]: https://github.com/GSU26SE55/backend/releases/tag/v1.6.0
[1.5.0]: https://github.com/GSU26SE55/backend/releases/tag/v1.5.0
[1.4.0]: https://github.com/GSU26SE55/backend/releases/tag/v1.4.0
[1.0.0]: https://github.com/GSU26SE55/backend/releases/tag/v1.0.0
