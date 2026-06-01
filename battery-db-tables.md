# BatteryService — Tác dụng các nhóm bảng (`battery_db`)

> Conceptual ERD của BatteryService trong hệ thống Solar Battery Maintenance.
> Trung tâm dữ liệu là **BATTERY_ASSET** và **SITE** — gần như mọi luồng (telemetry, alert, AI, năng lượng, IoT) đều quy về 2 thực thể này.
> Trạng thái: `[B]` đã build · `[P]` planned · `[P-bonus]` bonus.

---

## (A) Catalog & Topology — Danh mục & cấu trúc tài sản

Định nghĩa "có gì, ở đâu, thuộc loại nào" — bộ khung tham chiếu cho toàn service.

| Bảng | Tác dụng |
|------|----------|
| **CUSTOMER** | Bản sao thông tin khách hàng sync từ AuthService. Chủ sở hữu của Site và Battery Asset. |
| **SITE** | Trang trại/địa điểm lắp pin (vd: Solar Farm An Giang 1). Chứa toạ độ GPS để gọi Weather API, tổng công suất lắp đặt. |
| **BATTERY_TYPE** | Danh mục loại pin (model): dung lượng danh định, điện áp, hoá học (LiFePO4/NMC...), số chu kỳ tối đa. Dùng để phân loại asset và gắn ngưỡng. |
| **BATTERY_ASSET** | Quả pin vật lý cụ thể (theo SerialNumber). Thực thể trung tâm — gắn với hầu hết telemetry, alert, AI, năng lượng. |

---

## (B) Threshold — Cấu hình ngưỡng cảnh báo

Định nghĩa "khi nào coi là bất thường" — đầu vào cho engine sinh alert.

| Bảng | Tác dụng |
|------|----------|
| **THRESHOLD_CONFIG** | Ngưỡng cảnh báo cho pin theo từng BatteryType (min/max điện áp, nhiệt độ, SOC, SOH, nội trở...). Có versioning, chỉ 1 active/type. |
| **AMBIENT_THRESHOLD_CONFIG** | Ngưỡng môi trường theo Site (nhiệt độ/độ ẩm xung quanh). Khác với nhiệt độ thân pin. |

---

## (C) Telemetry — Dữ liệu cảm biến (time-series)

Dòng dữ liệu thô liên tục — nguồn chính cho alert và AI. Lưu hypertable TimescaleDB, append-only.

| Bảng | Tác dụng |
|------|----------|
| **SENSOR_READING** | Dữ liệu đo từ pin (V, A, °C, SOC, SOH...). Nguồn chính cho AI và alert. |
| **AMBIENT_READING** | Dữ liệu môi trường xung quanh Site (nhiệt độ, độ ẩm, bức xạ mặt trời). Từ IoT sensor hoặc Weather API. |

---

## (D) Alerts & Incidents — Cảnh báo & sự cố

Khi telemetry vượt ngưỡng → sinh alert, theo dõi vòng đời, escalation và sự cố an toàn.

| Bảng | Tác dụng |
|------|----------|
| **ALERT** | Cảnh báo khi vượt ngưỡng (14 loại anomaly). Có dedup, merge, trạng thái Open→Resolved. Có thể mở thành Ticket. |
| **SITE_ALERT** | Cảnh báo gộp cấp Site khi nhiều asset (≥5) cùng anomaly trong cửa sổ thời gian — tránh spam. |
| **ALERT_HISTORY** | Lịch sử chuyển trạng thái của alert (ai đổi, khi nào, lý do) — audit trail. |
| **ALERT_ACK_TIMELINE** | Theo dõi escalation khi alert chưa được ack (15m→Staff, 30m→Manager + auto tạo P1 ticket). |
| **ALERT_SILENCE_RULE** | Quy tắc tắt cảnh báo tạm thời (scope Asset/Site/Type) cho sự cố đã biết đang sửa — tránh nhiễu. |
| **ENVIRONMENTAL_INCIDENT** | Sự cố an toàn môi trường (khói, rò nước...). Nghiêm trọng hơn alert thường, có thể spawn alert. |

---

## (E) AI Inference Cache — Kết quả AI

Cache kết quả từ AI Module (FastAPI), tránh gọi lại mỗi lần truy vấn.

| Bảng | Tác dụng |
|------|----------|
| **SOH_PREDICTION** | Kết quả dự đoán SOH (State of Health) từ model LSTM/CNN-LSTM, kèm confidence, version, latency. |
| **ANOMALY_CLASSIFICATION** | Phân loại trạng thái (Normal/Degrading/Failed) từ Isolation Forest. Có staff feedback để cải thiện model. |

---

## (F) Preventive Maintenance — Bảo trì phòng ngừa

| Bảng | Tác dụng |
|------|----------|
| **MAINTENANCE_SCHEDULE** | Lịch bảo trì định kỳ theo asset (vệ sinh, kiểm tra, hiệu chuẩn). Tự tạo Ticket khi đến hạn. |

---

## (G) IoT Backend — Quản lý thiết bị IoT

Quản lý phần cứng thu thập dữ liệu tại hiện trường: vòng đời thiết bị, sức khoẻ, hiệu chuẩn, firmware.

| Bảng | Tác dụng |
|------|----------|
| **IOT_DEVICE** | Thiết bị IoT (gateway/sensor) deploy tại Site. Có API key riêng, theo dõi online/offline. |
| **IOT_DEVICE_HEARTBEAT** | Telemetry sức khoẻ thiết bị mỗi 60s (CPU, RAM, disk, queue depth, signal). Phát hiện thiết bị lỗi. |
| **IOT_DEVICE_CALIBRATION** | Lịch sử hiệu chuẩn cảm biến (offset, scale factor) — đảm bảo độ chính xác đo. |
| **IOT_FIRMWARE_RELEASE** | Danh mục các bản firmware phát hành (version, channel, file, checksum). |
| **IOT_FIRMWARE_UPDATE_LOG** | Lịch sử cập nhật firmware của từng thiết bị (from→to version, trạng thái). |

---

## (H) Energy Analytics — Phân tích năng lượng

Biến telemetry thô thành chỉ số kinh doanh: năng lượng, tiền tiết kiệm, CO2.

| Bảng | Tác dụng |
|------|----------|
| **ENERGY_SESSION** | Một phiên sạc/xả liên tục (năng lượng kWh, SOC đầu/cuối, công suất đỉnh). |
| **BATTERY_CYCLE_LOG** | Một chu kỳ sạc-xả hoàn chỉnh. Tính DOD, hiệu suất round-trip, StressScore — feed vào AI dự đoán SOH. |
| **ENERGY_DAILY_SUMMARY** | Tổng hợp năng lượng theo ngày/asset (kWh sạc-xả, tiền tiết kiệm, CO2 giảm). |
| **SITE_ENERGY_SUMMARY** | Tổng hợp năng lượng theo ngày/site (rollup từ asset). |
| **ELECTRICITY_RATE** | Biểu giá điện theo vùng/khung giờ (peak/off-peak) — tính tiền tiết kiệm. |
| **CARBON_EMISSION_FACTOR** | Hệ số phát thải CO2 lưới điện theo vùng/năm — tính CO2 tiết kiệm. |

---

## (I) Parts Inventory — Kho linh kiện (bonus)

| Bảng | Tác dụng |
|------|----------|
| **PART** | Danh mục linh kiện thay thế (SKU, tồn kho, ngưỡng cảnh báo hết hàng). |
| **PART_TRANSACTION** | Giao dịch xuất/nhập kho linh kiện. Có thể gắn với Ticket khi linh kiện được dùng để sửa chữa. |

---

## (J) External Logical Refs — Tham chiếu service khác

Liên kết logic sang microservice khác bằng ExternalRef (ID), không phải bảng dữ liệu đầy đủ.

| Bảng | Tác dụng |
|------|----------|
| **ACCOUNT** | Tham chiếu tài khoản từ AuthService (ExternalRef). |
| **TICKET** | Tham chiếu ticket từ TicketService (ExternalRef) — alert/maintenance/part transaction trỏ tới ticket bằng ID. |
