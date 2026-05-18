# Giới thiệu dự án — GSU26SE55

> **Hệ thống quản lý và bảo trì pin Lithium-ion cho điện mặt trời**
>
> Capstone GSU26SE55 — GVHD: Trương Long · Thời gian: 11/5/2026 → 6/9/2026
> Nhóm 5 sinh viên (3 Backend + 2 Frontend, AI là vai trò chung).

---

## 1. Dự án này giải quyết vấn đề gì?

Các trang trại điện mặt trời (solar farm) và hộ gia đình lắp pin lưu trữ năng lượng đang gặp ba vấn đề lớn:

1. **Không biết pin đang "khỏe" hay "yếu".** Pin lithium-ion xuống cấp theo thời gian. Khi pin gần hỏng, hệ thống có thể bốc cháy hoặc mất điện đột ngột nhưng người dùng không hề biết trước.
2. **Phát hiện sự cố quá muộn.** Hiện tại, kỹ thuật viên chỉ đến kiểm tra khi đã có hỏng hóc — lúc đó thiệt hại đã xảy ra.
3. **Quy trình bảo trì lộn xộn.** Không có công cụ chuẩn để khách hàng báo sự cố, quản lý phân công kỹ thuật viên, và theo dõi việc xử lý có đúng hạn hay không.

**Dự án xây dựng một nền tảng phần mềm hoàn chỉnh** để: theo dõi pin liên tục 24/7, dùng AI dự đoán pin nào sắp hỏng, và quản lý quy trình bảo trì theo chuẩn quốc tế (ITIL — chuẩn quản lý dịch vụ IT/kỹ thuật).

---

## 2. Hệ thống làm được những gì? (Góc nhìn người dùng)

### Cho **Khách hàng** (chủ pin / chủ trang trại điện)

- Mở app điện thoại xem pin của mình đang ở tình trạng nào (như xem app ngân hàng xem số dư).
- Nhận thông báo đẩy ngay khi pin có vấn đề (quá nóng, sụt áp, sức khỏe pin giảm dưới 80%).
- Tạo yêu cầu hỗ trợ trong app khi cần kỹ thuật viên đến xử lý.
- Đánh giá chất lượng dịch vụ sau khi kỹ thuật viên xử lý xong.

### Cho **Kỹ thuật viên** (Staff)

- Mở web/app thấy danh sách công việc được giao, sắp xếp theo mức độ khẩn cấp.
- Mỗi công việc có "đồng hồ đếm ngược" — buộc xử lý đúng thời hạn (4h / 24h / 72h tùy mức độ).
- Ghi lại quá trình bảo trì: thay linh kiện gì, lỗi do đâu, kèm ảnh chụp.

### Cho **Quản lý** (Manager)

- Xem dashboard tổng quan: bao nhiêu pin đang gặp sự cố, kỹ thuật viên nào đang quá tải, ticket nào sắp trễ hạn.
- Phân công công việc cho kỹ thuật viên phù hợp (theo tay nghề: Tier 1 — sơ cấp, Tier 2 — trung, Tier 3 — chuyên gia).
- Báo cáo định kỳ cho cấp trên.

### Cho **Quản trị hệ thống** (Admin)

- Quản lý tài khoản, phân quyền cho toàn bộ nhân viên và khách hàng.
- Cấu hình hệ thống: ngưỡng cảnh báo, quy tắc gán việc, v.v.

---

## 3. Hệ thống hoạt động như thế nào? (Câu chuyện đơn giản)

Hình dung một quả pin lithium-ion được lắp tại một trang trại điện mặt trời ở Bình Thuận:

```
1. Quả pin có gắn cảm biến đo điện áp, dòng điện, nhiệt độ liên tục.

2. Một thiết bị nhỏ gọi là "Gateway" (giống cục Wi-Fi router, đặt cạnh pin)
   thu thập số liệu mỗi 30 giây và gửi qua internet về máy chủ.

3. Máy chủ lưu lại toàn bộ lịch sử số liệu (như một cuốn nhật ký y tế của pin).

4. Một module AI (trí tuệ nhân tạo) phân tích số liệu:
   - Pin này còn khỏe bao nhiêu %? (đo "tuổi thọ" — gọi là SOH)
   - Có dấu hiệu bất thường không? (quá nóng, sụt áp đột ngột, lão hóa nhanh)

5. Nếu phát hiện bất thường, hệ thống TỰ ĐỘNG:
   - Đẩy thông báo về app khách hàng.
   - Tạo một "ticket" (phiếu yêu cầu xử lý) gửi vào hàng đợi của Quản lý.
   - Bắt đầu đếm ngược thời hạn: nếu là cảnh báo nguy hiểm → 4 giờ phải xử lý xong.

6. Quản lý nhận ticket, phân công cho kỹ thuật viên phù hợp.

7. Kỹ thuật viên xử lý, ghi nhật ký, đóng ticket.

8. Khách hàng đánh giá → kết thúc một chu trình.
```

---

## 4. Hệ thống gồm những thành phần nào?

Hình dung hệ thống như một **nhà hàng đa chi nhánh**:

| Thành phần | Ví dụ tương đương | Vai trò |
|------------|-------------------|---------|
| **Mobile App** (cho Khách hàng) | Thực đơn + nút gọi phục vụ ở bàn | Khách hàng tự theo dõi, tự báo sự cố |
| **Web App** (cho Admin/Manager/Staff) | Phòng điều hành bếp + quầy thu ngân | Nhân viên nội bộ vận hành |
| **API Gateway** | Lễ tân nhà hàng | Đầu mối duy nhất tiếp nhận yêu cầu, kiểm tra xem ai được vào |
| **Auth Service** | Bảo vệ + danh sách nhân viên | Đăng nhập, phân quyền, OTP, đăng nhập bằng Google |
| **Battery Service** | Bộ phận theo dõi nguyên liệu tươi | Quản lý từng quả pin, ghi nhận số liệu cảm biến, phát hiện bất thường |
| **Ticket Service** | Hệ thống phiếu order + đồng hồ bếp | Quản lý quy trình xử lý sự cố theo đúng thời hạn (SLA) |
| **Notification Service** | Loa thông báo "đơn của bàn 5 đã xong" | Gửi push, email, SMS |
| **AI Module** | Đầu bếp dày kinh nghiệm nhìn nguyên liệu biết có ngon không | Dự đoán sức khỏe pin, phát hiện bất thường |
| **IoT Gateway** | Bồi bàn ghi order ở bàn | Thiết bị đặt tại trang trại điện, thu thập số liệu từ pin gửi về máy chủ |
| **Database (PostgreSQL + TimescaleDB)** | Sổ cái + sổ nhật ký bán hàng | Lưu trữ tất cả dữ liệu |

---

## 5. Vai trò người dùng (Roles)

Hệ thống có **4 vai trò**, mỗi vai trò có quyền hạn khác nhau:

| Vai trò | Đối tượng | Quyền chính |
|---------|-----------|-------------|
| **Admin** | Quản trị viên hệ thống | Tạo tài khoản, cấu hình toàn cục, xem mọi thứ |
| **Manager** | Trưởng bộ phận bảo trì | Phân công công việc, duyệt ticket, xem báo cáo |
| **Staff** | Kỹ thuật viên (3 cấp tay nghề: Tier 1/2/3) | Nhận và xử lý ticket được giao |
| **Customer** | Chủ pin / chủ trang trại | Xem pin của mình, tạo ticket, đánh giá dịch vụ |

> Khách hàng **không đăng nhập web** — chỉ dùng app điện thoại. Web là dành cho nhân viên nội bộ.

---

## 6. SLA — Cam kết thời gian xử lý

Hệ thống áp dụng chuẩn **ITIL** (chuẩn quốc tế cho quản lý dịch vụ kỹ thuật), với 3 mức độ ưu tiên:

| Mức | Tên | Thời hạn xử lý | Khi nào áp dụng |
|-----|-----|----------------|-----------------|
| 🔴 **P1** | Critical | **< 4 giờ** | Nguy cơ cháy nổ, mất điện toàn site, ảnh hưởng an toàn |
| 🟠 **P2** | High | **< 24 giờ** | Pin xuống cấp đáng kể, một cụm pin có vấn đề |
| 🟡 **P3** | Standard | **< 72 giờ** | Bất thường nhẹ, bảo trì định kỳ, một viên pin riêng lẻ |

**Quy tắc quan trọng:** mức ưu tiên được Manager gán **một lần duy nhất** khi tiếp nhận, không thay đổi sau đó. Nếu trễ hạn → tự động leo thang (escalate) lên kỹ thuật viên cấp cao hơn, **không** kéo dài thời hạn.

---

## 7. Vòng đời một "Ticket" (yêu cầu xử lý)

Mỗi sự cố tạo ra một ticket đi qua các trạng thái:

```
NEW (mới)
  ↓
OPEN (đã ghi nhận)
  ↓ ← Manager phân công
ASSIGNED (đã giao cho kỹ thuật viên)
  ↓ ← Staff bắt đầu làm
IN_PROGRESS (đang xử lý)
  ↓ ← Staff hoàn tất
RESOLVED (đã xử lý)
  ↓ ← Khách hàng đánh giá
CLOSED (đã đóng)
```

Có hai nhánh đặc biệt:
- **ESCALATED** — leo thang khi trễ hạn hoặc Staff yêu cầu hỗ trợ
- **CLOSED_REJECTED** — Manager từ chối nếu yêu cầu ngoài phạm vi dịch vụ

---

## 8. Phần AI dùng để làm gì?

AI giúp **tự động phát hiện vấn đề** mà mắt người không nhìn ra trong dữ liệu cảm biến.

Có **hai mô hình AI** hoạt động song song:

### Mô hình 1 — Dự đoán "tuổi thọ pin" (SOH)
- Dùng mạng nơ-ron học sâu (LSTM/CNN-LSTM — chuyên xử lý dữ liệu theo thời gian).
- Đầu vào: 30 lần đo gần nhất (điện áp, dòng điện, nhiệt độ).
- Đầu ra: phần trăm sức khỏe pin (ví dụ "pin này còn 85% so với khi mới").
- Pin dưới 80% được coi là cần thay.

### Mô hình 2 — Phát hiện bất thường
- Dùng thuật toán **Isolation Forest** (chuyên tìm điểm dữ liệu "lạ").
- Phân loại pin thành: **Bình thường / Đang xuống cấp / Hỏng**.

### Dữ liệu huấn luyện
- Dataset **NASA Ames** (cơ quan vũ trụ Mỹ công khai dữ liệu pin) — ưu tiên chính.
- CALCE (Đại học Maryland) — dự phòng.

### Yêu cầu chất lượng
- Sai số dự đoán SOH **< 2%**.
- Tốc độ phân tích **< 100ms** mỗi lần (đủ nhanh để cảnh báo gần như tức thì).

---

## 9. Phần IoT — Cách dữ liệu pin về được máy chủ

Đây là phần "phần cứng" của dự án:

- **Cảm biến** gắn trên pin đo: điện áp, dòng điện, nhiệt độ, SOC (% pin còn lại), SOH (tuổi thọ).
- **Gateway** (máy tính nhỏ — Raspberry Pi hoặc ESP32 — to bằng bao thuốc) đặt cạnh pin, đọc số liệu từ pin qua các giao thức công nghiệp (RS485/Modbus, CAN bus, UART).
- **Internet (Wi-Fi/4G)** — Gateway gửi số liệu về máy chủ mỗi 30 giây.
- **Cơ chế chống mất mạng:** nếu Wi-Fi hỏng, Gateway lưu tạm vào bộ nhớ, khi có mạng lại thì gửi bù.

**MVP demo:** nhóm có **bản giả lập (simulator)** chạy trên máy tính để demo toàn bộ luồng mà không cần pin thật — phục vụ buổi bảo vệ KLTN.

---

## 10. Lộ trình 8 Sprint (4 tháng)

> Mỗi Sprint = 2 tuần làm việc.

| Sprint | Tập trung làm gì |
|--------|------------------|
| **S1** | Nền tảng đăng nhập, phân quyền, upload file |
| **S2** | Quản lý pin: nhập danh sách pin, phân nhóm theo site |
| **S3** | Thu thập dữ liệu cảm biến + biểu đồ theo dõi |
| **S4** | Hệ thống cảnh báo + tích hợp AI dự đoán |
| **S5** | Hệ thống ticket + đồng hồ SLA |
| **S5B** | App mobile cho Khách hàng + thông báo đẩy |
| **S6** | Tính năng nâng cao: comment, log bảo trì, đính kèm ảnh |
| **S7** | Báo cáo, dashboard, tìm kiếm |
| **S8** | Hoàn thiện, triển khai production, chuẩn bị demo |

Song song có **track IoT** (Sprint IoT-0 → IoT-3) cho phần phần cứng + simulator.

---

## 11. Công nghệ chính (tóm tắt — không cần hiểu sâu)

| Phần | Công nghệ | Vì sao chọn |
|------|-----------|-------------|
| Backend (máy chủ) | **ASP.NET Core (.NET 8)** — ngôn ngữ C# của Microsoft | Chuẩn doanh nghiệp, ổn định, hiệu năng cao |
| Web | **React 19** + Tailwind CSS | Phổ biến, dễ tuyển dev, UI đẹp |
| Mobile | **React Native + Expo** | Một code chạy cả iOS và Android |
| AI | **Python + PyTorch + scikit-learn** | Tiêu chuẩn ngành cho AI/ML |
| Database | **PostgreSQL + TimescaleDB** | TimescaleDB tối ưu cho dữ liệu cảm biến theo thời gian |
| Hàng đợi tin nhắn | **RabbitMQ** | Cho các service "nói chuyện" với nhau bất đồng bộ |
| Cache | **Redis** | Tăng tốc truy vấn lặp lại |
| Lưu file | **MinIO** | Tự host, tương thích Amazon S3 |
| Triển khai | **Docker + Kubernetes + Helm** | Chuẩn industry để deploy production |
| CI/CD | **GitHub Actions + Jenkins** | Tự động build/test/deploy |

---

## 12. Tại sao dự án này có ý nghĩa?

1. **Tính thời sự:** Việt Nam đang đẩy mạnh điện mặt trời, hệ thống lưu trữ pin sẽ bùng nổ trong 5 năm tới — nhu cầu phần mềm quản lý pin là có thật.
2. **Tính an toàn:** Cháy nổ pin lithium là rủi ro thật. Phát hiện sớm có thể cứu tài sản và tính mạng.
3. **Tính kỹ thuật:** Dự án kết hợp đủ stack hiện đại — backend microservice, AI/ML, IoT, mobile — phù hợp tiêu chí KLTN.
4. **Tính ứng dụng:** Có thể demo end-to-end bằng pin thật + Raspberry Pi tại buổi bảo vệ.

---

## 13. Đội ngũ thực hiện

- **5 sinh viên** chia 2 mảng: 3 Backend (xây máy chủ, AI, IoT) + 2 Frontend (web + mobile).
- **Một thành viên kiêm Leader** điều phối tiến độ, phân công, review code.
- **Quy trình làm việc** theo Agile/Scrum, sprint 2 tuần, có code review bắt buộc trước khi merge.
- **Tự động hóa** — toàn bộ code có CI/CD tự động kiểm tra chất lượng (lint, test, security scan) trước khi gộp vào nhánh chính.

---

## 14. Tài liệu kỹ thuật chi tiết (cho dev và GVHD)

Nếu cần đào sâu hơn từng phần:

| Tài liệu | Nội dung |
|----------|---------|
| [`overall.md`](overall.md) | Master backlog kỹ thuật đầy đủ (~8.000 dòng) |
| [`iot.md`](iot.md) | Chi tiết phần cứng + plan IoT |
| [`README.md`](README.md) | Hướng dẫn setup cho dev mới |
| [`.claude/docs/core-business-flow.md`](.claude/docs/core-business-flow.md) | Luồng nghiệp vụ 4 role · 6 phase |
| [`docs/adr/`](docs/adr) | Architecture Decision Records — vì sao chọn giải pháp X thay vì Y |
| [`docs/api-*.md`](docs) | Tài liệu API cho từng service |

---

## Tóm lại trong 3 câu

> Đây là một **hệ thống phần mềm + phần cứng + AI** để giám sát và bảo trì pin mặt trời theo thời gian thực.
>
> Người dùng cuối là chủ trang trại điện và đội kỹ thuật bảo trì.
>
> Giá trị mang lại: **phát hiện sớm sự cố pin, xử lý đúng hạn theo chuẩn quốc tế, giảm rủi ro cháy nổ và downtime.**
