# Nhập dữ liệu khách hàng & thiết bị từ bên thứ ba

> **Trạng thái:** đã hiện thực xong · **Cập nhật:** 2026-09-01
> **Phạm vi:** BatteryService (chính) · AuthService · EmailService · ApiGateway · SharedContracts
> **Tài liệu này thay thế** `PARTNER_DATA_IMPORT_PLAN.md` (kế hoạch cũ, đã lỗi thời sau hai lần thu hẹp phạm vi).

## Tính năng làm gì

Bên thứ ba (đơn vị lắp đặt, nhà phân phối) bàn giao danh sách khách hàng, site và pin dưới dạng CSV.
Admin tải file lên, xem trước kết quả kiểm định, rồi ghi vào hệ thống. Mỗi khách hàng được cấp tài
khoản tự động để dùng app; site và pin gắn sẵn vào khách, sẵn sàng cho việc bảo trì.

## Hai lần thu hẹp phạm vi (và vì sao)

| Đã bỏ | Lý do |
|---|---|
| **Nhập thiết bị IoT** (2026-08-20) | Thiết bị do hệ thống cấp phát cùng khoá API và credential MQTT — bên thứ ba không thể khai sinh một thiết bị. Bản đầu sai theo hai hướng: mã đã tồn tại (ca duy nhất có thật) thì bị đánh hỏng, còn mã lạ thì đẻ ra bản ghi cho phần cứng có thể không tồn tại, kèm khoá không ai nhận và **không** có credential MQTT nên thiết bị đó không bao giờ nối được broker. Gán gateway vào site làm ở màn quản trị thiết bị. |
| **Tài khoản đối tác + khoá `sb_live_`** (2026-08-20) | Chốt: chỉ Admin được đưa dữ liệu bên thứ ba vào hệ thống. Bỏ luôn `PartnerAccount`, hai controller đối tác, nhánh xác thực khoá đối tác, và cột định danh đối tác ở hai bảng. |

Bỏ cột định danh đối tác còn **sửa được một lỗ hổng thật**: khoá duy nhất cũ là
`(partner_id, entity_type, external_ref)` với `partner_id` cho phép rỗng, mà PostgreSQL coi mỗi giá
trị rỗng là khác nhau — nên với lô do Admin tải lên (luôn rỗng) ràng buộc **không chặn được gì**.
Đã kiểm chứng bằng cách chèn hai dòng trùng: trước khi sửa lọt cả hai, sau khi sửa bị chặn.

---

## 1. Ai được làm gì

| Việc | Ai |
|---|---|
| Toàn bộ 8 endpoint nhập dữ liệu | **Chỉ Admin** |
| Manager · Staff · Customer | Không có quyền nào |

Nhập dữ liệu tạo ra **tài khoản khách hàng ở AuthService** và có thể gỡ hàng loạt bản ghi khi hoàn
tác → thuộc quyền quản trị hệ thống, không phải điều hành hằng ngày.

## 2. Hợp đồng API

| Method | Path | Mô tả |
|---|---|---|
| `GET` | `/api/imports/templates` | File Excel mẫu — một file `.xlsx`, ba sheet (khách/site/pin) |
| `POST` | `/api/imports/batches` | Gửi **một file `.xlsx`** (field `file`) → kiểm định. **Không ghi dữ liệu nghiệp vụ** |
| `GET` | `/api/imports/batches` | Danh sách lô |
| `GET` | `/api/imports/batches/{id}` | Chi tiết + bộ đếm tiến độ |
| `GET` | `/api/imports/batches/{id}/rows` | Từng dòng, lọc theo trạng thái |
| `GET` | `/api/imports/batches/{id}/errors.csv` | Tải dòng hỏng kèm lý do |
| `POST` | `/api/imports/batches/{id}/commit` | Ghi thật (202, tiến trình nền làm) |
| `POST` | `/api/imports/batches/{id}/revert` | Hoàn tác |

Giao diện: `/admin/data-import` — menu **Third-party import**.

> **Đổi 2026-09-01:** trước đây mỗi loại (khách/site/pin) là một file CSV riêng, nộp tối đa ba lần
> một lượt (`customersFile`/`sitesFile`/`assetsFile`). Giờ chỉ còn **một file `.xlsx`** với ba sheet
> cố định tên `1-Customers`, `2-Sites`, `3-Assets` (đúng thứ tự ghi thật). `ImportWorkbookSplitter`
> tách workbook thành ba luồng CSV ngay khi nhận — **toàn bộ pipeline phía sau (kiểm định, đối
> chiếu chéo, ghi thật, hoàn tác) không đổi một dòng nào**, vẫn hoạt động trên CSV như cũ. Đổi tên
> sheet vẫn nạp được nhờ dò theo vị trí (0=khách, 1=site, 2=pin) nếu không khớp đúng tên. Một sheet
> không có dòng dữ liệu nào (ngoài dòng chú thích `#`) coi như không tham gia lô — giống hệt trước
> đây không đính kèm file cho loại đó, không phải lỗi.
>
> Đọc ô Excel theo đúng kiểu dữ liệu (số/ngày), không đọc theo chuỗi hiển thị: một ô số luôn ra dấu
> chấm thập phân bất kể Excel của người dùng đặt vùng miền gì (kể cả Excel tiếng Việt hiển thị dấu
> phẩy), và một ô ngày luôn ra `yyyy-MM-dd` bất kể định dạng hiển thị của ô — né được cả lớp lỗi
> "74,5 bị từ chối" mà trước đây người nhập CSV phải tự tránh bằng tay.

### Mã trạng thái trả về

| Mã | Khi nào | Ví dụ thông báo |
|---|---|---|
| `201` | Kiểm định xong — kể cả khi có dòng hỏng | `Validated 6 rows: 1 valid, 5 invalid. Nothing has been written yet.` |
| `202` | Đã nhận lệnh ghi, tiến trình nền đang làm | `Writing 7 row(s). Poll this batch to follow progress.` |
| `400` | Không gửi file nào, hoặc file rỗng 0 byte | |
| `404` | Không có lô với định danh đó | `Import batch not found.` |
| `409` | Nạp lại **đúng file đã nạp** (so khớp SHA-256) | `This exact file was already imported on … Revert that batch first…` |
| `409` | Cam kết lô sai trạng thái, hoặc lô không còn dòng hợp lệ nào | `This batch has no valid rows to write. Fix the file and upload it again.` |
| `409` | Hoàn tác lô chưa kết thúc, hoặc đã hoàn tác rồi | `Only a finished batch can be reverted. This batch is …` |
| `422` | File đọc được nhưng **không dùng được**: thiếu cột bắt buộc, không có dòng dữ liệu nào, hoặc vượt trần 5000 dòng | `customers.csv: The file is missing required columns: …` |

Phân biệt `422` với `201`-kèm-dòng-hỏng: `422` là hỏng ở mức **file** nên không có gì để xem trước;
`201` là file đọc được, từng dòng hỏng đã ghi vào lô và tải về được qua `errors.csv`.

## 3. Định dạng file

Một workbook `.xlsx`, ba sheet cố định tên và thứ tự:

| Sheet | Cột bắt buộc | Cột tuỳ chọn |
|---|---|---|
| `1-Customers` | `external_customer_code`, `full_name`, `email` | `phone` |
| `2-Sites` | `external_site_code`, `external_customer_code`, `site_name` | `address`, `latitude`, `longitude`, `install_date`, `contact_person_name`, `contact_person_phone` |
| `3-Assets` | `external_asset_code`, `external_site_code`, `serial_number`, `battery_type_name` | `manufacturer`, `nominal_capacity_ah`, `nominal_voltage`, `chemistry`, `install_date`, `warranty_end_date`, `location`, `notes` |

Ba sheet nối nhau bằng **mã của bên thứ ba**, không bằng định danh nội bộ — lúc họ xuất file chưa
biết ID bên mình.

> **Cấm** đặt tên cột chứa `kwh`, `capacity_kw`, `co2` — bộ kiểm tra CI (`rule-checks.sh` rule 4)
> quét hai thư mục `services/BatteryService/src` và `shared/src` và sẽ đỏ. Dung lượng pin chỉ dùng
> `nominal_capacity_ah`.

## 4. Quy tắc kiểm định

**Chung:** gom **tất cả** lỗi của một dòng, không dừng ở lỗi đầu. Mã tham chiếu ≤ 128 ký tự.

| Trường | Quy tắc |
|---|---|
| Email | Bắt buộc, đúng định dạng, ≤ 256, tự chuyển chữ thường |
| Họ tên | Bắt buộc, ≤ 150 |
| Điện thoại | ≤ 20 |
| Tên site | Bắt buộc, ≤ 200 · Địa chỉ ≤ 500 |
| Toạ độ | −90…90 / −180…180, **dấu chấm thập phân** |
| Serial pin | Tự chuẩn hoá `pyl/us3000c 88a21` → `PYL-US3000C-88A21`; sau chuẩn hoá 5–64 ký tự. Báo lỗi kèm giá trị đã chuẩn hoá |
| Dung lượng / Điện áp | Số dương, dấu chấm. `74,5` bị từ chối — đọc theo chuẩn bất biến sẽ thành 745 |
| Chemistry | Thuộc danh sách, sai thì liệt kê giá trị hợp lệ |
| Ngày hết bảo hành | Phải sau ngày lắp |
| Vị trí / Ghi chú | ≤ 255 / ≤ 1000 |

**Ngày tháng** — 6 dạng: `yyyy-MM-dd`, `yyyy/MM/dd`, `dd-MM-yyyy`, `dd/MM/yyyy`, 2 dạng ISO có giờ.
Cố tình **không** nhận `MM/dd/yyyy`: `03/04/2021` khi đó có hai nghĩa, đoán sai thì ngày lắp lệch cả
tháng mà không ai phát hiện.

**Ngày lắp** — không được ở tương lai, không cũ quá **15 năm**. Đường tạo thủ công chặn ở 5 năm; nới
lên vì dữ liệu bàn giao từ đơn vị lắp đặt lâu năm gần như chắc chắn có pin từ 2018–2019, giữ 5 năm
là loại sạch cả file. Thiếu ngày lắp thì lấy hôm nay (cột DB không cho rỗng).

## 5. Tham chiếu chéo và chống trùng

- Site phải trỏ tới khách **có thật**; pin phải trỏ tới site **có thật**
- "Có thật" = mã nằm trong chính lô này, **hoặc** đã nạp ở lô trước và còn trong bản đồ liên kết —
  không tính nguồn thứ hai thì nạp bổ sung luôn bị từ chối
- Hai dòng cùng mã trong một lô → **cả hai** đánh hỏng, báo rõ số dòng
- Nạp lại đúng nội dung đã nạp → **409**, chỉ rõ lô cũ. So theo **hash nội dung**, không theo tên file
- Lô đã hoàn tác hoặc hỏng không tính là trùng

## 6. Vòng đời

```
Pending(1) → Parsing(2) → Validating(3) ─┬→ ValidationFailed(4)
                                          └→ ReadyToCommit(5)
ReadyToCommit(5) → Committing(6) → AwaitingAccountSync(7) ─┬→ Completed(8)
                                                            ├→ CompletedWithErrors(9)
                                                            └→ Failed(12)
→ Reverting(10) → Reverted(11)
```

`Committing` và `AwaitingAccountSync` tách riêng có chủ ý: "đang ghi" khác hẳn "đang chờ AuthService
trả tài khoản". Gộp lại thì người vận hành không biết lô đứng im vì hệ thống bận hay vì message bus tắc.

**Trạng thái dòng:** Valid(2) · Invalid(3) · AwaitingAccount(4) · Created(5) · Updated(6) ·
**Skipped(7)** · Failed(8) · Reverted(9).
`Skipped` = email khách đã tồn tại → liên kết vào tài khoản cũ. **Không phải lỗi.**

## 7. Pha ghi thật

Thứ tự **bắt buộc**: khách hàng → site → pin. Mỗi nhịp tiến một bậc rồi lưu.

**Bậc 1 — cấp tài khoản (xuyên service, qua RabbitMQ)**

| Tình huống | Xử lý |
|---|---|
| Email chưa có | Tạo tài khoản **`Status = Active`**, mật khẩu ngẫu nhiên đạt policy, vai trò Customer |
| Email đã có | Liên kết vào tài khoản cũ → dòng `Skipped`, phát thêm snapshot để chắc chắn bản sao có mặt |
| SĐT trùng tài khoản khác | **Bỏ trống SĐT, vẫn tạo khách** — đường tạo tay trả 409 chặn cả dòng, với nhập hàng loạt thì mất luôn khách, site và pin vì một thông tin phụ |
| Không có vai trò Customer | Báo hỏng ngay, không chờ tiếp |

*Vì sao Active:* handler tạo Site/Pin đòi bản sao khách hàng đang hoạt động, mà bản sao chỉ sinh ra
từ `AccountActivatedEvent` — đường mời không phát event này.

*Mật khẩu:* sinh bằng nguồn mã hoá, **không gửi cho ai**. Khách tự đặt qua "Quên mật khẩu" (luồng đó
chỉ đòi tài khoản Active). Tránh mật khẩu nằm trong hộp thư, không phải thêm cột cờ vào bảng tài khoản.

Quá **10 phút** chưa có bản sao → dòng `Failed` kèm lý do. Không treo vô hạn.

**Bậc 2 — Site**: không tra được khách → hỏng · bản sao chưa đồng bộ → hỏng · đã có liên kết → cập
nhật · tên site trùng trong cùng khách mà chưa có liên kết → hỏng · còn lại → tạo mới `Active`.

**Bậc 3 — Pin**: không tra được site → hỏng · loại pin chưa có + file đủ dung lượng và điện áp → **tự
tạo loại pin** · thiếu thông số → hỏng (tạo bằng giá trị đoán sẽ khiến mọi ngưỡng cảnh báo về sau đều
lệch) · serial đã tồn tại mà chưa có liên kết → hỏng · còn lại → tạo mới, **bảo hành tự tính**.

> **Bẫy đã xử lý:** nhiều pin trong cùng lô dùng chung một loại pin mới. Loại vừa tạo chưa lưu xuống
> nên truy vấn không thấy — nếu chỉ dựa vào truy vấn thì mỗi dòng đẻ thêm một loại trùng tên, tới lúc
> lưu vi phạm ràng buộc duy nhất và hỏng cả lô. Có bộ nhớ tạm trong lượt để chặn.

## 8. Hoàn tác

- Chỉ hoàn tác lô **đã kết thúc**
- Xoá **ngược thứ tự**: pin → site (đi xuôi sẽ vấp khoá ngoại)
- **Chỉ gỡ bản ghi lô này tạo mới**; dòng `Updated` ghi đè lên bản ghi có sẵn nên không đụng
- Gỡ bản đồ liên kết để lần nạp sau tạo lại từ đầu
- **Không xoá, không vô hiệu hoá tài khoản khách hàng** — chúng có thể đã dùng để đăng nhập hoặc gắn
  phiếu bảo trì. Thông điệp trả về nói rõ điều này
- Xoá mềm toàn bộ

## 9. Tham số vận hành

| Tham số | Mặc định | Biến môi trường |
|---|---|---|
| Số dòng tối đa/lô | 5.000 | `Import__MaxRowsPerBatch` |
| Hạn chờ tài khoản | 10 phút | `Import__AccountSyncTimeoutMinutes` |
| Nhịp quét lô | 5 giây | `Import__PollIntervalSeconds` |
| Giữ lô cũ | 90 ngày | `Import__RetentionDays` |
| Ngày lắp lùi tối đa | 15 năm | `Import__MaxInstallDateAgeYears` |
| Số lần thử một dòng | 5 | `Import__MaxRowAttempts` |
| Kích thước file | 10 MB | (hằng số trong controller) |

Tiến trình nền duyệt **tối đa 20 lô mỗi nhịp** — bản đầu chỉ lấy lô cũ nhất và một lô đang chờ đã
chặn đứng mọi lô sau nó. Một lô hỏng không kéo theo lô khác. Dọn định kỳ **chỉ đụng lô ở trạng thái
cuối**: lô đang chờ là dấu hiệu sự cố cần người xem.

## 10. Mô hình dữ liệu

Ba bảng, đều kế thừa `AuditableEntity`, đều xoá mềm.

- **`import_batches`** — một lần nạp: tên file, `file_sha256` (chặn nạp trùng theo nội dung), trạng
  thái, cờ chạy thử, người bấm, bộ đếm (tổng / hợp lệ / hỏng / đã tạo / đã cập nhật / bỏ qua / thất
  bại), mốc thời gian, lỗi cấp lô
- **`import_rows`** — một dòng: số dòng gốc, loại, `raw_json` (nguyên văn để tra ngược), mã tham
  chiếu, trạng thái, `errors_json`, **`created_entity_id`** (chìa khoá cho hoàn tác), tài khoản đã
  liên kết, số lần thử
- **`import_entity_links`** — bản đồ mã bên ngoài ↔ định danh nội bộ. Khoá duy nhất
  `(entity_type, external_ref)` lọc `is_deleted = false`

> `created_entity_id` nằm ở `import_rows` thay vì thêm cột vào `battery_assets`/`sites`: hai bảng đó
> đang mang dữ liệu thật, thêm cột là migration xâm lấn phải nạp bù. Hoàn tác chỉ cần duyệt các dòng
> của lô.

## 11. Sự kiện, tiến trình nền, vết kiểm toán

| Sự kiện | Ai xử lý |
|---|---|
| `PartnerCustomerProvisionRequestedEvent` | AuthService → tạo tài khoản |
| `PartnerCustomerProvisionedEvent` | BatteryService → gắn tài khoản vào dòng |
| `SendPartnerImportWelcomeEvent` | EmailService → thư chào mừng (template `PartnerImportWelcome.html`) |

> Tên ba sự kiện và template vẫn mang chữ "Partner" theo nghĩa **nguồn dữ liệu là bên thứ ba** — đây
> là khái niệm nghiệp vụ vẫn còn, khác với thực thể `PartnerAccount` đã bị xoá. Đổi tên sẽ kéo theo
> AuthService, EmailService và các chuỗi audit đã ghi xuống DB.

**Tiến trình nền:** `ImportBatchProcessorBackgroundService` (ghi dữ liệu, chờ tài khoản, hết hạn thì
đánh hỏng) · `ImportRetentionBackgroundService` (dọn lô cũ).

**Audit:** `PartnerImportCommitted` · `PartnerImportReverted` (BatteryService) ·
`AccountProvisionedFromPartnerImport` (AuthService).

## 12. Những gì cố tình KHÔNG làm

| Không làm | Lý do |
|---|---|
| Nhập thiết bị IoT | Thiết bị do hệ thống cấp phát cùng khoá API và MQTT |
| Đối tác tự đẩy dữ liệu qua API | Chỉ Admin được đưa dữ liệu vào hệ thống |
| Hoàn tác xoá tài khoản khách | Có thể đã dùng để đăng nhập hoặc gắn phiếu bảo trì |
| Nhập lịch sử dữ liệu đo | Ngoài phạm vi; `SensorReadingSourceTypeEnum.External` đã sẵn nếu sau này cần |
| Connector kéo định kỳ | Ngoài phạm vi |
| Sửa validator của đường tạo thủ công | Sẽ làm hỏng hành vi hiện có và test đang xanh |

## 13. Ba điều dễ hiểu nhầm khi vận hành

1. **"Chờ cấp tài khoản" không phải treo** — tài khoản đi qua message bus, có quãng chờ thật. Quá 10
   phút mới bị đánh hỏng.
2. **Dòng "Bỏ qua" không phải lỗi** — email đã tồn tại thì liên kết vào tài khoản cũ.
3. **Chạy thử không ghi gì** — kể cả khi mọi dòng đều hợp lệ. Phải bấm Ghi thật.
