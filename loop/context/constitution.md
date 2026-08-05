# Hiến pháp dự án — backend GSU26SE55

> Tầng T0. Nạp vào MỌI lần gọi agent, mọi vòng lặp.

---

## 1. Dự án này làm gì

Nền tảng giám sát và bảo trì **pin lithium-ion cho hệ thống điện mặt trời**. Thiết bị IoT
(ESP32) gắn tại site đọc BMS/cảm biến rồi đẩy telemetry lên backend; backend phát hiện bất
thường, gọi AI dự đoán SOH, tự mở ticket bảo trì theo SLA, và báo cho Customer/Staff/Manager
qua push, email, SMS, realtime.

Bốn vai: **Admin · Manager · Staff · Customer**. Backend là 9 microservice .NET
(Auth, Battery, Ticket, Notification, Email, Sms, FileStorage, AuditAggregator, ApiGateway).

**Hậu quả khi sai:** pin lithium-ion hỏng có thể **cháy nổ**. Bỏ sót cảnh báo quá nhiệt /
rò khí / rò nước là rủi ro an toàn tính mạng, không phải phiền toái. Kèm theo: lộ dữ liệu
khách hàng (đa tenant), sai audit trail (yêu cầu compliance), và SLA breach có ràng buộc
hợp đồng B2B. **Hãy thận trọng ở mức cao nhất.**

---

## 2. Bất biến nghiệp vụ

| # | Bất biến | Vì sao | Chỗ cưỡng chế |
|---|---|---|---|
| 1 | Mọi query đọc dữ liệu PHẢI lọc `.Where(x => !x.IsDeleted)` | Dự án **KHÔNG** cấu hình global query filter (`HasQueryFilter`). Quên lọc = trả về bản ghi đã xoá mềm | Không có nơi nào cưỡng chế tự động — chỉ có code review. Đây là lỗi hay gặp nhất |
| 2 | Customer chỉ thấy tài nguyên thuộc tenant của chính mình | Đa tenant. Rò rỉ chéo tenant là lỗ hổng bảo mật, không phải bug hiển thị | Handler phải lọc theo `CustomerId` lấy từ JWT, KHÔNG lấy từ request body |
| 3 | `Priority` của ticket tính từ ma trận `ImpactScope × UrgencyLevel` lúc Manager triage, sau đó **CỐ ĐỊNH** cả vòng đời | SLA breach → escalate thêm *nhân lực/cấp bậc*, KHÔNG đổi deadline. Đổi priority làm hỏng audit trail và skew báo cáo SLA | `overall.md §2.4bis`. Ngoại lệ duy nhất: Manager override vì lý do an toàn, bắt buộc ghi `PriorityOverrideReason` vào `TicketActivity` |
| 4 | Enum bắt đầu từ `1`, không phải `0` | `0` là giá trị mặc định của int → không phân biệt được "chưa gán" với giá trị hợp lệ đầu tiên | Quy ước toàn repo. Ngoại lệ đã biết: `AccountStatusEnum.PendingVerification = 0` (cố ý) |
| 5 | Mọi entity PHẢI kế thừa `AuditableEntity` | Cho Id, CreatedAt, CreatedBy, UpdatedAt, IsDeleted, DeletedAt + interceptor soft-delete | `make ci-rules` có luật kiểm |
| 6 | Sensor reading có **3 nguồn mỗi pin mỗi tick** (primary BMS / redundant INA226 / external-temp DS18B20). Mọi truy vấn TÍNH TOÁN phải lọc `SensorSourceCode == "primary" \|\| null` | Không lọc = đếm gấp 3, và dính `temp=0`/`SOC=0` của bản ghi mirror ở chế độ real | Query handler. Bẫy này đã trả giá thật |
| 7 | Service KHÔNG có Outbox: publish event **SAU** `CommitTransactionAsync()`. Service CÓ Outbox: ghi outbox **TRƯỚC** `SaveChangesAsync()` (cùng transaction) | Publish trước khi commit → consumer đọc trạng thái chưa tồn tại. Publish ngoài transaction → mất event khi rollback | `.claude/rules/tech/be.md §11` |
| 8 | Ghi chú nội bộ (`IsInternal = true`) Customer KHÔNG được thấy — cả REST lẫn SignalR | Hub có 2 group tách biệt `ticket:{id}:public` và `ticket:{id}:internal` | `docs/chat/permission-matrix.md` |

---

## 3. Điều TUYỆT ĐỐI KHÔNG làm

- **KHÔNG** `await` `GetAllAsync()`, `UpdateAsync()`, `DeleteAsync()`. `GetAllAsync()` là
  **SYNC** trả `IQueryable` trực tiếp; `UpdateAsync`/`DeleteAsync` trả **void**. Tên có hậu tố
  "Async" là di sản từ `SharedKernel` và **không được đổi**. Chỉ `AddAsync` mới có await.
  Vì: `await` một void method không biên dịch được, còn `await` `GetAllAsync()` làm materialise
  toàn bảng vào RAM.
- **KHÔNG** inject `DbContext` trực tiếp vào handler. Chỉ inject `IUnitOfWork`.
  Vì: phá ranh giới transaction và làm handler không test được bằng mock.
- **KHÔNG** đặt logic nghiệp vụ trong Controller. Controller chỉ `_mediator.Send()`.
- **KHÔNG** dùng AutoMapper. Map tay trong handler. Vì: dự án đã chọn map tay, trộn hai
  kiểu làm code không đọc được.
- **KHÔNG** sửa test đang đỏ để nó xanh. Xem §6.
- **KHÔNG** đổi schema DB mà không kèm migration + `Down()` chạy được.
- **KHÔNG** commit. Người dùng tự commit sau khi review. Không chạy `git commit`,
  `git push`, `git checkout`, `git reset`, `git stash` — cây làm việc đang có thay đổi
  chưa commit của người dùng, mọi lệnh đó đều có thể xoá mất chúng.
- **KHÔNG** sửa file ngoài phạm vi issue đang làm. Không refactor code lân cận, không
  đổi tên biến, không format lại file, không xoá dead code ngoài scope.
- **KHÔNG** chạy `dotnet ef migrations remove --no-build` — nó làm hỏng snapshot, khiến
  migration kế tiếp sinh `CreateTable` cho bảng đã tồn tại.
- **KHÔNG** bọc `dotnet test` bằng lệnh `timeout` — macOS không có `timeout`, lệnh sẽ
  nuốt kết quả và trông như thiếu Docker.

---

## 4. Ranh giới kiến trúc

```
Api → Application, Infrastructure
Application → Domain (CHỈ Domain)
Domain → KHÔNG phụ thuộc gì cả
Infrastructure → Application, Domain
```

```
ServiceName/
├── ServiceName.Api/            Controller, Program.cs
├── ServiceName.Application/    CQRS (Command/Query + Handler), DTO, Interface, Validation
├── ServiceName.Domain/         Entity, Enum — ZERO dependency
└── ServiceName.Infrastructure/ DbContext, Repository, Consumer, DI, BackgroundJob
```

Thư viện dùng chung: `shared/src/SharedKernel` (base entity, IGenericRepository, IUnitOfWork) ·
`SharedContracts` (DTO, integration event) · `SharedInfrastructure` (middleware, behavior,
GenericRepository, cache, bus).

**Sửa ở đâu là đúng chỗ:** quy tắc nghiệp vụ mới → CommandHandler/QueryHandler trong
`Application`. Không nhét vào Controller, không nhét vào Repository.

---

## 5. Dữ liệu và thời gian

- **Thời gian lưu UTC.** `DateTime.UtcNow`, không `DateTime.Now`. Timezone chỉ áp ở tầng
  hiển thị / preference người dùng.
- **Định danh:** `Guid Id`. KHÔNG dùng int auto-increment.
- **DTO chính đổi Guid → string** (`.ToString()`). Không giữ `Guid` trong DTO trả ra ngoài.
- **Time-series** (`sensor_readings`) nằm trong TimescaleDB hypertable. Phân trang theo
  **cursor (timestamp)**, KHÔNG offset — bảng có hàng triệu dòng, offset gây full scan.
  `totalCount` luôn `null` cho time-series; FE chỉ dùng `hasMore`.
- **Cột thời gian của `SensorReading` tên là `Time`**, không phải `Timestamp`. Nhầm là
  compile lỗi hoặc sort sai.

---

## 6. Chuẩn về test

- Test là **thước đo, không phải mã sản phẩm**. Nếu test đỏ: sửa **code sản phẩm**.
- **Được phép THÊM** test mới (gần như mọi issue trong milestone này đều yêu cầu).
- **KHÔNG được làm yếu** test đã có: không xoá assertion, không nới ngưỡng, không đổi
  giá trị kỳ vọng cho khớp hành vi hiện tại, không thêm `[Skip]`, không đổi test thành
  `Assert.True(true)`. Sửa test đã có chỉ hợp lệ khi **spec thay đổi** và issue nói rõ điều đó.
- Nếu bạn thực sự tin test SAI (không phải code sai): **đừng sửa nó**. Ghi lập luận vào
  `.loop/dispute.md` (test nào, sai chỗ nào, hành vi đúng phải là gì, dẫn chứng từ
  `overall.md`/docs), rồi dừng và báo cáo. Con người phân xử.
- Mỗi issue sửa xong phải có **test hồi quy** chứng minh lỗi cũ không quay lại — test
  phải ĐỎ trên code cũ và XANH trên code mới. Test chỉ assert HTTP 200 là không đủ.
- Ngưỡng CI của dự án: BE ≥ 80% line coverage.

---

## 7. Bẫy đã trả giá

- **`make ci-test` dùng `--no-build`** ⇒ đo bản đã build lần trước. Luôn `make ci-build`
  trước. Đã từng báo nhầm 2548 test trong khi thực tế là 2682.
- **`TestResults/` giữ TRX của mọi lần chạy cũ** (~279 file). Gộp cả thư mục là đọc kết
  quả tuần trước. Loop dùng thư mục riêng `.loop/out/trx-*` và xoá trước mỗi lần.
- **Harness MassTransit mặc định inactivity ~1,2s.** Test đỏ thất thường kiểu
  `Consumed.Any<T>() to be true but found False` là **HẾT GIỜ**, không phải sai logic.
  Chẩn: các test fail cụm cuối run, duration ~1,2–1,6s. Sửa bằng `x.SetTestTimeouts(30s, 15s)`.
- **MassTransit lấy message type từ `typeof(T)` lúc chạy.** Truyền biến kiểu
  `IntegrationEvent` thì consumer của type cụ thể KHÔNG nhận được.
- **Policy `AdminOnly` là code chết** — dùng `[Authorize(Roles = "Admin")]`.
- **`MockQueryable` chạy in-memory**, không dịch sang SQL. Sort/filter qua navigation
  nullable phải viết null-safe ternary, nếu không NRE ngay trong test.
- **`BatteryService.IntegrationTests` có bản sao Helpers riêng** (cùng namespace
  `UnitTests.Helpers` nhưng không tham chiếu project UnitTests). Thêm helper phải copy
  **cả 2 nơi**.
- **Project `TicketService.IntergrationTests` gõ sai chính tả** ("Interg") nên filter
  `FullyQualifiedName!~IntegrationTests` KHÔNG loại nó → nó chạy nhầm ở stage unit
  (không có guard Docker) và không bao giờ chạy ở stage integration.
- **Baseline `dev` từng có test đỏ sẵn.** Trước khi kết luận "mình làm hỏng", so với
  baseline. Baseline hiện tại của nhánh này: **build 0 error, 2818 unit test xanh,
  e2e-smoke 14/14 xanh**.
- **Hook cảnh báo namespace mismatch là FALSE POSITIVE** với mọi project test (nó kỳ
  vọng tiền tố `tests.`). Bỏ qua.
- **`.claude/` bị GitHub Action đồng bộ GHI ĐÈ.** Ghi quyết định vào
  `docs/non-obvious-decisions.md`, đừng ghi vào `.claude/`.

---

## 8. Từ ngữ dùng sai sẽ hiểu sai

- **"GetAllAsync"** ở đây là hàm **đồng bộ** trả `IQueryable`, KHÔNG phải hàm bất đồng bộ.
- **"DeleteAsync"** ở đây là **xoá mềm** (interceptor đổi thành `IsDeleted = true`),
  KHÔNG phải xoá vật lý, và trả **void**.
- **"primary"** (SensorSourceCode) nghĩa là nguồn BMS chính, KHÔNG phải "khoá chính".
- **"Escalate"** nghĩa là thêm nhân lực/nâng cấp bậc xử lý, KHÔNG phải gia hạn deadline.
- **"P1/P2/P3"** là mức SLA của ticket (4h/24h/72h), KHÔNG phải mức độ nghiêm trọng của
  issue trên GitHub (dù nhãn trùng tên).
