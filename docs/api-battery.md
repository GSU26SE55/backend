# API Documentation — BatteryService

> Base URL: `http://localhost:{port}/api`
> Content-Type: `application/json`
> Response wrapper chuẩn: `CommonResponse<T>`
> **ID fields:** Tất cả `id` trong response đều là `string` (UUID dạng `"xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx"`). Entity C# dùng `Guid` nhưng serialize thành `string` trong JSON — TypeScript dùng `string` cho mọi id field.

> **Đối chiếu code 2026-08-02:** toàn bộ route, 20 enum (`AnomalyTypeEnum` 16 giá trị, `BatteryChemistryEnum`, `IotApiKeyScopeEnum` bitflag, …) và DTO **khớp với codebase**. Bổ sung 2 endpoint AI trước đây thiếu: **`GET /api/v1/anomaly-classifications`** và **`GET /api/v1/soh-predictions`** (cả hai `Admin/Manager/Staff`, xem mục AI cuối tài liệu).

> **Cập nhật 2026-08-06 — [Nhóm 14 (AI Module)](#nhóm-14--ai-module-dự-đoán-soh--phân-loại-bất-thường--kê-đơn--feedback) viết lại toàn bộ.** Mọi request/response/enum trong nhóm đó đã được **đối chiếu bằng request thật trên môi trường Docker đang chạy**, không suy từ code.
> - `SohPredictionDto` **thêm 12 field** (`healthStage`, `stageConfidence`, `isBorderline`, `sohStd`, `rulCyclesEstimate`, `aiPriority`, `riskLevel`, `actionCode`, `sohTrend`, `degradationRatePerCycle`, `cyclesToMaintenance`, `isTemperatureOod`) — **10 field nullable**, bản ghi cũ đều `null`.
> - **3 endpoint mới:** `GET /api/v1/soh-predictions/long` · `GET /api/v1/soh-predictions/batch` · `POST /api/alerts/{id}/ai-prescription`.
> - **1 endpoint bổ sung tài liệu** (đã có code từ GH-778, trước nay thiếu doc): `POST /api/alerts/{id}/prescription-feedback`.
> - `POST /api/v1/anomaly-classifications/{id}/feedback` **đổi hành vi**: nay gửi feedback ngược về AI sau khi lưu DB.
> - ⚠️ `classification` serialize **KHÔNG đồng nhất**: số ở `AnomalyClassificationDto`, chuỗi ở `BatchPredictionItemDto`.

---

## Server-side Sort (`SortBy` + `SortDir`) — cập nhật đợt này

**Mục đích:** sort **toàn bộ dataset** ở server rồi mới phân trang (thay client-side sort chỉ sort 1 page hiện tại). FE bấm header cột → gửi `SortBy`/`SortDir`.

**Tác dụng:** BE `ORDER BY <cột> <chiều>, Id ASC` **trước** `Skip/Take` → page trả về đúng thứ tự toàn cục. Không truyền `SortBy` → giữ nguyên default cũ. **Response shape KHÔNG đổi** — chỉ đổi thứ tự phần tử trong `items`.

**Request — 2 query param mới (PascalCase, đều optional):**

| Param | Type | Nullable | Default | Mô tả |
|---|---|---|---|---|
| `SortBy` | string | ✓ | field mặc định của endpoint | Whitelist per-endpoint; ngoài whitelist → field mặc định |
| `SortDir` | string | ✓ | `desc` | `asc` \| `desc`; giá trị lạ → `desc` |

> Sort theo cột **enum** = sort theo **giá trị số** của enum, không theo tên hiển thị.
> Ví dụ: `GET /api/battery-assets?PageNumber=1&PageSize=20&SortBy=installDate&SortDir=desc`

### `GET /api/battery-assets`

| `SortBy` | Sort theo | Kiểu | Nullable |
|---|---|---|---|
| `serialNumber` | số serial | string | Không |
| `batteryTypeName` | tên loại pin | string | Không |
| `customerName` | tên khách hàng (join) | string | Không (`""` nếu không có account) |
| `siteName` | tên site (join) | string | **Có** (null nếu asset chưa gán site) |
| `status` | trạng thái | enum `BatteryStatusEnum` | Không |
| `installDate` | ngày lắp | datetime | Không |
| `createdAt` *(default)* | ngày tạo | datetime | Không |

**`BatteryStatusEnum`:** `Active=1` · `Inactive=2` · `Decommissioned=3`.

### `GET /api/battery-types`

| `SortBy` | Sort theo | Kiểu | Nullable |
|---|---|---|---|
| `name` | tên loại | string | Không |
| `manufacturer` | hãng sản xuất | string | **Có** |
| `chemistry` | hoá học pin | enum `BatteryChemistryEnum` | Không |
| `nominalCapacityAh` | dung lượng danh định (Ah) | decimal | Không |
| `nominalVoltage` | điện áp danh định (V) | decimal | Không |
| `maxCycleCount` | số chu kỳ tối đa | int | Không |
| `createdAt` *(default)* | ngày tạo | datetime | Không |

**`BatteryChemistryEnum`:** `LiFePO4=1` · `Nmc=2` · `Nca=3` · `Lco=4` · `Other=99`.

### `GET /api/admin/battery/audit-logs` (áp dụng cả `GET /api/admin/alerts/audit-logs`)

Query dùng `pageNumber`/`pageSize` (mặc định 50, trần 100).

| `SortBy` | Sort theo | Kiểu | Nullable |
|---|---|---|---|
| `occurredAt` *(default)* | thời điểm xảy ra | datetime | Không |
| `actionCode` | mã action | string | Không |
| `severity` | mức độ | string | Không |
| `targetDisplay` | đối tượng target | string | **Có** |
| `actorAccountId` | account thực hiện | Guid | **Có** (null nếu system) |
| `isSuccess` | thành công? | bool | Không |

### `GET /api/admin/iot-devices`

> Dùng key phân trang **`page`** (không phải `pageNumber`). Giữ param cũ `IsDescending` (bool) — có `SortDir` thì **`SortDir` thắng**.

| `SortBy` | Sort theo | Kiểu | Nullable |
|---|---|---|---|
| `deviceCode` | mã thiết bị | string | Không |
| `displayName` | tên hiển thị | string | Không |
| `siteName` | tên site (join) | string | **Có** |
| `status` | trạng thái | enum `IotDeviceStatusEnum` | Không |
| `currentFirmwareVersion` | firmware hiện tại | string | **Có** (null nếu chưa report) |
| `lastSeenAt` | heartbeat gần nhất | datetime | **Có** (null nếu chưa từng online) |
| `createdAt` *(default)* | ngày tạo | datetime | Không |

**`IotDeviceStatusEnum`:** `Pending=1` (chưa provision) · `Active=2` (heartbeat trong 5 phút) · `Offline=3` (mất heartbeat >5 phút) · `Disabled=4` (admin revoke key) · `Decommissioned=5` (ngưng dùng).

### `GET /api/admin/iot-firmware-releases`

> Dùng key phân trang **`page`**.

| `SortBy` | Sort theo | Kiểu | Nullable |
|---|---|---|---|
| `version` | phiên bản SemVer | string | Không |
| `hardwareRevision` | revision phần cứng | string | Không |
| `channel` | kênh rollout | enum `IotFirmwareChannelEnum` | Không |
| `status` | trạng thái lifecycle (rank) | derived | Không |
| `artifactSizeBytes` | kích thước file | long | Không |
| `createdAt` *(default)* | ngày tạo | datetime | Không |

**`IotFirmwareChannelEnum`:** `Stable=1` · `Beta=2`.
**`status` (derived rank):** entity không có field status đơn — rank tính từ `IsPublished`/`IsArchived`: `Draft=0 < Published=1 < Archived=2`. Sort `status` = sort theo rank này.

### `GET /api/sites`

> **Auth (đổi đợt này):** trước đây `Admin,Manager` → nay **`Admin,Manager,Staff`** (Staff chọn site khi report sự cố thủ công, GH-145). Các thao tác mutation site vẫn Admin-only.

| `SortBy` | Sort theo | Kiểu | Nullable |
|---|---|---|---|
| `name` | tên site | string | Không |
| `customerName` | tên khách hàng (join) | string | Không (`""` nếu không có account) |
| `status` | trạng thái | enum `SiteStatusEnum` | Không |
| `batteryAssetCount` | số pin thuộc site (computed) | int | Không |
| `installDate` | ngày lắp | datetime | Không |
| `createdAt` *(default)* | ngày tạo | datetime | Không |

**`SiteStatusEnum`:** `Active=1` · `UnderMaintenance=2` · `Decommissioned=3`.

### `GET /api/sites/{id}/assets`

| `SortBy` | Sort theo | Kiểu | Nullable |
|---|---|---|---|
| `serialNumber` | số serial | string | Không |
| `batteryTypeName` | tên loại pin | string | Không |
| `status` | trạng thái | enum `BatteryStatusEnum` (như trên) | Không |
| `installDate` | ngày lắp | datetime | Không |
| `lastSensorReadingAt` | reading gần nhất | datetime | **Có** (null nếu chưa có reading) |
| `createdAt` *(default)* | ngày tạo | datetime | Không |

### `GET /api/sensor-readings/{id}/history` — cursor (Hướng B)

Whitelist `SortBy`: `time` (mặc định), `voltage`, `current`, `temperature`, `socPercent` — tất cả field non-null. Chi tiết behavior (Hướng B, validation 400 khi thiếu `from`/`to`, lọc primary source) xem section [`GET /history`](#get-apisensor-readingsbatteryassetidhistory) bên dưới.

---

## Cấu trúc Response chung

```json
{
  "isSuccess": true,
  "statusCode": 200,
  "message": "...",
  "data": { ... },
  "listErrors": null
}
```

> `listErrors` chỉ là mảng `[{ "field": "...", "detail": "..." }]` khi có lỗi validation field-level; mọi trường hợp khác (thành công, lỗi nghiệp vụ/quyền — chỉ có `message`) BE serialize thành **`null`** (`ErrorsListJsonConverter`), không bao giờ trả `[]`.

**Pagination response:**
```json
{
  "isSuccess": true,
  "data": {
    "items": [...],
    "totalItems": 100,
    "pageNumber": 1,
    "pageSize": 10
  }
}
```

**Cursor response cho time-series:**
```json
{
  "isSuccess": true,
  "data": {
    "items": [...],
    "nextCursor": "2026-05-16T08:01:40Z",
    "hasMore": true
  }
}
```

> Sensor readings là dữ liệu time-series, không dùng offset pagination và không trả `totalItems` vì full count trên TimescaleDB có thể rất tốn kém.

---

## Enums

### `BatteryStatusEnum`

| Giá trị | Int | Ý nghĩa |
|---|---|---|
| `Active` | 1 | Pin đang hoạt động bình thường, nhận sensor readings |
| `Inactive` | 2 | Pin tạm thời ngừng hoạt động (maintenance hoặc tắt máy) |
| `Decommissioned` | 3 | Pin đã ngừng sử dụng vĩnh viễn (hết vòng đời) |

### `BatteryChemistryEnum`

| Giá trị | Int | Ý nghĩa |
|---|---|---|
| `LiFePO4` | 1 | Lithium Iron Phosphate — an toàn, chu kỳ sạc cao, dùng trong hệ thống năng lượng mặt trời |
| `Nmc` | 2 | Lithium Nickel Manganese Cobalt — mật độ năng lượng cao, dùng trong xe điện |
| `Nca` | 3 | Lithium Nickel Cobalt Aluminum — mật độ năng lượng rất cao, dùng trong Tesla |
| `Lco` | 4 | Lithium Cobalt Oxide — pin điện thoại di động |
| `Other` | 99 | Loại hóa học khác chưa được phân loại |

### `WarrantyStatusEnum`

| Giá trị | Int | Ý nghĩa |
|---|---|---|
| `Active` | 1 | Pin đang trong thời gian bảo hành |
| `Expired` | 2 | Bảo hành đã hết hạn |
| `Void` | 3 | Bảo hành bị vô hiệu (do cải tạo không phép hoặc vi phạm điều khoản) |

### `AlertSeverityEnum`

| Giá trị | Int | Ý nghĩa |
|---|---|---|
| `Info` | 1 | Thông tin — bất thường nhẹ, theo dõi |
| `Warning` | 2 | Cảnh báo — cần kiểm tra sớm |
| `Critical` | 3 | Nguy hiểm — cần xử lý ngay, tạo ticket P1 |

### `AlertStatusEnum`

| Giá trị | Int | Ý nghĩa | Chuyển sang |
|---|---|---|---|
| `Open` | 1 | Cảnh báo mới phát sinh, chưa ai xử lý | → `Acknowledged` hoặc `Resolved` |
| `Acknowledged` | 2 | Đã xác nhận đã thấy, đang theo dõi/xử lý | → `Resolved` |
| `Merged` | 3 | Bị gộp vào alert khác trong dedup window (Sprint 3) | Terminal |
| `Resolved` | 4 | Đã xử lý xong | Terminal |

**State machine:**
```
Open ──→ Acknowledged ──→ Resolved
  └───────────────────→ Resolved
  └───→ Merged (Sprint 3 - AnomalyDetector)
```

### `AnomalyTypeEnum`

| Giá trị | Int | Ý nghĩa | Trigger điển hình |
|---|---|---|---|
| `Overheat` | 1 | Nhiệt độ vượt ngưỡng tối đa | `Temperature > ThresholdConfig.TemperatureMax` |
| `Overvoltage` | 2 | Điện áp vượt ngưỡng tối đa | `Voltage > ThresholdConfig.VoltageMax` |
| `Undervoltage` | 3 | Điện áp thấp hơn ngưỡng tối thiểu | `Voltage < ThresholdConfig.VoltageMin` |
| `LowSoc` | 4 | Dung lượng pin thấp nghiêm trọng | `SocPercent < ThresholdConfig.SocCriticalThreshold` |
| `RapidDischarge` | 5 | Xả nhanh bất thường | Current xả vượt ngưỡng |
| `AbnormalCharging` | 6 | Quá trình nạp bất thường | Charging current vượt `CurrentMaxCharge` |
| `DeviceOffline` | 7 | Thiết bị mất kết nối | Không nhận sensor reading trong X phút |
| `SohDegradation` | 8 | SOH giảm dưới ngưỡng (pin xuống cấp) | `SohPercent < ThresholdConfig.SohCriticalThreshold` |
| ↑ *(dùng lại)* | 8 | **Hiệu chuẩn thiết bị IoT hết hạn** | `CalibrationExpiryNotificationBackgroundService` tạo alert **cùng mã 8**. ⚠️ **Cùng `anomalyType` nhưng KHÁC hẳn nguyên nhân** — xem ghi chú ngay dưới bảng để biết cách phân biệt |
| `HighAmbientTemp` | 9 | Nhiệt độ môi trường xung quanh site vượt ngưỡng | Ambient `Temperature > AmbientThresholdConfig.HighAmbientTempCritical` |
| `HighHumidity` | 10 | Độ ẩm môi trường vượt ngưỡng | Ambient `Humidity > AmbientThresholdConfig.HighHumidityCritical` |
| `HighTempHumidityCombo` | 11 | Combo nhiệt độ cao + độ ẩm cao đồng thời | Temp ≥ `ComboTempThreshold` AND Humidity ≥ `ComboHumidityThreshold` |
| `HighInternalResistance` | 12 | Điện trở trong tăng (Tier 2 battery health) | Tính từ Voltage/Current pattern |
| `CellImbalance` | 13 | Mất cân bằng giữa các cell | Tier 2 battery health analysis |
| `EnvironmentalIncident` | 14 | Liên kết tới `EnvironmentalIncident` cấp site (smoke/fire/flood…) | Tạo từ incident raise |
| `SensorMismatch` | 15 | BMS reading lệch IoT reading vượt ngưỡng (Sprint 7) | Cross-source mismatch check — chỉ ghép cặp `Bms ↔ IotGateway`; ΔV > 0.5V hoặc ΔT > 5°C. **Nguồn `redundant` (INA226) không đo nhiệt (temp=0 cứng) → backend skip so sánh nhiệt cho cặp chứa nó** (contract firmware, NS-09 #653) |
| `Undertemp` | 16 | Nhiệt độ thấp hơn ngưỡng tối thiểu (Sprint Bonus NS-25 #665) | `Temperature < ThresholdConfig.TemperatureMin` (seed −10°C). Dưới `TemperatureMin − 5°C` → Critical, còn lại Warning. Sạc pin lithium dưới 0°C gây lithium plating (nguy hiểm thật). ⚠️ **Wire value cross-service** — FE cần mirror giá trị 16 |

> ⚠️ **`SohDegradation` (8) được DÙNG CHO HAI VIỆC KHÁC NHAU.** `anomalyType` một mình **không đủ**
> để phân biệt, và **`severity` cũng không đủ** (cả hai đều có thể là `Warning`). Dấu hiệu phân biệt
> chắc chắn là **bộ ba `thresholdValue`/`actualValue`/`unit`**:
>
> | | SOH pin thật sự tụt | Hiệu chuẩn thiết bị IoT hết hạn |
> |---|---|---|
> | Sinh bởi | `AnomalyRules` (ngưỡng) · `SohPredictionBackgroundService` (AI) | `CalibrationExpiryNotificationBackgroundService` |
> | `severity` | `Critical` (dưới `SohCriticalThreshold`) hoặc `Warning` (dưới `SohWarningThreshold`) | **luôn `Warning`** |
> | `thresholdValue` | **có** (vd `80`) | **`null`** |
> | `actualValue` | **có** (SOH % đo/dự đoán) | **`null`** |
> | `unit` | `"%"` | **`null`** |
> | `dedupWindowEndUtc` | nhánh ngưỡng: `detectedAt + DedupWindowMinutes` (`AnomalyEngineOptions`, mặc định **30 phút**) · nhánh AI: `detectedAt + 1 giờ` | `detectedAt + **7 ngày**` |
>
> **Quy tắc cho FE:** `anomalyType == 8 && actualValue == null` ⇒ đây là **cảnh báo hiệu chuẩn hết
> hạn**, không phải pin xuống cấp. Đừng hiển thị "SOH tụt còn N%" cho nhóm này — không có số nào để
> hiện.

### `EnvironmentalIncidentTypeEnum`

| Giá trị | Int | Ý nghĩa |
|---|---|---|
| `Smoke` | 1 | Phát hiện khói |
| `FireDetected` | 2 | Phát hiện cháy |
| `GasLeak` | 3 | Rò rỉ khí |
| `Flood` | 4 | Ngập nước |
| `OverheatHazard` | 5 | Nguy cơ quá nhiệt diện rộng |
| `Other` | 99 | Loại sự cố khác |

### `EnvironmentalIncidentStatusEnum`

| Giá trị | Int | Ý nghĩa | Chuyển sang |
|---|---|---|---|
| `Open` | 1 | Vừa report, chưa ai xử lý | → `Acknowledged` / `Resolved` / `FalseAlarm` |
| `Acknowledged` | 2 | Đã xác nhận, đang xử lý | → `Resolved` / `FalseAlarm` |
| `Resolved` | 3 | Đã xử lý xong (kèm `ResolutionNote`) | Terminal |
| `FalseAlarm` | 4 | Không phải incident thật (kèm `FalseAlarmReason`) | Terminal |

### `AmbientReadingSourceEnum`

| Giá trị | Int | Ý nghĩa |
|---|---|---|
| `IotSensor` | 1 | Cảm biến IoT thật tại site (vd DHT22, BME280) |
| `WeatherApi` | 2 | Dữ liệu sync từ OpenMeteo HTTP API qua `WeatherSyncBackgroundService` |

### `SensorReadingSourceTypeEnum`

> Phân loại nguồn của một sensor reading — phục vụ cross-source mismatch check (Sprint 7, anomaly `SensorMismatch`).

| Giá trị | Int | Ý nghĩa |
|---|---|---|
| `Bms` | 1 | Reading từ BMS gắn trực tiếp trong pack (qua RS485/Modbus) |
| `IotGateway` | 2 | Reading từ IoT edge device (ESP32-S3 + sensor ngoài). Tên giữ legacy "Gateway" — **giá trị mặc định** của `POST /batch` |
| `External` | 3 | Manual import / third-party feed |

### `ChargingStateEnum`

| Giá trị | Int | Ý nghĩa | Current convention |
|---|---|---|---|
| `Idle` | 1 | Không nạp/xả | Current ≈ 0 |
| `Charging` | 2 | Đang nạp điện | Current > 0 (BMS convention) |
| `Discharging` | 3 | Đang xả điện | Current < 0 |
| `Float` | 4 | Float charge — giữ điện áp đầy | Current rất nhỏ dương |
| `Bypass` | 5 | Bypass mode — dùng nguồn lưới trực tiếp | Pin không tham gia mạch |

### `SiteStatusEnum`

| Giá trị | Int | Ý nghĩa |
|---|---|---|
| `Active` | 1 | Site đang hoạt động bình thường |
| `UnderMaintenance` | 2 | Site đang trong thời gian bảo trì |
| `Decommissioned` | 3 | Site đã ngừng hoạt động |

### `IotDeviceStatusEnum`

> Trạng thái vòng đời của một IoT edge device (xem Nhóm 11).

| Giá trị | Int | Ý nghĩa |
|---|---|---|
| `Pending` | 1 | Đã tạo nhưng chưa provision (chưa bind firmware/credential) |
| `Active` | 2 | Đã provision và còn heartbeat trong threshold |
| `Offline` | 3 | Mất heartbeat quá threshold — đánh dấu bởi background job |
| `Disabled` | 4 | Bị admin vô hiệu hóa (API key revoked, không nhận ingest) |
| `Decommissioned` | 5 | Đã ngừng sử dụng vĩnh viễn |

### `IotApiKeyScopeEnum`

> Bitmask `[Flags]` — một API key per-device có thể mang nhiều scope đồng thời.

| Giá trị | Int | Ý nghĩa |
|---|---|---|
| `None` | 0 | Không có scope |
| `SensorIngest` | 1 | Cho phép `POST /api/sensor-readings/batch` |
| `DeviceHeartbeat` | 2 | Cho phép `provision` + `heartbeat` |
| `EnvironmentalIngest` | 4 | Cho phép ingest ambient + report environmental incident |
| `FirmwareCheck` | 8 | Cho phép `firmware-check` + báo cáo update log |
| `EdgeDeviceDefault` | 11 | Bundle mặc định = `SensorIngest \| DeviceHeartbeat \| FirmwareCheck` (1+2+8) |

### `IotFirmwareUpdateStatusEnum`

| Giá trị | Int | Ý nghĩa |
|---|---|---|
| `Pending` | 1 | Đã ra lệnh, device chưa báo kết quả |
| `Downloading` | 2 | Device đang download artifact |
| `Installing` | 3 | Device đang flash |
| `Success` | 4 | Update thành công, đã reboot |
| `Failed` | 5 | Update fail (CRC/checksum/rollback) |
| `Skipped` | 6 | Device không đủ điều kiện (vd battery thấp) → bỏ qua round này |
| `RolledBack` | 7 | Flash xong nhưng fail boot → rollback về bản trước |

### `IotFirmwareChannelEnum`

| Giá trị | Int | Ý nghĩa |
|---|---|---|
| `Stable` | 1 | Kênh phát hành ổn định |
| `Beta` | 2 | Kênh thử nghiệm |

### `ElectricalTopologyEnum` (Sprint 7 B4)

Cách đấu nối điện của pin trong site — dùng để tính **cascade risk** (rủi ro 1 pin hỏng lan sang pin lân cận). Topology càng "dính" nhau thì hệ số rủi ro lan truyền càng cao.

| Giá trị | Int | Ý nghĩa | Hệ số rủi ro (Rule 1) |
|---|---|---|---|
| `Independent` | 1 | Pin đơn lẻ, không kết nối điện với pin khác — hỏng không lây | +0.0 |
| `SeriesString` | 2 | Mắc nối tiếp (string voltage) — mất 1 pin có thể ngắt cả chuỗi | +0.6 |
| `ParallelBank` | 3 | Mắc song song (bank capacity) — 1 pin hỏng làm tăng tải pin còn lại | +0.2 |
| `SeriesParallel` | 4 | Hỗn hợp nối tiếp + song song | +0.4 |

### `CascadeRiskLevel` (Sprint 7 B4)

Mức rủi ro lan truyền, **derive từ `cascadeRiskScore`** (0.0–1.0).

| Giá trị | Int | Ngưỡng score | Ý nghĩa |
|---|---|---|---|
| `Low` | 1 | `< 0.5` | Rủi ro lan truyền thấp |
| `Medium` | 2 | `0.5 – < 0.7` | Rủi ro trung bình — Manager review (không auto-upgrade) |
| `High` | 3 | `>= 0.7` | Rủi ro cao — publish `BatteryCascadeRiskHighEvent` → TicketService auto-upgrade Priority ticket liên quan lên P1 |

---

## Nhóm 1 — Alerts (Cảnh báo)

Base route: `/api/alerts`

**Phân quyền:**
- List/Detail: tất cả role đăng nhập
- Acknowledge: tất cả role (Customer có thể ack cảnh báo của mình)
- Resolve: chỉ Admin/Manager/Staff

---

### `GET /api/alerts`

**Mục đích:** Danh sách cảnh báo có phân trang và lọc.

**Auth:** Bắt buộc (mọi role)

**Query params:**

| Param | Type | Bắt buộc | Mô tả |
|---|---|---|---|
| `pageNumber` | `int` | Không (mặc định 1) | Số trang |
| `pageSize` | `int` | Không (mặc định 10) | Số item mỗi trang |
| `batteryAssetId` | `string?` | Không | Lọc alert của một asset cụ thể (UUID string) |
| `severity` | `AlertSeverityEnum?` | Không | Lọc theo mức độ nghiêm trọng (`Info`/`Warning`/`Critical`) |
| `status` | `AlertStatusEnum?` | Không | Lọc theo trạng thái; nếu truyền thì `excludeMerged` bị bỏ qua |
| `excludeMerged` | `bool` | Không (mặc định `true`) | Loại trừ alert có `status = Merged` — mặc định `true` nên FE chỉ thấy alert gốc. Truyền `false` để xem tất cả kể cả Merged (dành cho debug/admin) |
| `from` | `DateTime?` | Không | Lọc từ thời điểm phát sinh (UTC) |
| `to` | `DateTime?` | Không | Lọc đến thời điểm phát sinh (UTC) |

**Response thành công `200`:** `PaginationResponse<AlertDto>`

**Chi tiết `AlertDto`:**

| Field | Type | Nullable | Mô tả |
|---|---|---|---|
| `id` | `string` | Không | ID cảnh báo |
| `batteryAssetId` | `string` | Không (rỗng `""` cho alert cấp site) | ID battery asset phát sinh cảnh báo. **Chuỗi rỗng `""`** cho alert **cấp SITE** (ambient `HighAmbientTemp`/`HighHumidity`/`HighTempHumidityCombo` = 9/10/11, hoặc `EnvironmentalIncident` = 14) — khi đó dùng `siteId` |
| `siteId` | `string?` | **Null** cho alert cấp pin thông thường | **Sprint Bonus NS-21 (#661)** — ID Site cho alert cấp site (ambient/environmental incident). Giúp FE route alert về đúng site. `null` với alert gắn 1 pin cụ thể |
| `batterySerialNumber` | `string` | Không (rỗng cho alert cấp site) | Serial number của pin (để hiển thị) — `""` cho alert cấp site (không gắn pin) |
| `anomalyType` | `AnomalyTypeEnum` | Không | Loại bất thường (xem enum) |
| `severity` | `AlertSeverityEnum` | Không | Mức độ nghiêm trọng (xem enum) |
| `thresholdValue` | `decimal?` | Null nếu anomaly không gắn ngưỡng cụ thể | Giá trị ngưỡng đã cấu hình |
| `actualValue` | `decimal?` | Null nếu anomaly không gắn giá trị đo | Giá trị thực tế tại thời điểm phát sinh |
| `unit` | `string?` | Null nếu không áp dụng đơn vị | Đơn vị đo (e.g., `V`, `°C`, `%`) |
| `detectedAt` | `DateTime` | Không | Thời điểm phát hiện bất thường (UTC) |
| `status` | `AlertStatusEnum` | Không | Trạng thái hiện tại (xem enum) |
| `ticketId` | `string?` | Null nếu chưa có ticket | ID ticket liên kết (nếu đã auto-tạo hoặc link thủ công) |
| `acknowledgedByUserId` | `string?` | Null nếu chưa ack | ID user đã acknowledge |
| `acknowledgedAt` | `DateTime?` | Null nếu chưa ack | Thời điểm acknowledge (UTC) |
| `resolvedAt` | `DateTime?` | Null nếu chưa resolve | Thời điểm resolve (UTC) |
| `dedupWindowEndUtc` | `DateTime` | Không | Thời điểm kết thúc cửa sổ deduplication (Sprint 3) — luôn được set cho mọi alert |
| `createdAt` | `DateTime` | Không | Thời điểm tạo record (UTC) |

### Logic Deduplication (Sprint 3)

- Window mặc định: `30` phút, cấu hình qua `AnomalyEngine:DedupWindowMinutes`.
- Điều kiện merge: cùng `batteryAssetId` + cùng `anomalyType`, alert gốc đang `Open` hoặc `Acknowledged`, và `dedupWindowEndUtc > now`.
- Khi phát hiện anomaly trùng trong window, hệ thống tạo record alert mới với `status = Merged`, `mergedIntoAlertId = id alert gốc`, `dedupWindowEndUtc = dedupWindowEndUtc của alert gốc`.
- Alert gốc chưa bị merge vẫn có `dedupWindowEndUtc = detectedAt + dedupWindow`.
- `GET /api/alerts` mặc định loại trừ `Merged` (`excludeMerged=true`). Để xem Merged alerts, truyền `excludeMerged=false` hoặc `status=Merged`.
- `assetsWithActiveAlerts` trong Site dashboard chỉ tính alert `Open` và `Acknowledged`, không tính `Merged` hoặc `Resolved`.

### Thông báo cho Customer theo mức nghiêm trọng (Sprint 6.2 NOTI-08 · #679)

Từ Sprint 6.2, **cả 3 mức severity đều sinh thông báo tới Customer sở hữu pin** — trước đó
BatteryService **cố ý chỉ publish event cho mức `Critical`** để tránh spam ticket, nên
`Warning`/`Info` chỉ nằm im trong bảng `alerts` và Customer **không bao giờ được báo** (spec §3.4
T#11/T#12 chưa đạt).

| Severity | Event publish (qua outbox) | Ai consume | Có tạo ticket không? | Kênh Customer nhận |
|---|---|---|---|---|
| `Critical` (3) | `BatteryAnomalyDetectedEvent` | TicketService (auto-tạo ticket BR-02) **+** NotificationService | ✅ **Có** | InApp + Push + **Email + SMS** (Sprint 6.2 mở đủ 4 kênh; trước chỉ InApp + Push) |
| `Warning` (2) | **`BatteryAnomalyWarningDetectedEvent`** *(MỚI)* | **CHỈ** NotificationService | ❌ Không | InApp + Push |
| `Info` (1) | **`BatteryAnomalyWarningDetectedEvent`** *(MỚI)* | **CHỈ** NotificationService | ❌ Không | **Chỉ InApp** |

> **Vì sao là event RIÊNG chứ không tái dùng `BatteryAnomalyDetectedEvent`:** TicketService consume
> `BatteryAnomalyDetectedEvent` để auto-tạo ticket (BR-02) và `AlertTicketSagaStateMachine` consume
> bản V2 cho cùng mục đích. Publish severity Warning lên chính event đó thì **mọi cảnh báo nhẹ sẽ đẻ
> ticket** — đúng nỗi lo "spam ticket" mà team đang né bằng cách không publish gì cả.

**Payload `BatteryAnomalyWarningDetectedEvent`:**

| Field | Type | Nullable | Mô tả |
|---|---|---|---|
| `alertId` | `Guid` | Không | ID alert vừa tạo |
| `batteryAssetId` | `Guid?` | **Có** | ID pin. `null`/`Guid.Empty` ⇒ **không publish** (xem dedup dưới) |
| `customerId` | `Guid` | Không | Chủ sở hữu pin — consumer skip nếu rỗng |
| `assetSerialNumber` | `string?` | **Có** | Serial để hiển thị; rỗng → consumer hiện `"(không rõ serial)"` |
| `anomalyType` | `int` | Không | Giá trị `AnomalyTypeEnum` |
| `severity` | `int` | Không | Giá trị `AlertSeverityEnum` — chỉ `1` (Info) hoặc `2` (Warning) |
| `thresholdValue` | `decimal?` | **Có** | Ngưỡng đã cấu hình |
| `actualValue` | `decimal?` | **Có** | Giá trị thực tế |
| `unit` | `string?` | **Có** | Đơn vị (`V`, `°C`, `%`, …) |
| `detectedAt` | `DateTime` | Không | UTC |

**Chống spam — dedup ở phía publisher:** alert Warning **không** đi qua dedup-merge như Critical (mỗi
tick vượt ngưỡng có thể tạo alert mới), nên publish thẳng sẽ khiến Customer lãnh một trận thông báo.
Chỉ publish khi trong `WarningNotifyDedupMinutes` gần nhất **chưa có** outbox event cùng
`(BatteryAssetId × AnomalyType)`. Alert **không gắn pin** (`batteryAssetId` null/rỗng — vd alert cấp
site) **không bao giờ được publish** event này.

**Config (section `AnomalyEngine`):**

| Khoá | Kiểu | Mặc định | Ý nghĩa |
|---|---|---|---|
| `AnomalyEngine:PublishWarningNotifications` | `bool` | `true` | `false` = quay lại hành vi cũ: chỉ ghi alert, **không báo ai** |
| `AnomalyEngine:WarningNotifyDedupMinutes` | `int` | `60` | Cửa sổ chống spam theo `(BatteryAssetId × AnomalyType)`. `≤ 0` = tắt dedup (publish mọi lần) |

> ⚠️ **Ghi chú cho người đọc code:** XML comment trong `BatteryAnomalyWarningDetectedEvent.cs` viết
> khoá là `Anomaly:WarningNotifyDedupMinutes`. Đó là **sai trong comment** — `AnomalyEngineOptions.SectionName`
> thực tế là **`AnomalyEngine`**, nên khoá đúng là `AnomalyEngine:WarningNotifyDedupMinutes`
> (biến môi trường: `AnomalyEngine__WarningNotifyDedupMinutes`).

**Outbox `EventTypeMap`:** `OutboxRelayService` đã đăng ký
`{ nameof(BatteryAnomalyWarningDetectedEvent), typeof(BatteryAnomalyWarningDetectedEvent) }`.
Thiếu dòng này thì relay báo `"Unknown event type"` và event **nằm lại outbox mãi mãi** — bẫy cố hữu
khi thêm event mới cho BatteryService.

> Chi tiết phía nhận (template, preference theo nhóm `Battery`, quiet hours, hạn mức):
> xem [`api-notification.md`](api-notification.md).

---

### `GET /api/alerts/{id}`

**Mục đích:** Xem chi tiết một cảnh báo.

**Auth:** Bắt buộc (mọi role)

**Path param:** `id` — Guid của alert

**Response thành công `200`:** `CommonResponse<AlertDto>`

---

### `PATCH /api/alerts/{id}/acknowledge`

**Mục đích:** Xác nhận đã thấy cảnh báo. Chuyển trạng thái `Open` → `Acknowledged`.

**Auth:** Bắt buộc (mọi role, bao gồm Customer)

**Path param:** `id` — Guid của alert

**Request body:** Không có

**Response thành công `200`:** `isSuccess = true`

**Lỗi thường gặp:**
- `404` — Alert không tìm thấy
- `409 isSuccess=false` — Alert đang ở trạng thái `Resolved` hoặc `Merged`; không thể acknowledge

---

### `PATCH /api/alerts/{id}/resolve`

**Mục đích:** Đánh dấu cảnh báo đã được xử lý xong. Chuyển trạng thái → `Resolved`.

**Auth:** Bắt buộc (chỉ Admin/Manager/Staff — Customer không được phép)

**Path param:** `id` — Guid của alert

**Request body:** Không có

**Response thành công `200`:** `isSuccess = true`

**State transition:** `Open → Resolved` hợp lệ, không bắt buộc phải qua `Acknowledged`. `Acknowledged → Resolved` cũng hợp lệ.

**Lỗi thường gặp:**
- `404` — Alert không tìm thấy hoặc đã bị soft-delete
- `409 isSuccess=false` — Alert đang ở trạng thái `Merged`; phải resolve alert gốc thay vì alert đã merge

---

### Hai endpoint AI trên alert — tài liệu ở Nhóm 14

Cùng base route `/api/alerts/{id}` nhưng thuộc luồng AI, nên tài liệu đầy đủ (body, enum, nullable, mã lỗi)
đặt chung ở [Nhóm 14](#nhóm-14--ai-module-dự-đoán-soh--phân-loại-bất-thường--kê-đơn--feedback):

| Endpoint | Role | Tóm tắt |
|---|---|---|
| `POST /api/alerts/{id}/ai-prescription` | Admin/Manager/Staff | Kê lại đơn xử lý cho alert ở chế độ đầy đủ; gắn `aiPrescriptionId` mới lên alert |
| `POST /api/alerts/{id}/prescription-feedback` | Admin/Manager/Staff/Customer | Phản hồi `accepted`/`edited`/`rejected` về đơn AI đã đưa ra |

> Thứ tự bắt buộc: chưa có `aiPrescriptionId` trên alert thì `prescription-feedback` trả `409`. Gọi
> `ai-prescription` trước để sinh id.

---

## Nhóm 2 — Battery Assets (Tài sản Pin)

Base route: `/api/battery-assets`

---

### `GET /api/battery-assets`

**Mục đích:** Danh sách battery asset với phân trang và lọc (dành cho Admin/Manager).

**Auth:** Bắt buộc (Admin/Manager)

**Query params:**

| Param | Type | Bắt buộc | Mô tả |
|---|---|---|---|
| `pageNumber` | `int` | Không (mặc định 1) | Số trang |
| `pageSize` | `int` | Không (mặc định 10) | Số item mỗi trang |
| `keyword` | `string?` | Không | Tìm theo serial number hoặc location |
| `customerId` | `string?` | Không | Lọc theo khách hàng (UUID string) |
| `batteryTypeId` | `string?` | Không | Lọc theo loại pin (UUID string) |
| `siteId` | `string?` | Không | Lọc theo site (UUID string) |
| `status` | `BatteryStatusEnum?` | Không | Lọc theo trạng thái |
| `includeDeleted` | `bool` | Không (mặc định `false`) | Bao gồm cả asset đã soft-delete |
| `SortBy` | `string?` | Không | Cột sort server-side. Whitelist: `serialNumber`, `batteryTypeName`, `customerName`, `siteName`, `status`, `installDate`. Ngoài whitelist → `createdAt` |
| `SortDir` | `string?` | Không (mặc định `desc`) | `asc` \| `desc`; giá trị lạ → `desc` |

> **Sắp xếp:** mặc định `createdAt` desc. **Đã hỗ trợ sort server-side** (đợt này) qua `SortBy`/`SortDir` — order toàn dataset **trước** khi phân trang, kèm tie-breaker `Id ASC`. Chi tiết whitelist + kiểu enum + field nullable: xem section **Server-side Sort** ở đầu tài liệu.

**Response thành công `200`:** `PaginationResponse<BatteryAssetDto>`

**Chi tiết `BatteryAssetDto`:**

| Field | Type | Nullable | Mô tả |
|---|---|---|---|
| `id` | `string` | Không | ID battery asset |
| `serialNumber` | `string` | Không | Serial number duy nhất của pin (chữ in hoa, số, dấu gạch ngang, 5–64 ký tự) |
| `batteryTypeId` | `string` | Không | ID loại pin |
| `batteryTypeName` | `string` | Không | Tên loại pin (để hiển thị) |
| `siteId` | `string?` | Null nếu chưa gán site | ID site lắp đặt |
| `siteName` | `string?` | Null nếu chưa gán site | Tên site |
| `customerId` | `string` | Không | ID khách hàng sở hữu |
| `customerName` | `string` | Không | Tên khách hàng từ `CustomerAccount` read model |
| `installDate` | `DateTime` | Không | Ngày lắp đặt (UTC) |
| `warrantyEndDate` | `DateTime?` | Null nếu không có bảo hành | Ngày hết bảo hành (UTC) |
| `warrantyStatus` | `WarrantyStatusEnum` | Không | Trạng thái bảo hành (xem enum) |
| `location` | `string?` | Null nếu không có mô tả vị trí | Mô tả vị trí lắp đặt (text) |
| `latitude` | `decimal?` | Null nếu không có tọa độ | Vĩ độ (-90 đến 90) |
| `longitude` | `decimal?` | Null nếu không có tọa độ | Kinh độ (-180 đến 180) |
| `status` | `BatteryStatusEnum` | Không | Trạng thái hoạt động của pin |
| `notes` | `string?` | Null nếu không có | Ghi chú |
| `lastSensorReadingAt` | `DateTime?` | Null nếu chưa nhận reading nào | Thời điểm nhận sensor reading gần nhất — stored trên `BatteryAsset` entity, được cập nhật real-time mỗi khi `POST /api/sensor-readings/batch` ingest thành công. Có DB index, không query sang TimescaleDB khi GET list. |
| `createdAt` | `DateTime` | Không | Thời điểm tạo record (UTC) |

---

### `GET /api/battery-assets/me`

**Mục đích:** Danh sách battery asset của Customer đang đăng nhập.

**Auth:** Bắt buộc (Customer)

**Query params:** `pageNumber`, `pageSize`

**Response thành công `200`:** `PaginationResponse<BatteryAssetDto>`

---

### `GET /api/battery-assets/{id}`

**Mục đích:** Xem chi tiết một battery asset.

**Auth:** Bắt buộc (Admin/Manager/Staff/Customer)

**Response thành công `200`:** `CommonResponse<BatteryAssetDto>`

---

### `GET /api/battery-assets/{id}/realtime`

**Mục đích:** Lấy snapshot realtime của pin: trạng thái hiện tại + sensor reading mới nhất + số alert đang mở.

**Auth:** Bắt buộc (Admin/Manager/Staff/Customer)

**Path param:** `id` — Guid của battery asset

**Response thành công `200`:**
```json
{
  "isSuccess": true,
  "data": {
    "assetId": "guid",
    "serialNumber": "BAT-001",
    "status": 1,
    "time": "2026-05-16T08:00:00Z",
    "voltage": 52.3,
    "current": -2.5,
    "temperature": 28.4,
    "socPercent": 78.5,
    "cycleCount": 234,
    "sohPercent": 92.1,
    "chargingState": 3,
    "activeAlerts": 0
  }
}
```

**Chi tiết `BatteryAssetRealtimeDto`:**

| Field | Type | Nullable | Mô tả |
|---|---|---|---|
| `assetId` | `string` | Không | ID battery asset |
| `serialNumber` | `string` | Không | Serial number |
| `status` | `BatteryStatusEnum` | Không | Trạng thái hoạt động |
| `time` | `DateTime?` | Null nếu chưa có reading | Timestamp của reading gần nhất (UTC) |
| `voltage` | `decimal?` | Null nếu chưa có reading | Điện áp (V) |
| `current` | `decimal?` | Null nếu chưa có reading | Dòng điện (A) — âm = đang xả |
| `temperature` | `decimal?` | Null nếu chưa có reading | Nhiệt độ (°C) |
| `socPercent` | `decimal?` | Null nếu chưa có reading | State of Charge (%) |
| `cycleCount` | `int?` | Null nếu BMS không report | Số chu kỳ sạc/xả |
| `sohPercent` | `decimal?` | Null nếu AI chưa tính toán | State of Health (%) từ AI module |
| `chargingState` | `ChargingStateEnum?` | Null nếu BMS không report | Trạng thái nạp/xả (xem enum) |
| `activeAlerts` | `int` | Không | Số alert đang ở trạng thái Open/Acknowledged |

---

### `POST /api/admin/battery-assets`

**Mục đích:** Tạo battery asset mới (đăng ký pin vào hệ thống).

**Auth:** Bắt buộc (Admin)

**Request body:**

| Field | Type | Bắt buộc | Validation | Mô tả |
|---|---|---|---|---|
| `serialNumber` | `string` | **Bắt buộc** | 5–64 ký tự, regex `^[A-Z0-9-]+$`. Hệ thống tự `Trim().ToUpperInvariant()` trước khi check trùng | Serial number duy nhất |
| `batteryTypeId` | `string` | **Bắt buộc** | UUID hợp lệ, phải tồn tại trong DB (BatteryType chưa xóa) | Loại pin |
| `customerId` | `string` | **Bắt buộc** | UUID hợp lệ, phải tồn tại trong `CustomerAccount` (read-model sync từ AuthService) và `IsActive = true` | Khách hàng sở hữu |
| `siteId` | `string?` | Không | UUID hợp lệ nếu truyền. Nếu có, Site phải tồn tại, chưa xóa và thuộc cùng `customerId` | Site lắp đặt |
| `installDate` | `DateTime` | **Bắt buộc** | Không ở tương lai và không cũ hơn 5 năm | Ngày lắp đặt |
| `warrantyEndDate` | `DateTime?` | Không | Phải sau `installDate` nếu truyền. Nếu đã qua hiện tại → `warrantyStatus` tự set `Expired`, ngược lại `Active` | Ngày hết bảo hành |
| `location` | `string?` | Không | Max 255 ký tự | Mô tả vị trí |
| `latitude` | `decimal?` | Không | -90 đến 90 | Vĩ độ |
| `longitude` | `decimal?` | Không | -180 đến 180 | Kinh độ |
| `notes` | `string?` | Không | Max 1000 ký tự | Ghi chú |

**Cách hoạt động:**
- Validate đầu vào (gom toàn bộ lỗi → `400`).
- Check customer active → check trùng serial → check BatteryType → check Site → validate relation.
- Tạo asset với `Status = Active` (muốn đổi sang Inactive/Decommissioned phải dùng PUT). Lưu xuống DB.

**Response thành công `201`:** `CommonResponse<BatteryAssetDto>`

**Lỗi thường gặp:**
- `400` — Validation field-level lỗi (xem `listErrors`)
- `422` — Lỗi cross-field business rule, ví dụ `warrantyEndDate` không sau `installDate`
- `404` — Không tìm thấy Customer / BatteryType / Site được tham chiếu
- `409` — Serial number đã tồn tại trong hệ thống
- `409` — Vi phạm ràng buộc Site/BatteryType (ví dụ `siteId` không thuộc `customerId` đã truyền)

---

### `PUT /api/admin/battery-assets/{id}`

**Mục đích:** Cập nhật thông tin battery asset.

**Auth:** Bắt buộc (Admin)

**Path param:** `id` — Guid của asset

**Request body:** Giống POST, thêm:

| Field | Type | Bắt buộc | Mô tả |
|---|---|---|---|
| `warrantyStatus` | `WarrantyStatusEnum` | Không (mặc định `Active`) | Trạng thái bảo hành |
| `status` | `BatteryStatusEnum` | Không (mặc định `Active`) | Trạng thái hoạt động |

> **Lưu ý `warrantyStatus = Void`:** Đặt trạng thái bảo hành thành `Void` (vô hiệu) không yêu cầu trường `voidReason` trong scope capstone. Admin thực hiện trực tiếp qua endpoint này mà không cần xác nhận thêm.

**Response thành công `200`:** `CommonResponse<BatteryAssetDto>`

---

### `DELETE /api/admin/battery-assets/{id}`

**Mục đích:** Xóa mềm (soft delete) battery asset.

**Auth:** Bắt buộc (Admin)

**Response thành công `200`:** `isSuccess = true`

---

### `PATCH /api/admin/battery-assets/{id}/restore`

**Mục đích:** Khôi phục battery asset đã bị soft-delete.

**Auth:** Bắt buộc (Admin)

**Response thành công `200`:** `isSuccess = true`

---

### `PUT /api/admin/battery-assets/{id}/transfer-owner`

**Mục đích:** Chuyển quyền sở hữu battery asset sang khách hàng khác.

**Auth:** Bắt buộc (Admin)

**Request body:**

| Field | Type | Bắt buộc | Validation | Mô tả |
|---|---|---|---|---|
| `newCustomerId` | `string` | **Bắt buộc** | UUID hợp lệ, khác `00000000-0000-...` | ID khách hàng mới |
| `reason` | `string?` | Không | Max 500 ký tự | Lý do chuyển chủ sở hữu |

**Response thành công `200`:** `isSuccess = true`

---

## Nhóm 3 — Battery Types (Loại Pin)

Base route: `/api/battery-types`

---

### `GET /api/battery-types`

**Mục đích:** Danh sách loại pin với phân trang.

**Auth:** Bắt buộc (Admin/Manager/Staff)

**Query params:**

| Param | Type | Mô tả |
|---|---|---|
| `pageNumber` | `int` | Trang |
| `pageSize` | `int` | Số item/trang |
| `keyword` | `string?` | Tìm theo tên loại pin **hoặc nhà sản xuất** (`Name` + `Manufacturer`, case-insensitive) |
| `includeDeleted` | `bool` | Bao gồm đã xóa (mặc định `false`) |
| `chemistry` | `BatteryChemistryEnum?` | Lọc theo hóa học pin (`1` LiFePO4, `2` Nmc, `3` Nca, `4` Lco, `99` Other). Không truyền = không lọc |
| `SortBy` | `string?` | Cột sort server-side. Whitelist: `name`, `manufacturer`, `chemistry`, `nominalCapacityAh`, `nominalVoltage`, `maxCycleCount`. Ngoài whitelist → `createdAt` |
| `SortDir` | `string?` | `asc` \| `desc` (mặc định `desc`; giá trị lạ → `desc`) |

> **Sắp xếp:** mặc định `createdAt` desc. Đã hỗ trợ sort server-side qua `SortBy`/`SortDir` (order toàn dataset trước phân trang, tie-breaker `Id ASC`). Chi tiết enum `BatteryChemistryEnum` + nullable: xem **Server-side Sort** đầu tài liệu.

**Response thành công `200`:** `PaginationResponse<BatteryTypeDto>`

**Chi tiết `BatteryTypeDto`:**

| Field | Type | Nullable | Mô tả |
|---|---|---|---|
| `id` | `string` | Không | ID loại pin (UUID dạng string) |
| `name` | `string` | Không | Tên model (e.g., `LiFePO4-200Ah-48V`) |
| `manufacturer` | `string?` | Null nếu chưa cung cấp | Nhà sản xuất |
| `nominalCapacityAh` | `decimal` | Không | Dung lượng danh định (Ah) |
| `nominalVoltage` | `decimal` | Không | Điện áp danh định (V) |
| `chemistry` | `BatteryChemistryEnum` | Không | Loại hóa học (xem enum) |
| `maxCycleCount` | `int` | Không | Số chu kỳ sạc/xả tối đa theo thiết kế (mặc định 2000) |
| `description` | `string?` | Null nếu không có | Mô tả |
| `createdAt` | `DateTime` | Không | Thời điểm tạo (UTC) |

---

### `GET /api/battery-types/{id}`

**Auth:** Bắt buộc (Admin/Manager/Staff)

**Response thành công `200`:** `CommonResponse<BatteryTypeDto>`

---

### `POST /api/admin/battery-types`

**Auth:** Bắt buộc (Admin)

**Request body:**

| Field | Type | Bắt buộc | Validation | Mô tả |
|---|---|---|---|---|
| `name` | `string` | **Bắt buộc** | Max 100 ký tự, duy nhất (case-insensitive, đã trim) trong các BatteryType chưa xóa | Tên model |
| `manufacturer` | `string?` | Không | Max 100 ký tự | Nhà sản xuất |
| `nominalCapacityAh` | `decimal` | **Bắt buộc** | > 0 | Dung lượng danh định (Ah) |
| `nominalVoltage` | `decimal` | **Bắt buộc** | > 0 | Điện áp danh định (V) |
| `chemistry` | `BatteryChemistryEnum` | Không (mặc định `LiFePO4`) | enum hợp lệ | Loại hóa học |
| `maxCycleCount` | `int` | Không (mặc định `2000`) | > 0 | Số chu kỳ sạc/xả tối đa. Là `int` có default 2000 — không gửi sẽ dùng 2000 (không bắt buộc client truyền) |
| `description` | `string?` | Không | Max 500 ký tự | Mô tả |

**Cách hoạt động:**
- Validate tất cả field qua `ValidateAsync`; mọi lỗi gom vào `ListErrors` rồi trả `400`.
- Kiểm tra trùng tên (case-insensitive, đã trim) trong các BatteryType chưa xóa; nếu trùng trả `409`.
- Sinh `Id = Guid.NewGuid()` và persist. `CreatedAt`/`CreatedBy` được `AuditableEntityInterceptor` tự set.

> **Lưu ý:** Sau khi tạo BatteryType, Admin nên gọi tiếp `PUT /api/admin/thresholds/by-type/{id}` để cấu hình ngưỡng cảnh báo cho loại pin này.

**Response thành công `201`:** `CommonResponse<BatteryTypeDto>`

**Lỗi thường gặp:**
- `400` — Validation field lỗi (xem `listErrors`)
- `409` — Tên loại pin đã tồn tại trong hệ thống

> **Lưu ý:** Endpoint create trả HTTP `201 Created`; `CommonResponse.statusCode` trong body khớp HTTP status thật. FE nên kiểm tra `isSuccess` để xác định thành công.

---

### `PUT /api/admin/battery-types/{id}`

**Auth:** Bắt buộc (Admin)

**Path param:** `id` — Guid của loại pin.

**Request body:**

| Field | Type | Bắt buộc | Validation | Mô tả |
|---|---|---|---|---|
| `name` | `string` | **Bắt buộc** | Max 100 ký tự, unique | Tên model |
| `manufacturer` | `string?` | Không | Max 100 ký tự | Nhà sản xuất |
| `nominalCapacityAh` | `decimal` | **Bắt buộc** | > 0 | Dung lượng danh định (Ah) |
| `nominalVoltage` | `decimal` | **Bắt buộc** | > 0 | Điện áp danh định (V) |
| `chemistry` | `BatteryChemistryEnum` | Không | — | Loại hóa học |
| `maxCycleCount` | `int` | Không | > 0 | Số chu kỳ tối đa |
| `description` | `string?` | Không | Max 500 ký tự | Mô tả |

**Response thành công `200`:** `CommonResponse<BatteryTypeDto>`

**Lưu ý:** Đây là PUT full update. `nominalCapacityAh` và `nominalVoltage` là bắt buộc — không gửi hoặc gửi `0` sẽ nhận `400`. Các field optional (`manufacturer`, `description`) không gửi sẽ bị reset về `null`.

---

### `DELETE /api/admin/battery-types/{id}`

Soft delete loại pin.

**Auth:** Bắt buộc (Admin)

**Response thành công `200`:** `isSuccess = true`

**Lỗi thường gặp:**
- `404` — Không tìm thấy loại pin
- `409` — Loại pin đang được gán cho tài sản pin. Phải cập nhật hoặc xóa các asset trước khi xóa loại pin.

> **Lưu ý:** Code hiện tại chỉ kiểm tra asset (`BatteryAssets`) — chỉ asset mới chặn xóa.

---

### `PATCH /api/admin/battery-types/{id}/restore`

Khôi phục loại pin đã xóa.

**Auth:** Bắt buộc (Admin)

---

## Nhóm 4 — Sensor Readings (Dữ liệu Cảm biến)

Base route: `/api/sensor-readings`

> **Lưu ý:** SensorReading không extend `AuditableEntity`. Đây là time-series append-only data lưu trong TimescaleDB hypertable.

---

### `GET /api/sensor-readings/stream` — SSE realtime

**Mục đích:** Kênh **một chiều server → client** đẩy số đo cảm biến theo thời gian thực (~5 giây/pin)
cho 1 pin hoặc nhiều pin cùng lúc.

**Tác dụng:** Thay cho việc FE gọi `/latest` theo vòng lặp. Dữ liệu lên stream **đã calibrate và đã
loại nhiễu/outlier** — giống hệt dữ liệu đã ghi vào hypertable.

**Auth:** Bắt buộc — `Admin` / `Manager` / `Staff` / `Customer`. Vì `EventSource` của trình duyệt
**không set được header `Authorization`**, token truyền qua query `?access_token=<JWT>`.
Quyền theo từng scope còn bị kiểm thêm (Customer chỉ mở được scope pin của chính mình).

**Content-Type trả về:** `text/event-stream` (KHÔNG phải `application/json` như các endpoint khác).

**Query params:**

| Param | Type | Bắt buộc | Mô tả |
|---|---|---|---|
| `scope` | `string` | **Có** | `asset:{id}` · `assets:{id1,id2}` · `customer:{id}` · `site:{id}` · `sites:{id1,id2}` · `type:{id}` · `all` · `site:none`. Mỗi danh sách **tối đa 50 id** |
| `access_token` | `string` | Có (nếu không set header `Authorization`) | JWT |

**Request header tuỳ chọn:**

| Header | Mô tả |
|---|---|
| `Last-Event-ID` | Id của event cuối client đã nhận. Có giá trị ⇒ server **phát bù** các reading sau id đó trước khi nối luồng trực tiếp. **Chỉ có tác dụng với scope 1 pin** (`asset:{1 id}`) |

**4 loại event** — `reading` · `summary` · `stats` · `ping`. **Chỉ `reading` mang dòng `id:`.**

```
id: 1785578400123-0
event: reading
data: {"batteryAssetId":"3fa85f64-...","time":"2026-08-01T07:20:00.123Z","voltage":52.3, ... }

```

**Lỗi non-2xx** trả `CommonResponse` như mọi endpoint khác (chưa mở stream):

| Mã | Khi nào | Hình dạng |
|---|---|---|
| `400` | `scope` sai cú pháp hoặc quá 50 id | `listErrors: [{ field: "scope", detail: "..." }]` |
| `401` | Không xác định được người dùng từ token | Chỉ `message`, `listErrors` = `null` |
| `403` | Token hợp lệ nhưng không có quyền với scope đó | Chỉ `message`, `listErrors` = `null` |

> 📖 **Hợp đồng đầy đủ cho FE/Mobile** — bộ field của `reading`/`summary`/`stats`, RBAC theo từng
> scope, phát bù `Last-Event-ID` (cửa sổ bù, tình huống biên, khử trùng), cờ cấu hình, và cách thử
> bằng curl: xem [docs/battery-realtime-description.md](battery-realtime-description.md).

---

### `GET /api/sensor-readings/{batteryAssetId}/latest`

**Mục đích:** Lấy sensor reading mới nhất của một pin.

**Auth:** Bắt buộc (Admin/Manager/Staff/Customer)

**Path param:** `batteryAssetId` — Guid của battery asset

**Response thành công `200`:**
```json
{
  "isSuccess": true,
  "data": {
    "time": "2026-05-16T08:00:00Z",
    "batteryAssetId": "guid",
    "voltage": 52.3,
    "current": -2.5,
    "temperature": 28.4,
    "socPercent": 78.5,
    "cycleCount": 234,
    "sourceDeviceId": "DEVICE-001"
  }
}
```

**Chi tiết `SensorReadingDto`:**

| Field | Type | Nullable | Mô tả |
|---|---|---|---|
| `time` | `DateTime` | Không | Timestamp đo lường (UTC) — partition key TimescaleDB |
| `batteryAssetId` | `string` | Không | ID battery asset |
| `voltage` | `decimal` | Không | Điện áp (V) |
| `current` | `decimal` | Không | Dòng điện (A) — âm = đang xả |
| `temperature` | `decimal` | Không | Nhiệt độ (°C) |
| `socPercent` | `decimal` | Không | State of Charge (%) — 0 đến 100 |
| `cycleCount` | `int?` | Null nếu BMS không report | Số chu kỳ sạc/xả đã thực hiện |
| `sourceDeviceId` | `string?` | Null nếu không có | ID thiết bị IoT gửi data |

---

### `GET /api/sensor-readings/{batteryAssetId}/history`

**Mục đích:** Lịch sử sensor readings của một pin trong khoảng thời gian, phân trang theo cursor timestamp.

**Auth:** Bắt buộc (Admin/Manager/Staff/Customer)

**Path param:** `batteryAssetId` — Guid của battery asset

**Query params:**

| Param | Type | Bắt buộc | Mô tả |
|---|---|---|---|
| `from` | `DateTime?` | Không | Từ thời điểm (UTC) |
| `to` | `DateTime?` | Không | Đến thời điểm (UTC) |
| `limit` | `int` | Không (mặc định 100) | Số record mỗi trang, range `1–1000` |
| `cursor` | `DateTime?` | Không | Timestamp của record cuối trang trước; BE lấy record có `time < cursor` (desc) hoặc `time > cursor` (asc) |
| `sortBy` | `string?` | Không (mặc định `time`) | Whitelist: `time` \| `voltage` \| `current` \| `temperature` \| `socPercent`. Xem **Lưu ý sort** bên dưới |
| `sortDir` | `string?` | Không (mặc định `desc`) | `asc` \| `desc` |

> **Lưu ý:** Swagger còn liệt kê một query param `BatteryAssetId` (trùng tên với path param) do model-binding của BE. FE **bỏ qua** query param này — chỉ truyền `batteryAssetId` qua path.

> **Lưu ý sort (Hướng B):**
> - `sortBy=time` (mặc định) — dùng **cursor pagination** bình thường (`nextCursor`/`hasMore` như cũ). Hỗ trợ cả `asc`/`desc`.
> - `sortBy` khác `time` (`voltage`/`current`/`temperature`/`socPercent`) — **bắt buộc** truyền cả `from` và `to` (thiếu → `400`); BE sort toàn khoảng `[from, to]` theo cột đó, **không dùng cursor** → trả `nextCursor = null` + `hasMore = false` (FE tắt "Tải thêm"). Chỉ tính trên reading nguồn **primary** (`sensorSourceCode = "primary"`/null/empty) để tránh 3 nguồn/tick (redundant `temp=0` / external `voltage=0`) làm sort sai.
> - `sortBy`/`sortDir` ngoài whitelist → coi như `time`/`desc`.

**Response thành công `200`:**
```json
{
  "isSuccess": true,
  "statusCode": 200,
  "data": {
    "items": [
      {
        "time": "2026-05-16T08:00:00Z",
        "batteryAssetId": "guid",
        "voltage": 52.3,
        "current": -2.5,
        "temperature": 28.4,
        "socPercent": 78.5,
        "cycleCount": 234,
        "sourceDeviceId": "DEVICE-001"
      }
    ],
    "nextCursor": "2026-05-16T08:00:00Z",
    "hasMore": true
  }
}
```

**Chi tiết `SensorReadingHistoryResponseDto`:**

| Field | Type | Nullable | Mô tả |
|---|---|---|---|
| `items` | `SensorReadingDto[]` | Không | Danh sách reading, sort `time` giảm dần |
| `nextCursor` | `DateTime?` | Null nếu hết data | Truyền lại vào query `cursor` để lấy trang tiếp theo |
| `hasMore` | `bool` | Không | `true` nếu còn dữ liệu sau trang hiện tại |

**Lỗi thường gặp:**
- `400` — `batteryAssetId` empty, `limit` ngoài `1–1000`, hoặc `sortBy` khác `time` mà thiếu `from`/`to`
- `422` — `from > to` (cross-field business rule)

**Lưu ý hiệu suất:** TimescaleDB có thể chứa hàng triệu rows. Luôn truyền `from`/`to` để giới hạn scan range. Endpoint này không trả `totalItems`. FE dùng `hasMore`/`nextCursor` để infinite scroll; không render pagination kiểu page number.

### `GET /api/sensor-readings/{batteryAssetId}/aggregate`

**Mục đích:** Lấy dữ liệu SensorReading đã được gộp theo bucket thời gian (dùng TimescaleDB `time_bucket()`) — phục vụ vẽ chart dài hạn.

**Auth:** Bắt buộc (Admin/Manager/Staff/Customer)

**Path param:** `batteryAssetId` — Guid của battery asset

**Query params:**

| Param | Type | Bắt buộc | Mô tả |
|---|---|---|---|
| `from` | `DateTime?` | Không | Từ thời điểm (UTC) — lọc `Time >= From` |
| `to` | `DateTime?` | Không | Đến thời điểm (UTC) — lọc `Time <= To` |
| `interval` | `string` | Không (mặc định `1h`) | Bucket: `1m`, `5m`, `15m`, `1h`, `1d` |

> **Lưu ý:** Swagger còn liệt kê query param `BatteryAssetId` trùng tên path param (model-binding artifact) — FE bỏ qua, chỉ dùng `batteryAssetId` qua path.

**Response thành công `200`:** `CommonResponse<SensorReadingAggregateDto[]>` — danh sách bucket sắp xếp tăng dần theo thời gian.

> **Sprint Bonus NS-01/NS-02 (#646/#647):** aggregate chỉ tính trên reading nguồn **primary** (`sensorSourceCode = "primary"` hoặc null/empty — cùng quy ước coalescer SSE §34.10.5). Reading `redundant`/`external-temp` bị loại để tránh đếm trùng 3 nguồn/pin.

```json
{
  "isSuccess": true,
  "data": [
    {
      "time": "2026-06-12T07:00:00Z",
      "avgVoltage": 52.34,
      "avgCurrent": -1.82,
      "avgTemperature": 28.5,
      "avgSocPercent": 76.4,
      "avgSohPercent": 91.2,
      "minVoltage": 51.9,
      "maxVoltage": 52.8,
      "minTemperature": 27.1,
      "maxTemperature": 30.2,
      "maxChargeCurrent": 1.92,
      "minChargeCurrent": 0.31,
      "avgChargeCurrent": 1.05,
      "maxDischargeCurrent": 4.75,
      "minDischargeCurrent": 0.42,
      "avgDischargeCurrent": 2.31,
      "chargeSampleCount": 210,
      "dischargeSampleCount": 385
    }
  ]
}
```

**Chi tiết `SensorReadingAggregateDto`:**

| Field | Type | Nullable | Mô tả |
|---|---|---|---|
| `time` | `DateTime` | Không | Thời điểm bắt đầu bucket (UTC) — field tên là `time` (không phải `bucket`) |
| `avgVoltage` | `decimal` | Không | AVG điện áp (V) trong bucket |
| `avgCurrent` | `decimal` | Không | AVG dòng điện (A) trong bucket — ⚠️ trộn dấu nạp/xả (giữ backward-compat); vẽ chart dùng `avgChargeCurrent`/`avgDischargeCurrent` |
| `avgTemperature` | `decimal` | Không | AVG nhiệt độ (°C) |
| `avgSocPercent` | `decimal` | Không | AVG SOC (%) |
| `avgSohPercent` | `decimal?` | Null nếu bucket không có reading nào có SohPercent | AVG SOH (%) |
| `minVoltage` / `maxVoltage` | `decimal?` | Null nếu bucket không có reading primary | MIN/MAX điện áp (V) |
| `minTemperature` / `maxTemperature` | `decimal?` | Null nếu bucket không có reading primary | MIN/MAX nhiệt độ (°C) |
| `maxChargeCurrent` | `decimal?` | Null nếu bucket không có mẫu nạp | Dòng nạp đỉnh (A, **luôn dương**) — `MAX(current)` với `current > 0` |
| `minChargeCurrent` | `decimal?` | Null nếu bucket không có mẫu nạp | Dòng nạp thấp nhất khi đang nạp (A, dương) |
| `avgChargeCurrent` | `decimal?` | Null nếu bucket không có mẫu nạp | AVG dòng nạp (A, dương) |
| `maxDischargeCurrent` | `decimal?` | Null nếu bucket không có mẫu xả | Dòng xả đỉnh (A, **luôn dương**) — `MAX(ABS(current))` với `current < 0` |
| `minDischargeCurrent` | `decimal?` | Null nếu bucket không có mẫu xả | Dòng xả thấp nhất khi đang xả (A, dương) |
| `avgDischargeCurrent` | `decimal?` | Null nếu bucket không có mẫu xả | AVG dòng xả (A, dương) |
| `chargeSampleCount` | `int` | Không (0 nếu không có) | Số mẫu chiều nạp trong bucket |
| `dischargeSampleCount` | `int` | Không (0 nếu không có) | Số mẫu chiều xả trong bucket |

**Quy ước min/max nạp/xả (chốt NS-01 — newsprint.md §2):**
- Quy ước dấu dữ liệu thô: `current > 0` = nạp, `current < 0` = xả. Response min/max **luôn trả giá trị DƯƠNG** cho cả 2 chiều — chiều nằm trong tên field, FE không phải xử lý dấu.
- Sample `current == 0` (idle) không thuộc chiều nào — bỏ qua khỏi cả 2 phía.
- Field nullable: bucket không có mẫu chiều nào → `null` (pin idle cả bucket → cả cụm field nạp/xả null). **Không trả `0`** — 0A là giá trị đo hợp lệ ≠ không có dữ liệu.
- Đường ngưỡng tham chiếu trên chart lấy từ `GET /api/thresholds` (`currentMaxCharge`/`currentMaxDischarge`) — là ngưỡng cảnh báo admin đặt, **không** nhầm với min/max thực đo.

**Lỗi thường gặp:**
- `400` — `BatteryAssetId` rỗng hoặc `interval` không thuộc `{1m, 5m, 15m, 1h, 1d}`
- `422` — `from > to` (cross-field business rule)
- `401` — Chưa đăng nhập

**Lỗi thường gặp:**
- `400` — `interval` không thuộc tập hợp lệ hoặc `from > to`

**Use case:**
- FE/Mobile vẽ biểu đồ SOC/Voltage/Temperature theo thời gian (≥ 24h).
- Thay thế `/history` cho time range lớn để tránh quá nhiều data points.

> **Lưu ý:** Không trả `totalItems`. FE dùng độ dài mảng `items` từ data.

---

### `GET /api/sensor-readings/{batteryAssetId}/aggregate/hourly`

**Sprint Bonus NS-06 (#650, PA-4).**

**Mục đích / tác dụng:** trả aggregate cố định bucket **1 giờ** cho chart min/max nạp/xả (+ V/T + avg) của 1 pin, đọc từ TimescaleDB **continuous aggregate** `sensor_readings_agg_1h` (materialized view tự refresh mỗi phút, bật real-time aggregation nên bao gồm cả dữ liệu gần đây chưa materialize). Dùng cho **chart range dài (tháng/năm)** — query gần O(số bucket), **không** materialize toàn bộ rows vào RAM như `/aggregate` (in-memory bounded ~7 ngày).

**Cách dùng:** giống `/aggregate` nhưng không có `interval` (cố định 1h). FE dùng khi vẽ chart range dài / cần hiệu năng ổn định. Chỉ tính trên reading source **primary** (giống `/aggregate`).

**Auth:** JWT — role `Admin` / `Manager` / `Staff` / `Customer`.

**Path param:**

| Param | Type | Bắt buộc | Mô tả |
|---|---|---|---|
| `batteryAssetId` | `Guid` | ✅ | ID pin (lấy từ route — client không set qua query/body) |

**Query params:**

| Param | Type | Bắt buộc | Mô tả |
|---|---|---|---|
| `from` | `DateTime?` | Không | Lọc `bucket >= from` (UTC) |
| `to` | `DateTime?` | Không | Lọc `bucket <= to` (UTC) |

> Không có `interval` — endpoint này cố định 1h.

**Response thành công `200`:** `CommonResponse<SensorReadingAggregateDto[]>` — **cùng shape** với `/aggregate` (đủ min/max nạp/xả + min/max V/T + avg tách chiều + `chargeSampleCount`/`dischargeSampleCount`), sắp xếp **tăng dần** theo `time` (= bucket). Xem chi tiết field + quy ước dương/nullable ở section **`GET .../aggregate`** phía trên (field `time` ở đây là mốc đầu bucket 1h).

```json
{
  "isSuccess": true,
  "data": [
    {
      "time": "2026-07-08T09:00:00Z",
      "avgVoltage": 52.34, "avgCurrent": -1.82, "avgTemperature": 28.5, "avgSocPercent": 76.4, "avgSohPercent": 91.2,
      "minVoltage": 51.9, "maxVoltage": 52.8, "minTemperature": 27.1, "maxTemperature": 30.2,
      "maxChargeCurrent": 1.92, "minChargeCurrent": 0.31, "avgChargeCurrent": 1.05,
      "maxDischargeCurrent": 4.75, "minDischargeCurrent": 0.42, "avgDischargeCurrent": 2.31,
      "chargeSampleCount": 210, "dischargeSampleCount": 385
    }
  ],
  "listErrors": null
}
```

**Lỗi thường gặp:**
- `400` — `batteryAssetId` rỗng → field-level (`listErrors: [{ field: "BatteryAssetId", detail }]`)
- `422` — `from > to` → field-level (`listErrors: [{ field: "To", detail }]`)
- `401` — Chưa đăng nhập

> **Khi nào dùng cái nào:** range ngắn (≤ 7 ngày, cần interval linh hoạt 1m/5m/…) → `/aggregate`. Range dài / interval 1h cố định / cần hiệu năng ổn định → `/aggregate/hourly`.

---

### `POST /api/sensor-readings/batch`

**Mục đích:** Ingest batch sensor readings từ IoT device/gateway (endpoint nội bộ).

**Auth:** API Key (IoT gateway xác thực bằng header `X-Api-Key`, không dùng JWT)

**Giới hạn batch:** Tối đa **1000** readings mỗi request. Vượt quá → `400 isSuccess=false`.

**Request body:**
```json
{
  "items": [
    {
      "batteryAssetId": "guid",
      "batteryAssetSerial": null,
      "time": "2026-05-16T08:00:00Z",
      "deviceTimestamp": "2026-05-16T08:00:01Z",
      "voltage": 52.3,
      "current": -2.5,
      "temperature": 28.4,
      "socPercent": 78.5,
      "cycleCount": 234,
      "sohPercent": 91.2,
      "chargingState": 3,
      "sourceDeviceId": "DEVICE-001",
      "internalResistanceMilliohm": 12.5,
      "cellVoltageDeltaMv": 30.0,
      "sourceType": 2,
      "bmsErrorCode": null,
      "sensorSourceCode": null
    }
  ]
}
```

**Validation mỗi reading:**

| Field | Type | Bắt buộc | Validation | Mô tả |
|---|---|---|---|---|
| `batteryAssetId` | `string?` | Bắt buộc 1 trong 2 | Cần `batteryAssetId` **hoặc** `batteryAssetSerial` | ID pin (UUID). Item không match asset hợp lệ → bị `skipped` |
| `batteryAssetSerial` | `string?` | Bắt buộc 1 trong 2 | Max 64 ký tự | Serial pin — thay thế cho `batteryAssetId` khi device chỉ biết serial |
| `time` | `DateTime` | Bắt buộc | Không ở tương lai (cho phép lệch tối đa +5 phút) | Timestamp đo (UTC) |
| `deviceTimestamp` | `DateTime?` | Không | Lệch tối đa ±5 phút so với server (nếu truyền) | Timestamp tại device — phục vụ kiểm tra clock skew |
| `voltage` | `decimal` | Bắt buộc | >= 0 | Điện áp (V) |
| `current` | `decimal` | Bắt buộc | — | Dòng điện (A) — âm = đang xả |
| `temperature` | `decimal` | Bắt buộc | — (range không validate ở field-level; giá trị ngoài dải chỉ bị đánh dấu outlier khi xử lý calibration ở handler) | Nhiệt độ (°C) |
| `socPercent` | `decimal` | Bắt buộc | — (không validate range 0–100 ở field-level) | State of Charge (%) |
| `cycleCount` | `int?` | Không | >= 0 | Số chu kỳ |
| `sohPercent` | `decimal?` | Không | — (không validate range ở field-level) | SOH từ AI module |
| `chargingState` | `ChargingStateEnum?` | Không | enum hợp lệ nếu truyền | Trạng thái nạp/xả |
| `sourceDeviceId` | `string?` | Không | Max 64 ký tự | Device ID |
| `internalResistanceMilliohm` | `decimal?` | Không | > 0 nếu truyền | Điện trở trong (mΩ) — Tier-2 battery health, dùng phát hiện `HighInternalResistance` |
| `cellVoltageDeltaMv` | `decimal?` | Không | >= 0 nếu truyền | Chênh lệch điện áp giữa các cell (mV) — dùng phát hiện `CellImbalance` |
| `sourceType` | `SensorReadingSourceTypeEnum` | Không (mặc định `2` = `IotGateway`) | enum hợp lệ | Nguồn dữ liệu reading (xem enum) — phục vụ cross-source mismatch check (Sprint 7) |
| `bmsErrorCode` | `string?` | Không | Max 64 ký tự | Mã lỗi raw từ BMS (nếu device gửi) |
| `sensorSourceCode` | `string?` | Không | Max 20 ký tự | Mã nguồn/định danh kênh cảm biến (nếu device gửi) |

> **Lưu ý:** Endpoint thực tế **không** validate range cho `temperature`, `socPercent`, `sohPercent` ở tầng validation (không trả `400` cho các giá trị này). FE/gateway nên tự đảm bảo dữ liệu hợp lý trước khi gửi.

**Response thành công `201 Created`:** Tạo resource mới (sensor readings trong TimescaleDB hypertable).
```json
{
  "isSuccess": true,
  "statusCode": 201,
  "data": {
    "totalReceived": 10,
    "inserted": 9,
    "skipped": 1
  }
}
```

| Field | Type | Mô tả |
|---|---|---|
| `totalReceived` | `int` | Tổng số reading nhận được trong batch |
| `inserted` | `int` | Số reading đã insert thành công vào TimescaleDB |
| `skipped` | `int` | Số reading bị bỏ qua vì `batteryAssetId` không tồn tại hoặc đã xóa |

**Lỗi thường gặp:**
- `401` — Thiếu hoặc sai `X-Api-Key` header
- `400` — `items` rỗng, vượt giới hạn 1000 readings, hoặc có item không hợp lệ (trả `listErrors` chi tiết từng item)

> **Lưu ý hiệu suất:** Không gửi quá nhiều batch nhỏ liên tiếp. Gateway nên gom readings trong 1 batch mỗi 30–60 giây. Không có rate limit cứng trong Sprint 3, nhưng sẽ thêm khi scale lên.

---

## Nhóm 5 — Sites (Địa điểm lắp đặt)

Base route: `/api/sites`

---

### `GET /api/sites`

**Mục đích:** Danh sách site với phân trang và lọc (Admin/Manager/Staff — Staff dùng chọn site khi report sự cố thủ công, GH-145).

**Auth:** Bắt buộc (Admin/Manager/**Staff** — mở cho Staff đợt này, GH-145/GH-146). Các thao tác tạo/sửa/xoá site vẫn Admin-only.

**Query params:**

| Param | Type | Mô tả |
|---|---|---|
| `pageNumber` | `int` | Trang |
| `pageSize` | `int` | Số item/trang |
| `keyword` | `string?` | Tìm theo tên site hoặc địa chỉ |
| `customerId` | `string?` | Lọc theo khách hàng (UUID string) |
| `status` | `SiteStatusEnum?` | Lọc theo trạng thái |
| `includeDeleted` | `bool` | Bao gồm đã xóa (mặc định `false`) |
| `SortBy` | `string?` | Cột sort server-side. Whitelist: `name`, `customerName`, `status`, `batteryAssetCount`, `installDate`. Ngoài whitelist → `createdAt` |
| `SortDir` | `string?` | `asc` \| `desc` (mặc định `desc`; giá trị lạ → `desc`) |

> **Sắp xếp:** mặc định `createdAt` desc. Đã hỗ trợ sort server-side qua `SortBy`/`SortDir` (order toàn dataset trước phân trang, tie-breaker `Id ASC`). `batteryAssetCount` = số pin thuộc site (computed). Chi tiết enum `SiteStatusEnum` + nullable: xem **Server-side Sort** đầu tài liệu.

**Response thành công `200`:** `PaginationResponse<SiteDto>`

**Chi tiết `SiteDto`:**

| Field | Type | Nullable | Mô tả |
|---|---|---|---|
| `id` | `string` | Không | ID site |
| `name` | `string` | Không | Tên site (e.g., `Nhà máy mặt trời An Giang 1`) |
| `customerId` | `string` | Không | ID khách hàng sở hữu |
| `customerName` | `string` | Không | Tên khách hàng từ `CustomerAccount` read model |
| `address` | `string?` | Null nếu chưa cung cấp | Địa chỉ |
| `latitude` | `decimal?` | Null nếu không có tọa độ | Vĩ độ (-90 đến 90) |
| `longitude` | `decimal?` | Null nếu không có tọa độ | Kinh độ (-180 đến 180) |
| `installDate` | `DateTime` | Không | Ngày lắp đặt hệ thống (UTC) |
| `status` | `SiteStatusEnum` | Không | Trạng thái site (xem enum) |
| `contactPersonName` | `string?` | Null nếu chưa cung cấp | Tên người liên hệ tại site |
| `contactPersonPhone` | `string?` | Null nếu chưa cung cấp | SĐT người liên hệ |
| `batteryAssetCount` | `int` | Không | Tổng số pin tại site |
| `activeBatteryAssetCount` | `int` | Không | Số pin đang Active |
| `createdAt` | `DateTime` | Không | Thời điểm tạo record (UTC) |

---

### `GET /api/sites/me`

**Mục đích:** Danh sách site của Customer đang đăng nhập.

**Auth:** Bắt buộc (Customer)

**Query params:** `pageNumber`, `pageSize`

---

### `GET /api/sites/{id}`

**Mục đích:** Xem chi tiết một site.

**Auth:** Bắt buộc (Admin/Manager/Staff/Customer)

**Response thành công `200`:** `CommonResponse<SiteDto>`

---

### `GET /api/sites/{id}/dashboard`

**Mục đích:** Dashboard tổng hợp cho site: số pin, số alert, health score.

**Auth:** Bắt buộc (Admin/Manager/Staff/Customer)

**Response thành công `200`:**
```json
{
  "isSuccess": true,
  "data": {
    "siteId": "guid",
    "name": "Nhà máy mặt trời An Giang 1",
    "customerId": "guid",
    "totalAssets": 48,
    "activeAssets": 46,
    "assetsWithActiveAlerts": 2,
    "lastAlertAt": "2026-05-16T06:30:00Z",
    "healthScore": 87
  }
}
```

**Chi tiết `SiteDashboardDto`:**

| Field | Type | Nullable | Mô tả |
|---|---|---|---|
| `siteId` | `string` | Không | ID site |
| `name` | `string` | Không | Tên site |
| `customerId` | `string` | Không | ID khách hàng |
| `totalAssets` | `int` | Không | Tổng số pin (bao gồm cả Inactive, Decommissioned) |
| `activeAssets` | `int` | Không | Số pin đang Active |
| `assetsWithActiveAlerts` | `int` | Không | Số pin có ít nhất 1 alert Open/Acknowledged |
| `lastAlertAt` | `DateTime?` | Null nếu chưa có alert nào | Thời điểm alert gần nhất (UTC) |
| `healthScore` | `int` | Không | Điểm sức khỏe hệ thống 0–100, tính theo công thức bên dưới |

**Công thức `healthScore` hiện tại (Sprint 3):**

```text
healthScore = 100
            - ((totalAssets - activeAssets) * 5)
            - (assetsWithActiveAlerts * 10)

healthScore được clamp về [0, 100].
Nếu site không có asset nào, healthScore = 100.
```

`assetsWithActiveAlerts` chỉ tính asset có alert `Open` hoặc `Acknowledged`, không tính `Merged` hoặc `Resolved`.

**Ngưỡng màu khuyến nghị FE/Mobile:**

| healthScore | Màu | Label |
|---|---|---|
| `80–100` | Xanh lá | Tốt |
| `50–79` | Vàng | Cần theo dõi |
| `0–49` | Đỏ | Nguy hiểm |

---

### `GET /api/sites/dashboard/stats`

**Mục đích:** Snapshot tổng hợp **TOÀN BỘ site** cho Dashboard Admin/Manager (KPI "Sites → N hoạt động", "Trung bình X% sức khỏe site", "Sites cần chú ý <80%") — khác `GET /api/sites/{id}/dashboard` là dashboard của **một** site. Thay cho việc FE tự tính trung bình health trên 1 trang list (cap 100 → sai số khi vượt pageSize).

**Auth:** Bắt buộc (Admin hoặc Manager) — cùng quyền với `GET /api/sites`.

**Query params:** Không có — endpoint snapshot, **không nhận from/to**. FE nên cache ~1 phút (staleTime).

**Response thành công `200`:** `CommonResponse<SiteDashboardStatsDto>`

```json
{
  "isSuccess": true, "statusCode": 200, "message": "",
  "data": {
    "total": 12,
    "activeCount": 10,
    "totalBatteries": 480,
    "activeBatteries": 452,
    "avgHealth": 88.42,
    "atRiskCount": 2
  },
  "listErrors": null
}
```

**Chi tiết `SiteDashboardStatsDto`** — mọi field đều **không null** (`data` chỉ null khi lỗi):

| Field | Type | Mô tả |
|---|---|---|
| `total` | `int` | Tổng số site (không tính đã xóa mềm) |
| `activeCount` | `int` | Số site có `status = Active` (không tính `UnderMaintenance`/`Decommissioned`) |
| `totalBatteries` | `int` | Tổng battery asset **toàn hệ thống** — gồm cả asset **chưa gán site** — cố ý **khớp `totalAssets`** của `GET /api/battery/dashboard/stats` để 2 dashboard không vênh số |
| `activeBatteries` | `int` | Số asset đang `Active` toàn hệ thống — khớp `activeAssets` của battery stats |
| `avgHealth` | `number` | Trung bình `healthScore` của các site, làm tròn 2 chữ số. Health từng site tính **cùng công thức** với `GET /api/sites/{id}/dashboard` (100 − inactive×5 − alertAssets×10, clamp [0,100]; site chưa có asset = 100). `= 0` khi chưa có site nào |
| `atRiskCount` | `int` | Số site có `healthScore < 80` — khớp ngưỡng màu "Tốt" ở bảng trên |

> **Lưu ý:** asset chưa gán site được tính vào `totalBatteries`/`activeBatteries` nhưng **không** ảnh hưởng health của site nào. `assetsWithActiveAlerts` dùng cùng định nghĩa alert active (`Open`/`Acknowledged`, distinct theo asset, loại alert/asset đã xóa) với endpoint per-site.

**Lỗi thường gặp:**
- `401` — Chưa đăng nhập
- `403` — Không có role Admin/Manager

---

### `GET /api/sites/{siteId}/assets`

**Mục đích:** Danh sách battery asset thuộc một site.

**Auth:** Bắt buộc (Admin/Manager/Staff/Customer)

> **Lưu ý route param:** Swagger đăng ký route này là `/api/sites/{id}/assets` (path param tên `id`, không phải `siteId`). Đường dẫn thực tế giống nhau — chỉ khác tên biến trong spec.

**Query params:**

| Param | Type | Mô tả |
|---|---|---|
| `pageNumber` | `int` | Trang |
| `pageSize` | `int` | Số item/trang |
| `status` | `BatteryStatusEnum?` | Lọc theo trạng thái |
| `SortBy` | `string?` | Cột sort server-side. Whitelist: `serialNumber`, `batteryTypeName`, `status`, `installDate`, `lastSensorReadingAt`. Ngoài whitelist → `createdAt` |
| `SortDir` | `string?` | `asc` \| `desc` (mặc định `desc`; giá trị lạ → `desc`) |

> **Sắp xếp:** mặc định `createdAt` desc. Đã hỗ trợ sort server-side qua `SortBy`/`SortDir` (order toàn dataset trước phân trang, tie-breaker `Id ASC`). `lastSensorReadingAt` nullable. Chi tiết: xem **Server-side Sort** đầu tài liệu.

**Response thành công `200`:** `PaginationResponse<BatteryAssetDto>`

---

### `POST /api/admin/sites`

**Mục đích:** Tạo site mới.

**Auth:** Bắt buộc (Admin)

**Request body:**

| Field | Type | Bắt buộc | Validation | Mô tả |
|---|---|---|---|---|
| `name` | `string` | **Bắt buộc** | Max 200 ký tự, unique trong phạm vi 1 Customer (case-insensitive) | Tên site |
| `customerId` | `string` | **Bắt buộc** | UUID hợp lệ, Customer phải tồn tại và active trong read-model | Khách hàng sở hữu |
| `address` | `string?` | Không | Max 500 ký tự | Địa chỉ |
| `latitude` | `decimal?` | Không | -90 đến 90 | Vĩ độ |
| `longitude` | `decimal?` | Không | -180 đến 180 | Kinh độ |
| `installDate` | `DateTime` | **Bắt buộc** | Không ở tương lai | Ngày lắp đặt |
| `status` | `SiteStatusEnum` | Không (mặc định `Active`) | — | Trạng thái ban đầu |
| `contactPersonName` | `string?` | Không | Max 150 ký tự | Tên người liên hệ |
| `contactPersonPhone` | `string?` | Không | Max 30 ký tự | SĐT người liên hệ |

> **Lưu ý:** `CreateSiteCommand` **không có** field `capacityKw`. Tương ứng, `SiteDto` và `SiteDashboardDto` **không có** field `capacityKw` / `totalCapacityKw` — các field này không tồn tại trong response.

**Cách hoạt động:**
- Validate đầy đủ.
- Check Customer tồn tại + active (`404` nếu không).
- Check trùng tên trong phạm vi customer (`409` nếu trùng).
- Lưu site với `Id = Guid.NewGuid()`.

**Response thành công `201`:** `CommonResponse<SiteDto>`

**Lỗi thường gặp:**
- `400` — Validation field lỗi (xem `listErrors`)
- `404` — `customerId` không tồn tại hoặc không active
- `409` — Tên site đã tồn tại trong phạm vi customer

---

### `PUT /api/admin/sites/{id}`

Cập nhật thông tin site. Body giống POST (các field trong `UpdateSiteCommand` — **không có** `capacityKw`), thêm `id` từ route.

**Auth:** Bắt buộc (Admin)

**Cách hoạt động:**
- Tìm site (include Assets); `404` nếu không có.
- Nếu `customerId` trong body khác customer hiện tại (và khác `Guid.Empty`) → trả `409` — không cho phép đổi chủ sở hữu qua Update (dùng `transfer-owner`).
- Check trùng tên (loại trừ chính nó); trùng trả `409`.
- Update toàn bộ field còn lại; **KHÔNG** đụng đến `customerId`.

**Response thành công `200`:** `CommonResponse<SiteDto>`

**Lỗi thường gặp:**
- `400` — Validation field lỗi
- `404` — Site không tìm thấy
- `409` — Trùng tên trong phạm vi customer, hoặc cố đổi `customerId` qua Update

---

### `DELETE /api/admin/sites/{id}`

Soft delete site.

**Auth:** Bắt buộc (Admin)

---

### `PATCH /api/admin/sites/{id}/restore`

Khôi phục site đã xóa.

**Auth:** Bắt buộc (Admin)

---

## Nhóm 6 — Threshold Configs (Ngưỡng Cảnh báo)

Base route: `/api/thresholds`

> ThresholdConfig định nghĩa ngưỡng cho từng `BatteryType`. Khi sensor reading vượt ngưỡng, AnomalyDetector tạo Alert tự động.

---

### `GET /api/thresholds`

**Mục đích:** Danh sách cấu hình ngưỡng có phân trang.

**Auth:** Bắt buộc (Admin/Manager)

**Query params:**

| Param | Type | Mô tả |
|---|---|---|
| `pageNumber` | `int` | Trang |
| `pageSize` | `int` | Số item/trang |
| `batteryTypeId` | `string?` | Lọc theo loại pin (UUID string) |
| `isActive` | `bool?` | Mặc định `true` — chỉ lấy config đang active |

**Response thành công `200`:** `PaginationResponse<ThresholdConfigDto>`

**Chi tiết `ThresholdConfigDto`:**

| Field | Type | Nullable | Mô tả |
|---|---|---|---|
| `id` | `string` | Không | ID cấu hình |
| `batteryTypeId` | `string` | Không | ID loại pin áp dụng |
| `batteryTypeName` | `string` | Không | Tên loại pin |
| `voltageMin` | `decimal` | Không | Điện áp tối thiểu (V) — thấp hơn → `Undervoltage` alert |
| `voltageMax` | `decimal` | Không | Điện áp tối đa (V) — cao hơn → `Overvoltage` alert |
| `temperatureMax` | `decimal` | Không | Nhiệt độ tối đa (°C) — cao hơn → `Overheat` alert |
| `temperatureMin` | `decimal` | Không | Nhiệt độ tối thiểu (°C) |
| `socWarningThreshold` | `decimal` | Không | SOC % cảnh báo Warning (e.g., 20%) |
| `socCriticalThreshold` | `decimal` | Không | SOC % cảnh báo Critical (e.g., 10%) — thấp hơn → `LowSoc` alert |
| `currentMaxCharge` | `decimal?` | Null nếu không giới hạn | Dòng nạp tối đa (A) — vượt → `AbnormalCharging` alert |
| `currentMaxDischarge` | `decimal?` | Null nếu không giới hạn | Dòng xả tối đa (A) — vượt → `RapidDischarge` alert |
| `sohWarningThreshold` | `decimal?` | Null nếu không monitor SOH | SOH % ngưỡng Warning (e.g., 80%) |
| `sohCriticalThreshold` | `decimal?` | Null nếu không monitor SOH | SOH % ngưỡng Critical (e.g., 70%) — thấp hơn → `SohDegradation` alert |
| `effectiveFromUtc` | `DateTime` | Không | Thời điểm cấu hình có hiệu lực (UTC) |
| `isActive` | `bool` | Không | `true` = đang được dùng để phát hiện bất thường |

---

### `GET /api/thresholds/by-type/{batteryTypeId}`

**Mục đích:** Lấy cấu hình ngưỡng đang active cho một loại pin.

**Auth:** Bắt buộc (Admin/Manager)

**Query params:**

| Param | Type | Mô tả |
|---|---|---|
| `includeInactive` | `bool` | Mặc định `false` — trả luôn config inactive nếu không có active |

**Response thành công `200`:** `CommonResponse<ThresholdConfigDto>`

---

### `PUT /api/admin/thresholds/by-type/{batteryTypeId}`

**Mục đích:** Upsert (tạo mới hoặc cập nhật) cấu hình ngưỡng cho một loại pin.

**Auth:** Bắt buộc (Admin)

**Request body:**

| Field | Type | Bắt buộc | Validation | Mô tả |
|---|---|---|---|---|
| `voltageMin` | `decimal` | **Bắt buộc** | > 0 | Điện áp tối thiểu (V) |
| `voltageMax` | `decimal` | **Bắt buộc** | > `voltageMin` | Điện áp tối đa (V) |
| `temperatureMax` | `decimal` | **Bắt buộc** | > `temperatureMin` | Nhiệt độ tối đa (°C) |
| `temperatureMin` | `decimal` | **Bắt buộc** | — | Nhiệt độ tối thiểu (°C) |
| `socWarningThreshold` | `decimal` | **Bắt buộc** | 0–100 | SOC Warning (%) |
| `socCriticalThreshold` | `decimal` | **Bắt buộc** | < `socWarningThreshold`, 0–100 | SOC Critical (%) |
| `currentMaxCharge` | `decimal?` | Không | > 0 nếu truyền | Dòng nạp tối đa (A) |
| `currentMaxDischarge` | `decimal?` | Không | > 0 nếu truyền | Dòng xả tối đa (A) |
| `sohWarningThreshold` | `decimal?` | Không | 0–100 nếu truyền | SOH Warning (%) |
| `sohCriticalThreshold` | `decimal?` | Không | 0–100 nếu truyền, và < `sohWarningThreshold` nếu cả hai truyền | SOH Critical (%) |
| `effectiveFromUtc` | `DateTime` | Không (mặc định `UtcNow`) | — | Thời điểm có hiệu lực |

> **Mã lỗi:** Lỗi field-level (vd `voltageMin <= 0`, `socWarning` ngoài 0–100) trả `400`. Lỗi cross-field (`voltageMax <= voltageMin`, `temperatureMax <= temperatureMin`, `socCritical >= socWarning`, `sohCritical >= sohWarning`) trả `422`.

**Path param:** `batteryTypeId` — Guid của loại pin cần cấu hình.

> **Lưu ý:** Body của `UpsertThresholdConfigCommand` cũng chứa field `batteryTypeId`, nhưng giá trị từ **path param** mới là nguồn quyết định. FE không cần set `batteryTypeId` trong body (BE gán từ route).

**Cách hoạt động:**
- Nếu chưa có config cho `batteryTypeId` → tạo mới với `isActive = true`
- Nếu đã có config active → ghi đè các field threshold trên record đó

**Response thành công `200`:** `CommonResponse<ThresholdConfigDto>`

---

## Nhóm 7 — Dashboard

Base route: `/api/battery/dashboard`

> Liên quan: `GET /api/sites/dashboard/stats` (snapshot tổng hợp toàn bộ site — Nhóm 5) dùng cùng mục đích dashboard nhưng nằm dưới route `api/sites`.

---

### `GET /api/battery/dashboard/stats`

**Mục đích:** Thống kê tổng quan Battery dashboard cho Admin/Manager UI (Sprint 5B B11) — KPI cards + nhiều chart (donut/bar/line/heatmap) chỉ qua 1 endpoint.

**Auth:** Bắt buộc (JWT hợp lệ — `[Authorize]`). Mọi role đã đăng nhập đều gọi được; tuỳ FE quyết định hiển thị.

**Query params:**

| Param | Type | Bắt buộc | Mô tả |
|---|---|---|---|
| `siteId` | `Guid?` | Không | Nếu truyền → mọi count filter theo site đó; không truyền → tổng hợp toàn hệ thống |

**Response thành công `200`:** `CommonResponse<BatteryDashboardStatsDto>`

```json
{
  "isSuccess": true,
  "statusCode": 200,
  "data": {
    "totalAssets": 48,
    "activeAssets": 46,
    "offlineAssets": 2,
    "openAlerts": 7,
    "openAlertsCritical": 2,
    "openAlertsWarning": 5,
    "openEnvironmentalIncidents": 1,
    "sites": 3,
    "assetStatusDistribution": [
      { "status": 1, "statusName": "Active", "count": 46 },
      { "status": 2, "statusName": "Inactive", "count": 1 },
      { "status": 3, "statusName": "Decommissioned", "count": 1 }
    ],
    "sohDistribution": {
      "healthy": 30, "normal": 10, "warning": 5, "eol": 1, "unknown": 2
    },
    "alertSeverityBreakdown": { "critical": 2, "warning": 5, "info": 0 },
    "openAlertsByType": [
      { "anomalyType": 1, "anomalyName": "Overheat", "count": 3 }
    ],
    "alertTrend7Days": [
      { "date": "2026-06-06", "critical": 0, "warning": 1, "info": 0, "total": 1 }
    ],
    "ambientTrend24Hours": [
      { "hourUtc": "2026-06-12T00:00:00Z", "avgTemperature": 31.4, "avgHumidity": 68.2, "avgSolarIrradiance": 420.1 }
    ],
    "sensorAggregate24Hours": {
      "avgVoltage": 52.1, "avgCurrent": -1.8, "avgTemperature": 29.4,
      "avgSoc": 76.0, "avgSoh": 91.2, "readingsCount": 12480
    },
    "topAlertingAssets": [
      { "batteryAssetId": "guid", "serialNumber": "BAT-001", "alertCount": 6, "criticalCount": 1 }
    ],
    "environmentalIncidentsByType": [
      { "incidentType": 2, "incidentName": "FireDetected", "count": 1 }
    ],
    "chemistryDistribution": [
      { "chemistry": 1, "chemistryName": "LiFePO4", "assetCount": 42 }
    ]
  }
}
```

**Chi tiết `BatteryDashboardStatsDto`:**

| Field | Type | Nullable | Mô tả |
|---|---|---|---|
| `totalAssets` | `int` | Không | Tổng asset (bao gồm mọi status, không tính `IsDeleted`) |
| `activeAssets` | `int` | Không | Số asset `Active` |
| `offlineAssets` | `int` | Không | Số asset không nhận sensor reading trong `OfflineThresholdMinutes` (mặc định **10** phút) gần nhất |
| `openAlerts` | `int` | Không | Tổng alert đang ở trạng thái `Open` (chỉ `Open`, **không** tính `Acknowledged`) |
| `openAlertsCritical` | `int` | Không | Alert `Open` severity `Critical` |
| `openAlertsWarning` | `int` | Không | Alert `Open` severity `Warning` |
| `openEnvironmentalIncidents` | `int` | Không | Số `EnvironmentalIncident` đang ở trạng thái `Open` (chỉ `Open`, **không** tính `Acknowledged`) |
| `sites` | `int` | Không | Tổng số site (theo filter) |
| `assetStatusDistribution` | `AssetStatusBucketDto[]` | Không | Donut chart: mỗi bucket gồm `status` (enum int), `statusName`, `count` |
| `sohDistribution` | `SohBucketDto` | Không | Donut SOH: `healthy` (≥90%), `normal` (80–89%), `warning` (75–79%), `eol` (<75%), `unknown` (chưa có reading SOH) |
| `alertSeverityBreakdown` | `AlertSeverityBreakdownDto` | Không | Donut alert by severity — chỉ tính trên alert đang `Open`: `critical`, `warning`, `info` |
| `openAlertsByType` | `AlertByTypeDto[]` | Không | Bar chart: alert đang mở phân theo `AnomalyTypeEnum` |
| `alertTrend7Days` | `DailyTrendPointDto[]` | Không | Line chart 7 ngày — mỗi điểm: `date` (DateOnly), `critical`, `warning`, `info`, `total` |
| `ambientTrend24Hours` | `AmbientTrendPointDto[]` | Không | Line chart 24h gần nhất — mỗi điểm: `hourUtc`, `avgTemperature?`, `avgHumidity?`, `avgSolarIrradiance?` (có thể null nếu giờ đó không có data) |
| `sensorAggregate24Hours` | `SensorAggregateDto` | Không | Aggregated sensor metrics 24h: `avgVoltage?`, `avgCurrent?`, `avgTemperature?`, `avgSoc?`, `avgSoh?`, `readingsCount` (decimal fields nullable nếu không có data) |
| `topAlertingAssets` | `TopAlertingAssetDto[]` | Không | Top 5 asset có nhiều alert nhất 30 ngày qua |
| `environmentalIncidentsByType` | `EnvironmentalIncidentByTypeDto[]` | Không | Donut incident theo type — tính trên incident đang `Open` **hoặc** `Acknowledged` (khác `openEnvironmentalIncidents` ở trên chỉ tính `Open`) |
| `chemistryDistribution` | `ChemistryBucketDto[]` | Không | Donut phân phối asset theo `BatteryChemistryEnum` |

**Lỗi thường gặp:**
- `401` — Chưa đăng nhập

---

## Nhóm 8 — Ambient Readings (Dữ liệu môi trường site)

Base route: `/api/ambient`

> Quản lý dữ liệu môi trường xung quanh site (ambient temperature, humidity, solar irradiance). Tách khỏi sensor readings cấp battery. Phục vụ phát hiện anomaly cấp site (`HighAmbientTemp`, `HighHumidity`, `HighTempHumidityCombo`).

---

### `POST /api/ambient/readings/batch`

**Mục đích:** Ingest batch ambient readings (ambient temperature, humidity, solar irradiance) từ IoT edge device hoặc `WeatherSyncBackgroundService` (OpenMeteo) — phục vụ phát hiện anomaly cấp site (`HighAmbientTemp`, `HighHumidity`, `HighTempHumidityCombo`).

**Auth:** API Key — scheme `ApiKey` + policy `EnvironmentalIngest` (không dùng JWT).

**Giới hạn batch:** Tối đa **100** readings mỗi request. Vượt → `400 isSuccess=false`.

**Request body:** Wrapper object có field `items` (mảng `AmbientReadingItem`).

```json
{
  "items": [
    {
      "siteId": "11111111-1111-1111-1111-111111111111",
      "time": "2026-06-12T08:00:00Z",
      "ambientTemperature": 34.2,
      "humidity": 72.5,
      "solarIrradiance": 580.0,
      "source": 1,
      "sourceDeviceId": "GATEWAY-AG01"
    }
  ]
}
```

**Field rules (cho mỗi item):**

| Field | Type | Bắt buộc | Validation | Mô tả |
|---|---|---|---|---|
| `siteId` | `Guid` | ✅ | Không được `Guid.Empty` | ID site |
| `time` | `DateTime` | ✅ | UTC | Timestamp đo lường |
| `ambientTemperature` | `decimal` | ✅ | -90 đến 90 | Nhiệt độ môi trường (°C) |
| `humidity` | `decimal?` | ❌ | 0–100 nếu truyền | Độ ẩm tương đối (%) |
| `solarIrradiance` | `decimal?` | ❌ | — | Cường độ bức xạ mặt trời (W/m²) |
| `source` | `AmbientReadingSourceEnum` | ❌ (mặc định `IotSensor` = 1) | enum hợp lệ | Nguồn dữ liệu — xem enum |
| `sourceDeviceId` | `string?` | ❌ | — | ID gateway / device gửi data |

**Response thành công `201 Created`:** `CommonResponse<int>` — `data` là số reading đã insert.

```json
{
  "isSuccess": true,
  "statusCode": 201,
  "data": 100
}
```

> Handler không set `message`. `data` = `request.Items.Count` (số reading client gửi lên — dedup phụ thuộc DB constraint / TimescaleDB hypertable, không filter ở application layer).

**Lỗi thường gặp:**
- `400` — `items` rỗng, vượt 100 record, hoặc item không hợp lệ (xem `listErrors` chi tiết từng item: `Items[0].SiteId`, `Items[0].Humidity`, …)
- `401` — Thiếu `X-Api-Key`
- `403` — ApiKey không có scope `EnvironmentalIngest`

---

### `GET /api/ambient/readings/history`

**Mục đích:** Lịch sử ambient readings của một site, dùng cho chart/timeline.

**Auth:** Bắt buộc (Admin/Manager/Staff/Customer)

**Query params:**

| Param | Type | Bắt buộc | Mô tả |
|---|---|---|---|
| `siteId` | `Guid` | ✅ | ID site |
| `from` | `DateTime?` | ❌ | Lọc `Time >= from` (UTC) |
| `to` | `DateTime?` | ❌ | Lọc `Time <= to` (UTC) |
| `pageNumber` | `int` | ❌ (mặc định 1) | Trang |
| `pageSize` | `int` | ❌ (mặc định 100) | Số item/trang |

> **Lưu ý:** Ambient readings tần suất thấp (theo giờ) nên dùng **offset pagination**, KHÔNG cursor như SensorReading. Sort `Time DESC`.

**Response thành công `200`:** `CommonResponse<PaginationResponse<AmbientReadingDto>>`

```json
{
  "isSuccess": true,
  "data": {
    "items": [
      {
        "time": "2026-06-12T08:00:00Z",
        "siteId": "guid",
        "ambientTemperature": 34.2,
        "humidity": 72.5,
        "solarIrradiance": 580.0,
        "source": 2,
        "sourceDeviceId": null
      }
    ],
    "totalItems": 240,
    "pageNumber": 1,
    "pageSize": 100,
    "totalPages": 3,
    "hasNextPage": true,
    "hasPreviousPage": false
  }
}
```

**Chi tiết `AmbientReadingDto`:**

| Field | Type | Nullable | Mô tả |
|---|---|---|---|
| `time` | `DateTime` | Không | Timestamp đo (UTC) |
| `siteId` | `string` | Không | ID site |
| `ambientTemperature` | `decimal` | Không | Nhiệt độ môi trường (°C) |
| `humidity` | `decimal?` | Có | Độ ẩm (%) |
| `solarIrradiance` | `decimal?` | Có | Cường độ bức xạ (W/m²) |
| `source` | `AmbientReadingSourceEnum` | Không | Nguồn (xem enum) — mặc định `WeatherApi` |
| `sourceDeviceId` | `string?` | Có | ID device/gateway |

---

### `GET /api/ambient/readings/latest`

**Mục đích:** Reading ambient mới nhất của một site — dùng cho dashboard widget (tile hiển thị nhiệt độ/độ ẩm hiện tại).

**Auth:** Bắt buộc (Admin/Manager/Staff/Customer)

**Query params:**

| Param | Type | Bắt buộc | Mô tả |
|---|---|---|---|
| `siteId` | `Guid` | ✅ | ID site |

**Response thành công `200`:** `CommonResponse<AmbientReadingDto>` — record có `Time` lớn nhất. Field rules giống `GET /history`.

```json
{
  "isSuccess": true,
  "data": {
    "time": "2026-06-12T08:00:00Z",
    "siteId": "guid",
    "ambientTemperature": 34.2,
    "humidity": 72.5,
    "solarIrradiance": 580.0,
    "source": 2,
    "sourceDeviceId": null
  }
}
```

**Lỗi thường gặp:**
- `404` — Site chưa có ambient reading nào

---

### `PUT /api/ambient/threshold-configs`

**Mục đích:** Upsert `AmbientThresholdConfig` cho 1 site (mỗi site có duy nhất 1 config active). Có config → cập nhật; chưa có → tạo mới.

**Auth:** Bắt buộc (Admin/Manager)

**Request body:**

```json
{
  "siteId": "11111111-1111-1111-1111-111111111111",
  "highAmbientTempWarning": 38.0,
  "highAmbientTempCritical": 45.0,
  "highHumidityWarning": 80.0,
  "highHumidityCritical": 90.0,
  "comboTempThreshold": 35.0,
  "comboHumidityThreshold": 75.0,
  "enabled": true
}
```

**Field rules:**

| Field | Type | Bắt buộc | Validation | Mô tả |
|---|---|---|---|---|
| `siteId` | `Guid` | ✅ | Không được `Guid.Empty` | ID site |
| `highAmbientTempWarning` | `decimal?` | ❌ | — | Ngưỡng cảnh báo nhiệt độ (°C) |
| `highAmbientTempCritical` | `decimal?` | ❌ | >= `highAmbientTempWarning` nếu cả 2 cùng truyền | Ngưỡng nguy hiểm nhiệt độ (°C) |
| `highHumidityWarning` | `decimal?` | ❌ | — | Ngưỡng cảnh báo độ ẩm (%) |
| `highHumidityCritical` | `decimal?` | ❌ | >= `highHumidityWarning` nếu cả 2 cùng truyền | Ngưỡng nguy hiểm độ ẩm (%) |
| `comboTempThreshold` | `decimal?` | ❌ | — | Ngưỡng nhiệt cho combo rule (temp ≥ X AND humidity ≥ Y → `HighTempHumidityCombo`) |
| `comboHumidityThreshold` | `decimal?` | ❌ | — | Ngưỡng ẩm cho combo rule |
| `enabled` | `bool` | ❌ (mặc định `true`) | — | Tắt nhanh threshold mà không xóa config |

> Tất cả threshold fields đều **nullable** — gửi `null` để bỏ qua không monitor metric đó. Combo rule chỉ active khi cả 2 field combo đều có giá trị.

**Response thành công `200`:** `CommonResponse<AmbientThresholdConfigDto>`

```json
{
  "isSuccess": true,
  "data": {
    "id": "guid",
    "siteId": "guid",
    "highAmbientTempWarning": 38.0,
    "highAmbientTempCritical": 45.0,
    "highHumidityWarning": 80.0,
    "highHumidityCritical": 90.0,
    "comboTempThreshold": 35.0,
    "comboHumidityThreshold": 75.0,
    "enabled": true,
    "createdAt": "2026-06-12T08:00:00Z"
  }
}
```

**Lỗi thường gặp:**
- `400` — `SiteId` rỗng, hoặc `Critical < Warning`

---

### `GET /api/ambient/threshold-configs/by-site/{siteId}`

**Mục đích:** Lấy threshold config hiện tại của 1 site (dùng cho trang config UI).

**Auth:** Bắt buộc (Admin/Manager)

**Path param:** `siteId` — Guid của site.

**Response thành công `200`:** `CommonResponse<AmbientThresholdConfigDto>` (shape giống response của PUT ở trên).

**Lỗi thường gặp:**
- `404` — Site chưa được cấu hình threshold (`data = null`, `isSuccess = false`)

---

### `GET /api/ambient/threshold-configs`

**Mục đích:** Liệt kê toàn bộ threshold config — trang Admin xem nhanh tất cả site đã cấu hình.

**Auth:** Bắt buộc (Admin/Manager)

**Query params:**

| Param | Type | Bắt buộc | Mô tả |
|---|---|---|---|
| `pageNumber` | `int` | ❌ (mặc định 1) | Trang |
| `pageSize` | `int` | ❌ (mặc định 50, max 200) | Số item/trang |

> **Lưu ý:** Endpoint **không** có filter động (siteId/enabled) — chỉ phân trang. Sort mặc định `CreatedAt DESC`. Filter `!IsDeleted` luôn áp dụng.

**Response thành công `200`:** `CommonResponse<PaginationResponse<AmbientThresholdConfigDto>>` (items shape giống response PUT).

---

## Nhóm 9 — Environmental Incidents (Sự cố môi trường site)

Base route: `/api/environmental-incidents`

> Quản lý vòng đời `EnvironmentalIncident` (smoke, fire, gas leak, flood, overheat hazard) ở cấp **site** — tách khỏi `Alert` cấp battery, nhưng Alert có thể reference incident qua `EnvironmentalIncidentId`.

---

### `POST /api/environmental-incidents`

**Mục đích:** Report incident mới từ IoT edge (smoke/fire/gas leak/flood/overheat hazard cấp site). Khi tạo thành công, hệ thống tự động phát `EnvironmentalIncidentDetectedEvent` để Notification + Ticket service consume (auto tạo Alert site-level + ticket P1).

**Auth:** API Key — scheme `ApiKey` + policy `EnvironmentalIngest`.

**Request body:**

```json
{
  "siteId": "11111111-1111-1111-1111-111111111111",
  "incidentType": 2,
  "severity": 3,
  "detectedAt": "2026-06-12T08:00:00Z",
  "reportedBy": "GATEWAY-AG01",
  "notes": "Cảm biến khói khu cabinet B kích hoạt."
}
```

**Field rules:**

| Field | Type | Bắt buộc | Validation | Mô tả |
|---|---|---|---|---|
| `siteId` | `Guid` | ✅ | Không `Guid.Empty` | ID site |
| `incidentType` | `EnvironmentalIncidentTypeEnum` | ✅ | Enum hợp lệ | Loại sự cố (xem enum) |
| `severity` | `AlertSeverityEnum` | ❌ (mặc định `Critical` = 3) | Enum hợp lệ | Mức độ nghiêm trọng (xem enum) |
| `detectedAt` | `DateTime` | ✅ | Không lệch quá `UtcNow + 5 phút` | Thời điểm phát hiện (UTC) |
| `reportedBy` | `string?` | ❌ | Max 256 ký tự | Định danh device/gateway hoặc operator báo cáo |
| `notes` | `string?` | ❌ | Max 1000 ký tự | Ghi chú/mô tả chi tiết |

**Response thành công — 2 paths:**

| Status | Path | Mô tả |
|---|---|---|
| `201 Created` | **Create** (normal path) | Incident mới được tạo. Status set `Open`, `acknowledgedAt`/`resolvedAt`/`falseAlarmAt` đều `null`. Hệ thống phát `EnvironmentalIncidentDetectedEvent` để Notification + Ticket service consume (auto tạo Alert site-level + ticket P1). |
| `200 OK` | **Dedup** (idempotency) | Đã tồn tại incident active đang `Open`/`Acknowledged` cho cùng `SiteId` + cùng `IncidentType` → trả lại incident cũ thay vì tạo mới. KHÔNG phát event lần nữa. Idempotency-friendly cho IoT gateway spam cùng sự cố. |

Cả 2 path đều trả `CommonResponse<EnvironmentalIncidentDto>` — payload đầy đủ incident (shape giống `GET /{id}` bên dưới).

**Lỗi thường gặp:**
- `400` — `SiteId` rỗng, `IncidentType`/`Severity` không hợp lệ, `DetectedAt` rỗng/vượt quá hiện tại 5'
- `401` — Thiếu `X-Api-Key`
- `403` — ApiKey không có scope `EnvironmentalIngest`

---

### `POST /api/environmental-incidents/manual`

**Sprint Bonus NS-23 (#663, E3).**

**Mục đích / tác dụng:** cho **con người** (Staff/Manager/Admin đứng tại site) report sự cố môi trường **thủ công bằng JWT** khi thấy cháy/khói/ngập mà **không có sensor tự động** — đặc biệt các loại chỉ con người phát hiện được (`FireDetected`/`OverheatHazard`/`Other`). Trước fix, endpoint `POST /` chỉ nhận ApiKey (IoT) nên không ai tạo tay được. Reuse cùng handler với `POST /` → cũng tạo Alert site-level + publish `EnvironmentalIncidentDetectedEvent` (→ auto-ticket P1 NS-22).

**Auth:** JWT — role `Admin` / `Manager` / `Staff`. Customer **KHÔNG** được phép.

**Khác biệt so với `POST /` (ApiKey IoT):**
- `reportedBy` trong body **bị bỏ qua** — backend luôn ghi đè bằng user id từ token (chống mạo danh).
- `detectedAt` **không bắt buộc** — mặc định `UtcNow` nếu bỏ trống (con người report không cần điền giờ).

**Request body:**
```json
{
  "siteId": "11111111-1111-1111-1111-111111111111",
  "incidentType": 2,
  "severity": 3,
  "notes": "Nhân viên phát hiện lửa tại cabinet B khu vực 2."
}
```

| Field | Type | Bắt buộc | Validation | Mô tả |
|---|---|---|---|---|
| `siteId` | `Guid` | ✅ | Không `Guid.Empty` | ID site xảy ra sự cố |
| `incidentType` | `EnvironmentalIncidentTypeEnum` (int) | ✅ | Enum hợp lệ | Loại sự cố (xem `EnvironmentalIncidentTypeEnum`: 1 Smoke · 2 FireDetected · 3 GasLeak · 4 Flood · 5 OverheatHazard · 99 Other) |
| `severity` | `AlertSeverityEnum` (int) | ❌ (mặc định `Critical` = 3) | Enum hợp lệ | Mức nghiêm trọng: `1` Info · `2` Warning · `3` Critical |
| `detectedAt` | `DateTime?` | ❌ (mặc định `UtcNow`) | Không vượt `UtcNow + 5'` | Thời điểm phát hiện (UTC) |
| `reportedBy` | `string?` | ❌ (**bị bỏ qua**) | — | **Không dùng** — backend lấy từ token |
| `notes` | `string?` | ❌ | Max 1000 ký tự | Ghi chú/mô tả chi tiết |

**Response thành công — 2 path (giống `POST /`):**

| Status | Path | Mô tả |
|---|---|---|
| `201 Created` | Create | Incident mới tạo (`Status = Open`). Publish `EnvironmentalIncidentDetectedEvent` → Notification + auto Alert site-level + auto-ticket P1 (NS-22). |
| `200 OK` | Dedup | Đã có incident active `Open`/`Acknowledged` cùng `SiteId`+`IncidentType` → trả incident cũ, KHÔNG phát event lần nữa. |

Cả 2 path trả `CommonResponse<EnvironmentalIncidentDto>` (shape giống `GET /{id}`).

**Lỗi thường gặp:**
- `400` — `SiteId` rỗng, `IncidentType`/`Severity` không hợp lệ → field-level (`listErrors: [{ field, detail }]`)
- `401` — Chưa đăng nhập
- `403` — Role không nằm trong Admin/Manager/Staff

---

### `POST /api/environmental-incidents/{id}/acknowledge`

**Mục đích:** Acknowledge incident — chuyển state `Open → Acknowledged`. Hệ thống set `acknowledgedBy = userId hiện tại (từ JWT)` và `acknowledgedAt = UtcNow`.

**Auth:** Bắt buộc (Admin/Manager/Staff). Customer KHÔNG được phép.

**Path param:** `id` — Guid của incident.

**Request body:** Không có.

**Response thành công `200`:** `CommonResponse<EnvironmentalIncidentDto>` — trả incident sau khi update (status mới `Acknowledged`, `acknowledgedAt` có giá trị).

**Lỗi thường gặp:**
- `400` — `id` rỗng hoặc JWT không có UserId hợp lệ
- `401` — Chưa đăng nhập
- `403` — Role không nằm trong Admin/Manager/Staff
- `404` — Incident không tồn tại
- `409` — State hiện tại không phải `Open` (đã `Acknowledged`/`Resolved`/`FalseAlarm`)

---

### `POST /api/environmental-incidents/{id}/resolve`

**Mục đích:** Resolve incident — chuyển state `Open` hoặc `Acknowledged → Resolved`. Set `resolvedBy = userId hiện tại`, `resolvedAt = UtcNow`, lưu `resolutionNote` (audit trail).

**Auth:** Bắt buộc (Admin/Manager/Staff). Customer KHÔNG được phép.

**Path param:** `id` — Guid của incident.

**Request body:**

```json
{
  "resolutionNote": "Đã bơm thoát nước cabinet, đo điện trở cách điện đạt chuẩn."
}
```

**Field rules:**

| Field | Type | Bắt buộc | Validation | Mô tả |
|---|---|---|---|---|
| `resolutionNote` | `string` | ✅ | 5–2000 ký tự, không whitespace | Mô tả cách xử lý — audit trail bắt buộc |

**Response thành công `200`:** `CommonResponse<EnvironmentalIncidentDto>` — incident sau update (status `Resolved`, `resolvedAt` và `resolutionNote` có giá trị).

**Lỗi thường gặp:**
- `400` — Thiếu/quá ngắn/quá dài `resolutionNote`, `id` rỗng
- `401` — Chưa đăng nhập
- `404` — Incident không tồn tại
- `409` — State đang là `Resolved` / `FalseAlarm`

---

### `POST /api/environmental-incidents/{id}/false-alarm`

**Mục đích:** Đánh dấu `FalseAlarm` — incident không thật (ví dụ sương khói do vệ sinh module bằng cồn). Set `falseAlarmBy` = user hiện tại, `falseAlarmAt = UtcNow`, lưu `falseAlarmReason`.

**Auth:** Bắt buộc (chỉ Admin/Manager — Staff/Customer không được phép, để tránh lạm dụng).

> **Side-effect (Sprint Bonus NS-22 #662):** endpoint này publish `EnvironmentalIncidentResolvedEvent` với `wasFalseAlarm = true` → **TicketService tự đóng ticket** đã auto-tạo từ incident này (chuyển sang `ClosedRejected`, dừng SLA timer). Genuine resolve (`POST /{id}/resolve`) **không** auto-close — Staff đóng theo quy trình chuẩn.

**Path param:** `id` — Guid của incident.

**Request body:**

```json
{
  "falseAlarmReason": "Vệ sinh module bằng cồn — bay hơi giả gas leak."
}
```

**Field rules:**

| Field | Type | Bắt buộc | Validation | Mô tả |
|---|---|---|---|---|
| `falseAlarmReason` | `string` | ✅ | 5–2000 ký tự | Lý do đánh dấu false alarm — audit trail |

**Response thành công `200`:** `CommonResponse<EnvironmentalIncidentDto>` — status `FalseAlarm`, `falseAlarmAt` + `falseAlarmReason` có giá trị.

**Lỗi thường gặp:**
- `400` — Thiếu/quá ngắn/quá dài `falseAlarmReason`
- `401` — Chưa đăng nhập
- `403` — Role không nằm trong Admin/Manager
- `404` — Incident không tồn tại
- `409` — State đã ở terminal (`Resolved` hoặc `FalseAlarm`)

---

### `GET /api/environmental-incidents`

**Mục đích:** Liệt kê incident (filter + phân trang). Sort mặc định `DetectedAt DESC`. Filter `!IsDeleted` luôn áp dụng.

**Auth:** Bắt buộc (Admin/Manager/Staff/Customer)

**Query params:**

| Param | Type | Bắt buộc | Mô tả |
|---|---|---|---|
| `pageNumber` | `int` | ❌ (mặc định 1) | Trang |
| `pageSize` | `int` | ❌ (mặc định 50) | Số item/trang |
| `siteId` | `Guid?` | ❌ | Lọc theo site |
| `status` | `EnvironmentalIncidentStatusEnum?` | ❌ | Lọc theo trạng thái (xem enum) |
| `incidentType` | `EnvironmentalIncidentTypeEnum?` | ❌ | Lọc theo loại sự cố |
| `from` | `DateTime?` | ❌ | Range `DetectedAt >= from` (UTC) |
| `to` | `DateTime?` | ❌ | Range `DetectedAt <= to` (UTC) |

> **Lưu ý:** Endpoint hiện **không** filter theo `severity` (mặc dù enum tồn tại). Nếu cần, FE filter client-side.

**Response thành công `200`:** `CommonResponse<PaginationResponse<EnvironmentalIncidentDto>>` (mỗi item shape giống `GET /{id}` bên dưới).

---

### `GET /api/environmental-incidents/{id}`

**Mục đích:** Chi tiết incident — đầy đủ lifecycle (Acknowledged/Resolved/FalseAlarm timestamps + actor) phục vụ trang chi tiết.

**Auth:** Bắt buộc (Admin/Manager/Staff/Customer)

**Path param:** `id` — Guid của incident.

**Response thành công `200`:** `CommonResponse<EnvironmentalIncidentDto>`

```json
{
  "isSuccess": true,
  "data": {
    "id": "guid",
    "siteId": "guid",
    "incidentType": 2,
    "status": 3,
    "severity": 3,
    "reportedBy": "GATEWAY-AG01",
    "detectedAt": "2026-06-12T08:00:00Z",
    "acknowledgedAt": "2026-06-12T08:05:00Z",
    "resolvedAt": "2026-06-12T09:30:00Z",
    "resolutionNote": "Đã dập hoàn toàn, không thiệt hại pin.",
    "falseAlarmAt": null,
    "falseAlarmReason": null,
    "createdAt": "2026-06-12T08:00:01Z"
  }
}
```

**Chi tiết `EnvironmentalIncidentDto`:**

| Field | Type | Nullable | Mô tả |
|---|---|---|---|
| `id` | `string` | Không | ID incident |
| `siteId` | `string` | Không | ID site phát sinh |
| `incidentType` | `EnvironmentalIncidentTypeEnum` | Không | Loại (xem enum) |
| `status` | `EnvironmentalIncidentStatusEnum` | Không | Trạng thái lifecycle (xem enum) |
| `severity` | `AlertSeverityEnum` | Không | Mức độ nghiêm trọng |
| `reportedBy` | `string?` | Có | Device/gateway/operator báo cáo |
| `detectedAt` | `DateTime` | Không | Thời điểm phát hiện (UTC) |
| `acknowledgedAt` | `DateTime?` | Có | Thời điểm acknowledge |
| `resolvedAt` | `DateTime?` | Có | Thời điểm resolve |
| `resolutionNote` | `string?` | Có | Ghi chú resolve (`null` khi chưa resolve) |
| `falseAlarmAt` | `DateTime?` | Có | Thời điểm đánh dấu false alarm |
| `falseAlarmReason` | `string?` | Có | Lý do false alarm (`null` khi chưa mark) |
| `createdAt` | `DateTime` | Không | Thời điểm tạo record |

**Lỗi thường gặp:**
- `404` — Incident không tồn tại hoặc đã soft-delete

---

### `GET /api/environmental-incidents/by-site/{siteId}/active`

**Mục đích:** Liệt kê incident đang Active (`Open` hoặc `Acknowledged`) của 1 site — dùng cho dashboard site (widget cảnh báo đang xảy ra).

**Auth:** Bắt buộc (Admin/Manager/Staff/Customer)

**Path param:** `siteId` — Guid của site.

**Response thành công `200`:** `CommonResponse<PaginationResponse<EnvironmentalIncidentDto>>` — wrapper PaginationResponse nhưng chỉ trả các record active (không phân trang theo client). Items shape giống `GET /{id}`.

---

## Nhóm 10 — Health

Base route: `/api/battery/health`

---

### `GET /api/battery/health`

**Mục đích:** Liveness probe đơn giản cho Docker/Kubernetes/ApiGateway aggregated health. Endpoint anonymous, không validate DB/RabbitMQ/Redis.

**Auth:** Không yêu cầu (public).

**Response thành công `200`:**

```json
{
  "status": "Healthy",
  "service": "BatteryService",
  "timestamp": "2026-06-12T08:00:00Z"
}
```

| Field | Type | Mô tả |
|---|---|---|
| `status` | `string` | Luôn `"Healthy"` nếu reach được endpoint |
| `service` | `string` | Tên service (`"BatteryService"`) |
| `timestamp` | `DateTime` | UTC hiện tại |

---

## Nhóm 11 — IoT Device Management (Quản lý thiết bị IoT)

> Quản lý vòng đời IoT edge device (ESP32-S3), API key per-device, calibration cảm biến, và OTA firmware. Có **2 kênh auth khác nhau**:
> - **ApiKey per-device** (`X-Api-Key: iotk_...`, scheme `ApiKey`): các endpoint device tự gọi (`api/iot-devices/provision|heartbeat|firmware-check|firmware-update-log`). Mỗi endpoint yêu cầu một `IotApiKeyScopeEnum` cụ thể. `DeviceId`/`DeviceCode` lấy từ claim của key — client **không** gửi qua body.
> - **JWT Bearer** (role-based): calibration (`api/iot-devices/...`) và toàn bộ admin (`api/admin/iot-devices`, `api/admin/iot-firmware-releases`).
>
> **Lưu ý chung:** 3 command device-facing (`provision`, `heartbeat`, `firmware-update-log`) **không có** validation layer riêng — các giới hạn mô tả là ý định, được enforce ở handler (404/403/409/422). 401/403 do middleware ApiKey/JWT trả.

---

### 11A. Device self-service (auth: ApiKey per-device)

Base route: `/api/iot-devices`

#### `POST /api/iot-devices/provision`

**Mục đích:** Device báo đã boot xong — provision lần đầu hoặc sau khi flash firmware. Gọi 1 lần sau mỗi boot/reset.

**Auth:** ApiKey, scope `DeviceHeartbeat`.

**Header tùy chọn:** `X-Device-Code` — cross-check với DeviceCode của key; mismatch → `403`.

**Request body:**

| Field | Type | Bắt buộc | Mô tả |
|---|---|---|---|
| `firmwareVersion` | `string` | Có | Version firmware đang chạy (e.g., `1.2.3`) |
| `hardwareRevision` | `string?` | Không | Nếu khác giá trị đăng ký → overwrite |
| `deviceTimestamp` | `DateTime` | Có | Timestamp tại device (UTC) — backend kiểm tra clock skew |

> `deviceId`/`deviceCode` server tự gán từ claim của API key.

**Response thành công `200`:** `CommonResponse<IotDeviceProvisionResultDto>`

| Field | Type | Mô tả |
|---|---|---|
| `deviceId` | `string` | ID device |
| `deviceCode` | `string` | Mã device |
| `siteId` | `string` | ID site |
| `heartbeatIntervalSeconds` | `int` | Tần suất heartbeat khuyến nghị |
| `apiKeyScopes` | `IotApiKeyScopeEnum` | Bitmask scopes của key |
| `targetFirmwareVersion` | `string?` | Version target OTA (nếu có) |
| `pollingIntervalSeconds` | `int` | Tần suất poll sensor (mặc định 10) |
| `ntpServer` | `string` | NTP server để sync clock (mặc định `time.google.com`) |
| `batteryMappings` | `BatteryMappingEntry[]` | Mapping `batteryAssetSerial` → `unitId` (Modbus) + `sensorSourceCode` |
| `supportedSensors` | `string[]` | Loại sensor được phép push |

**Lỗi thường gặp:**
- `401` — Thiếu/sai key hoặc thiếu scope `DeviceHeartbeat`
- `403` — `X-Device-Code` / DeviceCode không khớp key
- `404` — Device không tồn tại
- `409` — Device đang `Disabled`/`Decommissioned`
- `422` — Clock skew vượt 300 giây (5 phút)

---

#### `POST /api/iot-devices/heartbeat`

**Mục đích:** Telemetry health định kỳ (khuyến nghị 60s). Backend phát hiện offline + hint OTA available. **Không** reject khi skew vượt ngưỡng — chỉ raise warning.

**Auth:** ApiKey, scope `DeviceHeartbeat`.

**Request body:**

| Field | Type | Bắt buộc | Mô tả |
|---|---|---|---|
| `firmwareVersion` | `string?` | Không | Nếu có → cập nhật `CurrentFirmwareVersion` |
| `rssiDbm` | `int?` | Không | WiFi RSSI (dBm). Alias: `signalStrengthDbm` |
| `freeMemoryPercent` | `decimal?` | Không | % RAM free [0..100] |
| `uptimeSeconds` | `long?` | Không | Uptime từ lần boot cuối |
| `queuedReadingCount` | `int?` | Không | Số reading queue local chưa upload. Alias: `localQueueDepth` |
| `deviceTimestamp` | `DateTime` | Có | Backend tính skew |
| `cpu` | `decimal?` | Không | CPU usage 0..100 (ESP32 gửi null) |
| `diskFreeMb` | `long?` | Không | Free disk (MB) |
| `temperature` | `decimal?` | Không | Nhiệt độ MCU (°C) |
| `memoryUsageMb` | `long?` | Không | Memory usage (MB) |

> **Alias field:** `signalStrengthDbm` ↔ `rssiDbm`, `localQueueDepth` ↔ `queuedReadingCount` cùng trỏ về một giá trị — khi serialize JSON cả hai tên đều xuất hiện.

**Response thành công `200`:** `CommonResponse<IotHeartbeatAckDto>`

| Field | Type | Mô tả |
|---|---|---|
| `serverTime` | `DateTime` | Thời gian server |
| `clockSkewSeconds` | `double` | Skew hiện tại |
| `clockSkewWarning` | `bool` | `true` nếu skew > 5 phút |
| `nextHeartbeatInSeconds` | `int` | Tần suất khuyến nghị lần kế |
| `firmwareUpdateAvailable` | `bool` | `true` nếu target khác current (hint — vẫn phải gọi `firmware-check`) |

**Lỗi thường gặp:**
- `401` — Thiếu/sai key hoặc thiếu scope
- `404` — Device không tồn tại
- `409` — Device `Disabled`/`Decommissioned` hoặc key đã revoke

---

#### `GET /api/iot-devices/firmware-check`

**Mục đích:** Device poll (mỗi ~1h) check firmware mới. Nếu có update → tạo `IotFirmwareUpdateLog` mới `Pending` để track.

**Auth:** ApiKey, scope `FirmwareCheck`.

**Query params:**

| Param | Type | Bắt buộc | Mô tả |
|---|---|---|---|
| `currentVersion` | `string` | Có (null → coi như rỗng) | Version đang chạy |

> Trả `updateAvailable=false` nếu: không có target, target chưa publish / đã archive, hoặc target version == currentVersion.

**Response thành công `200`:** `CommonResponse<IotFirmwareCheckDto>`

| Field | Type | Mô tả |
|---|---|---|
| `updateAvailable` | `bool` | Có update hay không. Alias: `hasUpdate` |
| `targetVersion` | `string?` | Version target |
| `artifactUrl` | `string?` | URL `.bin`. Alias: `downloadUrl` |
| `sha256Checksum` | `string?` | SHA-256 hex (64 ký tự) |
| `artifactSizeBytes` | `long?` | Kích thước artifact |
| `updateLogId` | `string?` | Id log để device PUT progress |
| `releaseNotes` | `string?` | Changelog markdown |
| `isRequired` | `bool` | Force update flag |
| `channel` | `IotFirmwareChannelEnum` | `Stable`/`Beta` |

**Lỗi thường gặp:**
- `401` — Thiếu/sai key hoặc thiếu scope
- `404` — Device không tồn tại

---

#### `PUT /api/iot-devices/firmware-update-log/{id}`

**Mục đích:** Device báo cáo progress OTA flash. Gửi nhiều lần (`Pending → Downloading → Installing → Success`).

**Auth:** ApiKey, scope `FirmwareCheck`.

**Path param:** `id` — Guid của `IotFirmwareUpdateLog` (nhận từ `firmware-check`).

**Request body:**

| Field | Type | Bắt buộc | Mô tả |
|---|---|---|---|
| `status` | `IotFirmwareUpdateStatusEnum` | Có | Trạng thái OTA (xem enum) |
| `bytesDownloaded` | `long?` | Không | Progress bar |
| `failureReason` | `string?` | Không | Lý do lỗi khi `Failed` |

> Khi `Success` → cập nhật `IotDevice.CurrentFirmwareVersion` = version của release.

**Response thành công `200`:** `CommonResponse<object>` (message)

**Lỗi thường gặp:**
- `401` — Thiếu/sai key hoặc thiếu scope
- `404` — Log không tồn tại hoặc không thuộc device đang gọi

---

### 11B. Device calibration (auth: JWT)

Base route: `/api/iot-devices` (cùng prefix với 11A nhưng auth bằng JWT role)

> POST/DELETE tự invalidate Redis cache `iot:calibration:{deviceId}`.

#### `GET /api/iot-devices/by-code/{deviceCode}`

**Mục đích:** Tra cứu device theo `deviceCode` (mã in trên thân thiết bị, vd `ESP32-SIM-001`) để lấy `id` (GUID). Các route calibration bên dưới đều keyed theo `{deviceId}` = GUID, trong khi Staff cầm thiết bị chỉ đọc được `deviceCode` — endpoint này là cầu nối `deviceCode → deviceId` cho Staff/Manager (list admin `GET /api/admin/iot-devices` yêu cầu role `Admin` nên Staff không gọi được).

**Auth:** Bắt buộc (Admin/Manager/Staff)

**Route params:** `deviceCode` (`string`) — chuẩn hoá `Trim().ToUpperInvariant()` rồi match exact trên unique index `idx_iot_devices_device_code`, nên **case-insensitive** và bỏ qua khoảng trắng thừa.

**Response thành công `200`:** `CommonResponse<IotDeviceDto>` (xem shape `IotDeviceDto` ở §11C — gồm `id` GUID, `deviceCode`, `displayName`, `status`, `siteName`...).

**Lỗi thường gặp:**
- `404` — Không có device khớp `deviceCode` (hoặc đã decommission / soft-delete)

> Luồng mobile (GH-56): đọc `deviceCode` trên thiết bị → gọi endpoint này lấy `id` → dùng `id` cho `GET/POST/DELETE .../calibrations`.

---

**`IotDeviceCalibrationDto`:**

| Field | Type | Nullable | Mô tả |
|---|---|---|---|
| `id` | `string` | Không | ID calibration |
| `iotDeviceId` | `string` | Không | ID device |
| `channel` | `string` | Không | Channel sensor (`voltage`/`current`/`temperature`/`soc`) |
| `batteryAssetId` | `string?` | Có | Gắn cho 1 pin cụ thể; null = device-level |
| `scale` | `decimal` | Không | Hệ số nhân |
| `offset` | `decimal` | Không | Hệ số cộng |
| `unit` | `string` | Không | Đơn vị (`V`/`A`/`°C`/`%`) |
| `calibratedAt` | `DateTime` | Không | Ngày calibration thực tế (UTC) |
| `expiresAt` | `DateTime?` | Có | Ngày hết hạn |
| `notes` | `string?` | Có | Ghi chú technician |
| `createdAt` | `DateTime` | Không | Thời điểm tạo (UTC) |

#### `GET /api/iot-devices/{deviceId}/calibrations`

**Auth:** Bắt buộc (Admin/Manager/Staff)

**Query params:** `channel` (`string?`, filter case-insensitive), `includeExpired` (`bool`, mặc định `false`).

> Flat list, **không** phân trang, sort `calibratedAt DESC`. `deviceId` sai → trả mảng rỗng (không 404).

**Response thành công `200`:** `CommonResponse<IotDeviceCalibrationDto[]>`

---

#### `POST /api/iot-devices/{deviceId}/calibrations`

**Auth:** Bắt buộc (Admin/Staff)

**Request body:**

| Field | Type | Bắt buộc | Validation | Mô tả |
|---|---|---|---|---|
| `channel` | `string` | Có | Max 32 ký tự | Channel sensor (lowercase) |
| `batteryAssetId` | `Guid?` | Không | — | null = calibration cấp device |
| `scale` | `decimal` | Có (mặc định 1) | **khác 0** | Hệ số nhân |
| `offset` | `decimal` | Có (mặc định 0) | — | Hệ số cộng |
| `unit` | `string` | Có | Max 16 ký tự | Đơn vị |
| `calibratedAt` | `DateTime` | Có | `!= default` | Ngày calibration (UTC) |
| `expiresAt` | `DateTime?` | Không | > `calibratedAt` nếu truyền | Ngày hết hạn |
| `notes` | `string?` | Không | Max 500 ký tự | Ghi chú |

**Response thành công `201`:** `CommonResponse<IotDeviceCalibrationDto>`

**Lỗi thường gặp:**
- `400` — Validation lỗi (xem `listErrors`)
- `404` — Device không tồn tại hoặc đã xóa

---

#### `DELETE /api/iot-devices/{deviceId}/calibrations/{calibrationId}`

**Auth:** Bắt buộc (Admin/Staff)

**Response thành công `200`:** `CommonResponse<object>` (message)

**Lỗi thường gặp:**
- `404` — Calibration không tồn tại, đã xóa, hoặc không thuộc `deviceId`

---

#### `GET /api/iot-devices/calibrations-expiring`

**Mục đích:** List calibration sắp hết hạn trong N ngày tới (cross-device, Manager dashboard).

**Auth:** Bắt buộc (Admin/Manager)

**Query params:**

| Param | Type | Bắt buộc | Mô tả |
|---|---|---|---|
| `within` | `int?` | Không (mặc định 30, clamp `[1..365]`) | Số ngày tới |

> Flat list, **không** phân trang, sort `expiresAt ASC`. Chỉ trả calibration có `expiresAt` trong khoảng `(now, now + within]` — đã hết hạn hoặc không set `expiresAt` không hiển thị.

**Response thành công `200`:** `CommonResponse<IotDeviceCalibrationDto[]>`

---

### 11C. Admin — IoT Devices (auth: JWT Admin)

Base route: `/api/admin/iot-devices` — toàn bộ yêu cầu role `Admin`.

**`IotDeviceDto`:**

| Field | Type | Nullable | Mô tả |
|---|---|---|---|
| `id` | `string` | Không | ID device |
| `deviceCode` | `string` | Không | Mã device duy nhất |
| `displayName` | `string` | Không | Tên hiển thị |
| `siteId` | `string` | Không | ID site |
| `siteName` | `string?` | Có | Tên site |
| `hardwareRevision` | `string?` | Có | Phiên bản phần cứng |
| `status` | `IotDeviceStatusEnum` | Không | Trạng thái (xem enum) |
| `currentFirmwareVersion` | `string?` | Có | Version đang chạy |
| `targetFirmwareReleaseId` | `string?` | Có | ID release target OTA |
| `targetFirmwareVersion` | `string?` | Có | Version target |
| `apiKeyScopes` | `IotApiKeyScopeEnum` | Không | Bitmask scopes |
| `apiKeyLastFour` | `string` | Không | 4 ký tự cuối key (UI hint) |
| `apiKeyIssuedAt` | `DateTime` | Không | Thời điểm cấp key |
| `apiKeyRevokedAt` | `DateTime?` | Có | null nếu key còn hiệu lực |
| `lastSeenAt` | `DateTime?` | Có | Heartbeat gần nhất |
| `lastProvisionedAt` | `DateTime?` | Có | Provision gần nhất |
| `lastOfflineAt` | `DateTime?` | Có | Lần cuối chuyển Offline |
| `heartbeatIntervalSeconds` | `int` | Không | Tần suất heartbeat |
| `lastClockSkewSeconds` | `double?` | Có | Skew device vs server |
| `notes` | `string?` | Có | Ghi chú |
| `createdAt` | `DateTime` | Không | Thời điểm tạo (UTC) |

> **`IotDeviceCreatedDto`** (trả khi tạo mới + rotate-key) = `IotDeviceDto` + các field bí mật **chỉ trả 1 lần**: `rawApiKey`, `provisioningQrCode` (`iot://provision?dc={deviceCode}&key={rawApiKey}`), `mqttUsername`, `mqttPassword`, `mqttBrokerHost`, `mqttBrokerPort`.

> **`IotDeviceDetailDto`** (trả bởi `GET /api/admin/iot-devices/{id}`) = `IotDeviceDto` + `apiKey` (`string?`) — **full plaintext API key**, xem lại được nhiều lần (khác `rawApiKey` của create/rotate). `null` với device cũ chưa lưu plaintext (rotate-key để populate). List **không** có field này.

#### `GET /api/admin/iot-devices`

**Auth:** Admin

**Query params:** `siteId` (`Guid?`), `status` (`IotDeviceStatusEnum?`), `keyword` (`string?` — tìm trong `deviceCode` + `displayName`), `pageNumber` (mặc định 1), `pageSize` (mặc định 10, clamp `[1,100]`), `isDescending` (mặc định `true`, sort theo `createdAt`).

**Response thành công `200`:** `CommonResponse<PaginationResponse<IotDeviceDto>>`

---

#### `GET /api/admin/iot-devices/{id}`

**Mục đích:** Lấy chi tiết 1 device kèm **full plaintext API key** để Admin xem lại và flash vào ESP32. Đây là endpoint **duy nhất** trả full key ngoài lúc create/rotate — endpoint `list` chỉ trả `apiKeyLastFour`.

**Auth:** Admin

**Route param:** `id` (`Guid`, bắt buộc) — chỉ bind từ route (`[JsonIgnore][BindNever]`).

**Response thành công `200`:** `CommonResponse<IotDeviceDetailDto>`

> **`IotDeviceDetailDto`** = tất cả field của `IotDeviceDto` (bảng trên) **+ 1 field bổ sung**:
>
> | Field | Type | Nullable | Mô tả |
> |---|---|---|---|
> | `apiKey` | `string?` | Có | **Full plaintext API key** (prefix `iotk_`, ~47 ký tự). `null` nếu device được tạo **trước** khi bật lưu plaintext key (DB cũ chỉ giữ hash SHA-256, không backfill được) — gọi `POST /{id}/rotate-key` để sinh key mới + lưu plaintext. |

**Ví dụ response `200`:**

```json
{
  "isSuccess": true,
  "statusCode": 200,
  "message": null,
  "data": {
    "id": "3f2504e0-4f89-41d3-9a0c-0305e82c3301",
    "deviceCode": "ESP32-001",
    "displayName": "Site A - Rack 1 gateway",
    "siteId": "9b1deb4d-3b7d-4bad-9bdd-2b0d7b3dcb6d",
    "siteName": "Site A",
    "status": 2,
    "apiKeyScopes": 11,
    "apiKeyLastFour": "aB12",
    "apiKey": "iotk_9xK3...full-key-here...aB12",
    "apiKeyIssuedAt": "2026-07-16T14:00:00Z",
    "apiKeyRevokedAt": null,
    "heartbeatIntervalSeconds": 60,
    "createdAt": "2026-07-16T13:55:00Z"
  }
}
```

**Lỗi:** `404` — không tìm thấy / đã soft-delete (message-only, `listErrors` null)

---

#### `POST /api/admin/iot-devices`

**Mục đích:** Tạo device mới + sinh API key per-device + MQTT credential. Raw key + raw MQTT password trả **đúng 1 lần**. Device khởi tạo `Status = Pending`.

**Auth:** Admin

**Request body:**

| Field | Type | Bắt buộc | Validation | Mô tả |
|---|---|---|---|---|
| `deviceCode` | `string` | Có | 3–64 ký tự, regex `^[A-Z0-9-]+$`, unique (case-insensitive) | Mã device |
| `displayName` | `string` | Có | Max 200 ký tự | Tên hiển thị |
| `siteId` | `Guid` | Có | `!= Guid.Empty`, Site phải tồn tại & chưa xóa | Site |
| `hardwareRevision` | `string?` | Không | Max 64 ký tự | e.g., `v1.0-S3-MAX485` |
| `apiKeyScopes` | `IotApiKeyScopeEnum` | Không (mặc định `EdgeDeviceDefault` = 11) | `!= None` | Bitmask scopes |
| `heartbeatIntervalSeconds` | `int` | Không (mặc định 60) | `[10, 3600]` | Tần suất heartbeat |
| `notes` | `string?` | Không | Max 1000 ký tự | Ghi chú |

**Response thành công `201`:** `CommonResponse<IotDeviceCreatedDto>`

**Lỗi thường gặp:**
- `400` — Validation lỗi
- `404` — Site không tồn tại
- `409` — `deviceCode` đã tồn tại

---

#### `PUT /api/admin/iot-devices/{id}`

**Mục đích:** Cập nhật metadata + status + scopes + target firmware. **Không** đổi API key.

**Auth:** Admin

**Request body:**

| Field | Type | Bắt buộc | Validation | Mô tả |
|---|---|---|---|---|
| `displayName` | `string` | Có | Max 200 ký tự | — |
| `siteId` | `Guid` | Có | `!= Guid.Empty`, tồn tại & chưa xóa | Đổi site |
| `hardwareRevision` | `string?` | Không | Max 64 ký tự | — |
| `status` | `IotDeviceStatusEnum` | Có | enum hợp lệ | Cho phép set thủ công `Disabled`/`Decommissioned` |
| `apiKeyScopes` | `IotApiKeyScopeEnum` | Không (mặc định `EdgeDeviceDefault`) | `!= None` | Bitmask |
| `heartbeatIntervalSeconds` | `int` | Không (mặc định 60) | `[10, 3600]` | — |
| `targetFirmwareReleaseId` | `Guid?` | Không | Phải tồn tại, đã publish, chưa archive | Set target OTA |
| `notes` | `string?` | Không | Max 1000 ký tự | — |

**Response thành công `200`:** `CommonResponse<IotDeviceDto>`

**Lỗi thường gặp:**
- `400` — Validation lỗi
- `404` — Device / Site / Firmware release không tồn tại
- `409` — Target firmware chưa publish hoặc đã archive

---

#### `DELETE /api/admin/iot-devices/{id}`

**Mục đích:** Decommission — soft-delete + set `Status=Decommissioned` + revoke key. Calibration cascade soft-delete; heartbeat history giữ nguyên.

**Auth:** Admin

**Response thành công `200`:** `CommonResponse<object>`

---

#### `POST /api/admin/iot-devices/{id}/rotate-key`

**Mục đích:** Sinh API key 256-bit mới (prefix `iotk_`), thay hash, reset issuedAt, bỏ revoke. **Không** đổi `Status`. Raw key mới trả 1 lần.

**Auth:** Admin

**Response thành công `200`:** `CommonResponse<IotDeviceCreatedDto>` — lưu ý success code là **200** (không phải 201) dù sinh key mới.

---

#### `POST /api/admin/iot-devices/{id}/revoke-key`

**Mục đích:** Block device khỏi mọi request — set `apiKeyRevokedAt = UtcNow` + `Status = Disabled`. Device vẫn còn trong list.

**Auth:** Admin

**Response thành công `200`:** `CommonResponse<object>`

---

#### `POST /api/admin/iot-devices/{id}/command`

**Mục đích:** Push downlink command tới device qua MQTT topic `solar/{deviceCode}/cmd`; device ack qua `.../cmd/ack`. Backend chỉ relay JSON, không validate sâu.

**Auth:** Admin

**Request body:**

| Field | Type | Bắt buộc | Mô tả |
|---|---|---|---|
| `cmdId` | `string?` | Không | Idempotency key; backend sinh GUID nếu null/blank |
| `type` | `string` | **Có** | Loại command — xem bảng dưới. Kiểu chuỗi tự do, backend KHÔNG kiểm |
| `params` | `object?` | Không | Param JSON, tuỳ theo `type` |

**Firmware hiện chỉ hiểu BA loại** — nguồn: `iot/firmware-esp32/src/cmd/cmd_logic.cpp`, hàm
`classifyType`. Mỗi loại chấp cả biến thể gạch dưới lẫn gạch ngang.

| `type` | `params` | Thiết bị làm gì |
|---|---|---|
| `set_interval` (= `set-interval`) | `{"pollingSeconds": 5}` hoặc `{"pollingIntervalSeconds": 5}`, dải **[1, 3600]** | Đổi chu kỳ lấy mẫu ngay, không cần khởi động lại |
| `trigger_ota` (= `trigger-ota`) | không cần | Hỏi backend xem có firmware mới không |
| `request_heartbeat` (= `request-heartbeat`) | không cần | Gửi heartbeat ngay, không đợi hết chu kỳ |

> ⚠️ **Bảng này từng ghi sai suốt nhiều tuần** (`reboot`/`ota`/`calibrate`/`sample-now`/`set-config`
> — **không loại nào firmware hiểu**). Frontend chép nguyên vào dropdown, nên Admin gửi lệnh thấy
> `202` và toast thành công, còn thiết bị ack `status: "unknown"` rồi không làm gì. Sửa 08/08/2026.
>
> Nguồn sự thật là `classifyType`, **không phải tài liệu này**. Thêm loại lệnh mới thì sửa firmware
> trước, rồi mới sửa `IOT_COMMAND_TYPES` ở frontend và bảng này.

> **`202` KHÔNG có nghĩa là thiết bị đã thực thi** — chỉ là "đã đưa được vào broker". Kết quả thật
> về qua `solar/{deviceCode}/cmd/ack` với `status`: `ok` · `failed` · `rejected` · `unknown`.
> Backend ghi `LogWarning` cho ba trạng thái sau; hiện **chưa** hiển thị lên UI.

**Response thành công `202 Accepted`:** `CommonResponse<IotDeviceCommandAcceptedDto>`

| Field | Type | Mô tả |
|---|---|---|
| `cmdId` | `string` | Id command |
| `deviceCode` | `string` | Mã device |
| `topic` | `string` | `solar/{deviceCode}/cmd` |

**Lỗi thường gặp:**
- `400` — `type` rỗng
- `404` — Device không tồn tại / đã xóa
- `503` — MQTT bridge không khả dụng

---

### 11D. Admin — IoT Firmware Releases (auth: JWT Admin)

Base route: `/api/admin/iot-firmware-releases` — toàn bộ yêu cầu role `Admin`.

**`IotFirmwareReleaseDto`:**

| Field | Type | Nullable | Mô tả |
|---|---|---|---|
| `id` | `string` | Không | ID release |
| `version` | `string` | Không | SemVer (`X.Y.Z`) |
| `hardwareRevision` | `string` | Không | Phải khớp `IotDevice.HardwareRevision` |
| `artifactUrl` | `string` | Không | URL `.bin` |
| `sha256Checksum` | `string` | Không | SHA-256 hex (64 ký tự) |
| `artifactSizeBytes` | `long` | Không | Kích thước artifact |
| `releaseNotes` | `string?` | Có | Changelog markdown |
| `isPublished` | `bool` | Không | Đã publish chưa |
| `publishedAt` | `DateTime?` | Có | Thời điểm publish |
| `isArchived` | `bool` | Không | Đã archive (rollback/EOL) chưa |
| `createdAt` | `DateTime` | Không | Thời điểm tạo (UTC) |
| `isRequired` | `bool` | Không | Force update |
| `channel` | `IotFirmwareChannelEnum` | Không | `Stable`/`Beta` |
| `deviceModel` | `string?` | Có | e.g., `ESP32-S3-WROOM-1` |

#### `GET /api/admin/iot-firmware-releases`

**Auth:** Admin

**Query params:** `hardwareRevision` (`string?`), `publishedOnly` (`bool?` — `true` chỉ lấy đã publish & chưa archive), `pageNumber` (mặc định 1), `pageSize` (mặc định 10, clamp `[1,100]`).

> Sort `createdAt DESC`. Filter `!IsDeleted` luôn áp dụng.

**Response thành công `200`:** `CommonResponse<PaginationResponse<IotFirmwareReleaseDto>>`

---

#### `POST /api/admin/iot-firmware-releases`

**Mục đích:** Tạo firmware release (metadata sau khi đã upload artifact). Không verify checksum file thực sự.

**Auth:** Admin

**Request body:**

| Field | Type | Bắt buộc | Validation | Mô tả |
|---|---|---|---|---|
| `version` | `string` | Có | regex SemVer `^\d+\.\d+\.\d+$` | e.g., `1.2.3` |
| `hardwareRevision` | `string` | Có | required | Phải khớp `IotDevice.HardwareRevision` |
| `artifactUrl` | `string` | Có | URL tuyệt đối hợp lệ | URL `.bin` |
| `sha256Checksum` | `string` | Có | đúng 64 ký tự | SHA-256 hex |
| `artifactSizeBytes` | `long` | Có | `> 0 && <= 50_000_000` (50MB) | Kích thước artifact |
| `releaseNotes` | `string?` | Không | — | Changelog markdown |
| `publishImmediately` | `bool` | Không (mặc định `false`) | — | `true` = publish ngay |
| `isRequired` | `bool` | Không (mặc định `false`) | — | Force update |
| `channel` | `IotFirmwareChannelEnum` | Không (mặc định `Stable`) | — | `Stable`/`Beta` |
| `deviceModel` | `string?` | Không | — | e.g., `ESP32-S3-WROOM-1` |

**Response thành công `201`:** `CommonResponse<IotFirmwareReleaseDto>`

**Lỗi thường gặp:**
- `400` — Validation lỗi
- `409` — Cặp (`version`, `hardwareRevision`) đã tồn tại (unique index)

---

#### `POST /api/admin/iot-firmware-releases/{id}/publish`

**Mục đích:** Publish release để cho phép đặt làm target OTA. Set `isPublished=true`.

**Auth:** Admin

**Response thành công `200`:** `CommonResponse<IotFirmwareReleaseDto>`

**Lỗi thường gặp:**
- `404` — Release không tồn tại
- `409` — Release đã archive → không publish lại được

---

#### `POST /api/admin/iot-firmware-releases/{id}/archive`

**Mục đích:** Đánh dấu rollback/EOL — set `isArchived=true` (giữ nguyên `isPublished`). Không cho đặt làm target nữa.

**Auth:** Admin

**Response thành công `200`:** `CommonResponse<object>`

**Lỗi thường gặp:**
- `404` — Release không tồn tại

---

#### `POST /api/admin/iot-firmware-releases/upload-binary`

**Mục đích:** Upload file firmware `.bin` qua multipart. Backend stream vào storage, tự tính SHA-256, sanitize filename.

**Auth:** Admin

**Content-Type:** `multipart/form-data` — giới hạn request **60MB**.

**Form fields:**

| Field | Type | Bắt buộc | Mô tả |
|---|---|---|---|
| `file` | `IFormFile` | **Có** | File `.bin` (extension phải `.bin`) |
| `version` | `string` | Có | SemVer `X.Y.Z` |
| `hardwareRevision` | `string` | Có | e.g., `v1.0-S3-MAX485` |
| `isRequired` | `bool` | Không (mặc định `false`) | Force update |
| `channel` | `IotFirmwareChannelEnum` | Không (mặc định `Stable`) | `Stable`/`Beta` |
| `releaseNotes` | `string?` | Không | Changelog markdown |
| `deviceModel` | `string?` | Không | e.g., `ESP32-S3-WROOM-1` |

**Response thành công `201`:** `CommonResponse<FirmwareBinaryUploadDto>`

| Field | Type | Mô tả |
|---|---|---|
| `artifactUrl` | `string` | URL `.bin` (public hoặc tương đối) |
| `sha256Checksum` | `string` | SHA-256 hex (backend tính) |
| `artifactSizeBytes` | `long` | Kích thước thực |
| `fileName` | `string` | Tên file đã sanitize |
| `version` | `string` | echo từ form |
| `hardwareRevision` | `string` | echo từ form |
| `isRequired` | `bool` | echo |
| `channel` | `IotFirmwareChannelEnum` | echo |
| `releaseNotes` | `string?` | echo |
| `deviceModel` | `string?` | echo |

> Sau khi upload, dùng `artifactUrl` + `sha256Checksum` + `artifactSizeBytes` để gọi `POST /api/admin/iot-firmware-releases` tạo release metadata.

**Lỗi thường gặp:**
- `400` — Thiếu file hoặc extension không phải `.bin`
- `413` — File vượt 60MB

---

## Nhóm 12 — Cascade Risk Assessment (Sprint 7 B4 · §31.7)

**Bối cảnh & tác dụng:** Đánh giá rủi ro **1 pin hỏng lây lan sang pin lân cận cùng site** (cascade/propagation). `cascadeRiskScore` (0.0–1.0) được `CascadeRiskBackgroundService` tính lại **mỗi 5 phút** theo 3 rule cộng dồn rồi clamp ≤ 1.0:

1. **Topology factor** — theo `ElectricalTopology` (xem enum): Independent +0.0 · ParallelBank +0.2 · SeriesParallel +0.4 · SeriesString +0.6.
2. **Proximity** — số asset **cùng Site** có Open alert trong 1h gần đây: ≥1 → +0.2 · ≥3 → +0.2 (cộng dồn).
3. **Thermal runaway** — asset có alert `Overheat` + `Critical` + `Open` → +0.3.

> **Sprint Bonus NS-15 (#659) — score decay:** ngoài asset đang có Open alert, service còn recompute asset **từng có score > 0 nhưng đã hết Open alert** → score tự **tụt về topology** khi pin khỏe lại (trước fix score bị "đóng băng" trên heat map mãi mãi). FE **không cần** dựa vào `cascadeRiskUpdatedAt` để đoán score ôi nữa — score luôn được cập nhật.

**Chuỗi phản ứng khi score cross ngưỡng ≥ 0.7** → publish `BatteryCascadeRiskHighEvent`:
- **TicketService** — auto-upgrade Priority ticket liên quan lên **P1** (safety override, ghi `TicketActivity`) + **rút SLA DueAt về 4h** (NS-12 #656). Fallback **chỉ chọn ticket incident** (không nâng nhầm ticket bảo trì định kỳ). **Không có ticket active** → **auto-tạo ticket P1** `origin = System` (NS-13 #657) — event không rơi vào hư không.
- **NotificationService** — notify **Manager + Admin** (in-app + push + email) để điều Staff kiểm tra/cô lập (NS-14 #658, `NotificationTypeEnum.CascadeRiskHigh = 15`).

> Adaptation: project đã bỏ `BatteryGroup` nên proximity nhóm theo `SiteId`.

### `GET /api/battery-assets/{id}/cascade-risk`

**Mục đích:** Lấy cascade risk hiện tại của 1 asset (trả score đã lưu, không recompute on-demand).

**Auth:** Bắt buộc (Admin/Manager/Staff/Customer)

**Path param:** `id` — Guid của BatteryAsset.

**Response thành công `200`:** `CommonResponse<CascadeRiskDto>`

```json
{
  "isSuccess": true,
  "statusCode": 200,
  "message": "",
  "data": {
    "batteryAssetId": "8f1c...",
    "serialNumber": "BAT-2026-001",
    "siteId": "3a2b...",
    "cascadeRiskScore": 0.800,
    "level": "High",
    "electricalTopology": "SeriesString",
    "cascadeRiskUpdatedAt": "2026-06-24T03:15:00Z"
  },
  "listErrors": null
}
```

**Chi tiết `CascadeRiskDto`:**

| Field | Type | Nullable | Mô tả |
|---|---|---|---|
| `batteryAssetId` | `string` | Không | ID asset (UUID string) |
| `serialNumber` | `string?` | Null nếu không lấy được | Serial number của pin |
| `siteId` | `string?` | **Null nếu asset chưa gán site** | ID site |
| `cascadeRiskScore` | `decimal` | Không | Điểm rủi ro 0.0–1.0 (lưu DB, refresh mỗi 5 phút) |
| `level` | `CascadeRiskLevel` | Không | Mức derive từ score: `Low`/`Medium`/`High` (xem enum) |
| `electricalTopology` | `ElectricalTopologyEnum` | Không | Cách đấu nối điện (xem enum) |
| `cascadeRiskUpdatedAt` | `DateTime?` | **Null nếu chưa từng tính** | Thời điểm recompute gần nhất (UTC) |

**Lỗi thường gặp:** `404` — không tìm thấy asset (hoặc đã soft-delete) · `401`/`403`.

---

### `GET /api/sites/{id}/cascade-risk-summary`

**Mục đích:** Heat map cascade risk tổng hợp toàn bộ asset trong 1 site (Manager dashboard).

**Auth:** Bắt buộc (Admin/Manager)

**Path param:** `id` — Guid của Site.

**Response thành công `200`:** `CommonResponse<SiteCascadeRiskSummaryDto>`

**Chi tiết `SiteCascadeRiskSummaryDto`:**

| Field | Type | Nullable | Mô tả |
|---|---|---|---|
| `siteId` | `string` | Không | ID site |
| `totalAssets` | `int` | Không | Tổng số asset (chưa xóa) trong site |
| `highRiskCount` | `int` | Không | Số asset mức `High` (score ≥ 0.7) |
| `mediumRiskCount` | `int` | Không | Số asset mức `Medium` (0.5 ≤ score < 0.7) |
| `lowRiskCount` | `int` | Không | Số asset mức `Low` (score < 0.5) |
| `maxScore` | `decimal` | Không | Score lớn nhất trong site (0 nếu site rỗng) |
| `highRiskAssets` | `CascadeRiskDto[]` | Không (mảng có thể rỗng) | Danh sách asset `High`, sort theo score giảm dần |

**Lỗi thường gặp:** `404` — không tìm thấy site · `401`/`403`.

---

### `POST /api/battery-assets/{id}/topology`

**Mục đích:** Admin gán **electrical topology** cho asset (ảnh hưởng trực tiếp tới cascade risk). Sau khi set, score được **recompute ngay** để response phản ánh topology mới.

**Auth:** Bắt buộc (chỉ **Admin**)

**Path param:** `id` — Guid của BatteryAsset (lấy từ path, **không** nhận trong body).

**Request body:**

| Field | Type | Bắt buộc | Validation | Mô tả |
|---|---|---|---|---|
| `electricalTopology` | `ElectricalTopologyEnum` | **Bắt buộc** | Phải thuộc `1..4` | Cách đấu nối điện |

```json
{ "electricalTopology": 2 }
```

**Response thành công `200`:** `CommonResponse<CascadeRiskDto>` (DTO giống GET cascade-risk ở trên, đã recompute).

**Lỗi thường gặp:**
- `400` — Field-level validation (xem `listErrors`): `electricalTopology` ngoài 1..4, hoặc `id` rỗng. Mỗi lỗi có `field` + `detail`.
- `404` — không tìm thấy asset.
- `401`/`403` — chưa đăng nhập / không phải Admin.

---

## Nhóm 13 — Reports (Sprint 7 #114 · §5.2)

**Quy ước chung mọi report:**
- **Export:** thêm query `?format=csv` hoặc `?format=xlsx` → trả **file download** (XLSX dùng ClosedXML; CSV có UTF-8 BOM cho tiếng Việt). Không truyền `format` (hoặc giá trị khác) → trả **JSON** `CommonResponse<List<...>>`.
- **Thời gian:** `from`/`to` là UTC, tùy chọn. Report time-series mặc định **30 ngày gần nhất** nếu bỏ trống. `granularity`: `day` (mặc định) · `week` · `month`.
- Route phẳng `api/reports/...` (project bỏ API versioning).
- Tất cả là `GET`. Auth mặc định **Admin/Manager** (riêng `ambient-trend` mở thêm Staff/Customer).

### `GET /api/reports/battery-health-by-type`

**Mục đích:** Sức khỏe pin theo từng loại — tổng asset, số có alert active, health score.
**Auth:** Admin/Manager · **Query:** chỉ `format`.
**Response `200`:** `CommonResponse<List<BatteryHealthByTypeRow>>`

| Field | Type | Nullable | Mô tả |
|---|---|---|---|
| `typeId` | `string` | Không | ID loại pin |
| `name` | `string?` | Null nếu loại pin không có tên | Tên loại pin |
| `totalAssets` | `int` | Không | Tổng asset thuộc loại này |
| `withActiveAlerts` | `int` | Không | Số asset đang có Open alert |
| `healthScore` | `decimal` | Không | 0–100: % asset **không** có alert active (`(total-withAlerts)/total*100`) |

### `GET /api/reports/alert-volume`

**Mục đích:** Số lượng Alert theo thời gian (time-series).
**Auth:** Admin/Manager · **Query:** `from?`, `to?`, `granularity?` (mặc định 30 ngày/day), `format?`.
**Response `200`:** `CommonResponse<List<ReportTimeSeriesPoint>>` — `{ date: DateTime, count: int }` (cả 2 không null).

### `GET /api/reports/top-anomalies`

**Mục đích:** Top loại anomaly theo số lượng (kèm số Critical).
**Auth:** Admin/Manager · **Query:** `from?`, `to?`, `limit?` (mặc định 10, tối đa 100), `format?`.
**Response `200`:** `CommonResponse<List<TopAnomalyRow>>`

| Field | Type | Nullable | Mô tả |
|---|---|---|---|
| `anomalyType` | `string` | Không | Tên `AnomalyTypeEnum` (vd "Overheat") |
| `count` | `int` | Không | Tổng số alert loại này |
| `criticalCount` | `int` | Không | Số alert có severity Critical |

### `GET /api/reports/asset-lifecycle`

**Mục đích:** Vòng đời asset — tuổi (ngày), cycle count (BMS), tổng alert.
**Auth:** Admin/Manager · **Query:** chỉ `format`.
**Response `200`:** `CommonResponse<List<AssetLifecycleRow>>`

| Field | Type | Nullable | Mô tả |
|---|---|---|---|
| `assetId` | `string` | Không | ID asset |
| `serialNumber` | `string?` | Null nếu không có | Serial number |
| `ageDays` | `int` | Không | Số ngày từ `installDate` đến nay |
| `cycleCount` | `int?` | **Null nếu chưa có reading có cycleCount** | Số chu kỳ sạc/xả mới nhất từ BMS |
| `alertsTotal` | `int` | Không | Tổng số alert của asset |

### `GET /api/reports/warranty-expiring`

**Mục đích:** Asset sắp hết bảo hành trong N ngày.
**Auth:** Admin/Manager · **Query:** `within?` (chuỗi "90d" hoặc số, mặc định 90 ngày), `format?`.
**Response `200`:** `CommonResponse<List<WarrantyExpiringRow>>`

| Field | Type | Nullable | Mô tả |
|---|---|---|---|
| `assetId` | `string` | Không | ID asset |
| `serialNumber` | `string?` | Null nếu không có | Serial number |
| `warrantyEndDate` | `DateTime?` | Null nếu không set | Ngày hết bảo hành (UTC) |
| `daysRemaining` | `int?` | Null nếu không có warrantyEndDate | Số ngày còn lại đến khi hết hạn |
| `customerId` | `string` | Không | ID khách hàng sở hữu |

### `GET /api/reports/environmental-incidents`

**Mục đích:** Sự cố môi trường theo site/loại/thời gian (Sprint 7 mới).
**Auth:** Admin/Manager · **Query:** `from?`, `to?`, `siteId?` (Guid), `type?` (int = `EnvironmentalIncidentTypeEnum`), `format?`.
**Response `200`:** `CommonResponse<List<EnvironmentalIncidentRow>>`

| Field | Type | Nullable | Mô tả |
|---|---|---|---|
| `siteId` | `string` | Không | ID site |
| `incidentType` | `string` | Không | Tên `EnvironmentalIncidentTypeEnum` |
| `severity` | `string` | Không | Tên `AlertSeverityEnum` |
| `detectedAt` | `DateTime` | Không | Thời điểm phát hiện (UTC) |
| `resolvedAt` | `DateTime?` | **Null nếu chưa resolve** | Thời điểm xử lý xong (UTC) |
| `durationHours` | `decimal?` | **Null nếu chưa resolve** | Thời gian tồn tại sự cố (giờ) |
| `wasFalseAlarm` | `bool` | Không | `true` nếu được đánh dấu báo động giả |

### `GET /api/reports/ambient-trend`

**Mục đích:** Xu hướng nhiệt độ/độ ẩm/bức xạ môi trường theo thời gian cho 1 site (Sprint 7 mới).
**Auth:** Admin/Manager/**Staff/Customer** · **Query:** `siteId` (Guid, **bắt buộc**), `from?`, `to?`, `granularity?` (mặc định 30 ngày/day), `format?`.
**Response `200`:** `CommonResponse<List<AmbientTrendPoint>>`

| Field | Type | Nullable | Mô tả |
|---|---|---|---|
| `date` | `DateTime` | Không | Mốc bucket (UTC) |
| `avgTemp` | `decimal` | Không | Nhiệt độ môi trường trung bình (°C) |
| `maxTemp` | `decimal` | Không | Nhiệt độ cao nhất (°C) |
| `minTemp` | `decimal` | Không | Nhiệt độ thấp nhất (°C) |
| `humidityAvg` | `decimal?` | **Null nếu bucket không có dữ liệu độ ẩm** | Độ ẩm trung bình (%RH) |
| `irradianceAvg` | `decimal?` | **Null nếu không có dữ liệu bức xạ** | Bức xạ mặt trời trung bình |

---

## Bảng tổng hợp Endpoints

| Method | Path | Mục đích | Auth |
|---|---|---|---|
| GET | `/api/alerts` | Danh sách cảnh báo | Mọi role |
| GET | `/api/alerts/{id}` | Chi tiết cảnh báo | Mọi role |
| PATCH | `/api/alerts/{id}/acknowledge` | Acknowledge cảnh báo | Mọi role |
| PATCH | `/api/alerts/{id}/resolve` | Resolve cảnh báo | Admin/Manager/Staff |
| GET | `/api/battery-assets` | Danh sách pin (admin) | Admin/Manager |
| GET | `/api/battery-assets/me` | Danh sách pin (customer) | Customer |
| GET | `/api/battery-assets/{id}` | Chi tiết pin | Mọi role |
| GET | `/api/battery-assets/{id}/realtime` | Realtime snapshot pin | Mọi role |
| POST | `/api/admin/battery-assets` | Tạo pin | Admin |
| PUT | `/api/admin/battery-assets/{id}` | Cập nhật pin | Admin |
| DELETE | `/api/admin/battery-assets/{id}` | Xóa pin | Admin |
| PATCH | `/api/admin/battery-assets/{id}/restore` | Khôi phục pin | Admin |
| PUT | `/api/admin/battery-assets/{id}/transfer-owner` | Chuyển chủ sở hữu | Admin |
| GET | `/api/battery-types` | Danh sách loại pin | Admin/Manager/Staff |
| GET | `/api/battery-types/{id}` | Chi tiết loại pin | Admin/Manager/Staff |
| POST | `/api/admin/battery-types` | Tạo loại pin | Admin |
| PUT | `/api/admin/battery-types/{id}` | Cập nhật loại pin | Admin |
| DELETE | `/api/admin/battery-types/{id}` | Xóa loại pin | Admin |
| PATCH | `/api/admin/battery-types/{id}/restore` | Khôi phục loại pin | Admin |
| GET | `/api/sensor-readings/{id}/latest` | Reading mới nhất | Mọi role |
| GET | `/api/sensor-readings/{id}/history` | Lịch sử readings cursor-based | Mọi role |
| GET | `/api/sensor-readings/{id}/aggregate` | Aggregate theo bucket (chart) | Mọi role |
| POST | `/api/sensor-readings/batch` | Ingest batch (IoT) | API Key |
| GET | `/api/sites` | Danh sách site | Admin/Manager/Staff |
| GET | `/api/sites/me` | Site của customer | Customer |
| GET | `/api/sites/{id}` | Chi tiết site | Mọi role |
| GET | `/api/sites/{id}/dashboard` | Dashboard site | Mọi role |
| GET | `/api/sites/dashboard/stats` | Snapshot tổng hợp toàn bộ site (avg health, at-risk) | Admin/Manager |
| GET | `/api/sites/{siteId}/assets` | Pin tại site | Mọi role |
| POST | `/api/admin/sites` | Tạo site | Admin |
| PUT | `/api/admin/sites/{id}` | Cập nhật site | Admin |
| DELETE | `/api/admin/sites/{id}` | Xóa site | Admin |
| PATCH | `/api/admin/sites/{id}/restore` | Khôi phục site | Admin |
| GET | `/api/thresholds` | Danh sách ngưỡng | Admin/Manager |
| GET | `/api/thresholds/by-type/{id}` | Ngưỡng theo loại pin | Admin/Manager |
| PUT | `/api/admin/thresholds/by-type/{id}` | Upsert ngưỡng | Admin |
| GET | `/api/battery/dashboard/stats` | Battery dashboard stats | Mọi role auth |
| POST | `/api/ambient/readings/batch` | Ingest ambient (IoT) | API Key |
| GET | `/api/ambient/readings/history` | Lịch sử ambient | Mọi role |
| GET | `/api/ambient/readings/latest` | Ambient mới nhất theo site | Mọi role |
| PUT | `/api/ambient/threshold-configs` | Upsert ambient threshold | Admin/Manager |
| GET | `/api/ambient/threshold-configs/by-site/{siteId}` | Ambient threshold theo site | Admin/Manager |
| GET | `/api/ambient/threshold-configs` | List ambient threshold | Admin/Manager |
| POST | `/api/environmental-incidents` | Report incident (IoT) | API Key |
| POST | `/api/environmental-incidents/{id}/acknowledge` | Acknowledge incident | Admin/Manager/Staff |
| POST | `/api/environmental-incidents/{id}/resolve` | Resolve incident | Admin/Manager/Staff |
| POST | `/api/environmental-incidents/{id}/false-alarm` | Mark false alarm | Admin/Manager |
| GET | `/api/environmental-incidents` | Danh sách incident | Mọi role |
| GET | `/api/environmental-incidents/{id}` | Chi tiết incident | Mọi role |
| GET | `/api/environmental-incidents/by-site/{siteId}/active` | Incident active theo site | Mọi role |
| GET | `/api/battery/health` | Health check | Public |
| POST | `/api/iot-devices/provision` | Device provision sau boot | API Key (scope DeviceHeartbeat) |
| POST | `/api/iot-devices/heartbeat` | Heartbeat health định kỳ | API Key (scope DeviceHeartbeat) |
| GET | `/api/iot-devices/firmware-check` | Device poll firmware mới | API Key (scope FirmwareCheck) |
| PUT | `/api/iot-devices/firmware-update-log/{id}` | Device báo cáo progress OTA | API Key (scope FirmwareCheck) |
| GET | `/api/iot-devices/by-code/{deviceCode}` | Tra cứu device theo deviceCode → lấy id GUID | Admin/Manager/Staff |
| GET | `/api/iot-devices/{deviceId}/calibrations` | List calibration của device | Admin/Manager/Staff |
| POST | `/api/iot-devices/{deviceId}/calibrations` | Tạo calibration | Admin/Staff |
| DELETE | `/api/iot-devices/{deviceId}/calibrations/{calibrationId}` | Xóa calibration | Admin/Staff |
| GET | `/api/iot-devices/calibrations-expiring` | Calibration sắp hết hạn | Admin/Manager |
| GET | `/api/admin/iot-devices` | List IoT device | Admin |
| GET | `/api/admin/iot-devices/{id}` | Chi tiết IoT device (kèm full `apiKey` plaintext) | Admin |
| POST | `/api/admin/iot-devices` | Tạo device + sinh API key | Admin |
| PUT | `/api/admin/iot-devices/{id}` | Cập nhật device | Admin |
| DELETE | `/api/admin/iot-devices/{id}` | Decommission device | Admin |
| POST | `/api/admin/iot-devices/{id}/rotate-key` | Rotate API key | Admin |
| POST | `/api/admin/iot-devices/{id}/revoke-key` | Revoke API key | Admin |
| POST | `/api/admin/iot-devices/{id}/command` | Push downlink command (MQTT) | Admin |
| GET | `/api/admin/iot-firmware-releases` | List firmware release | Admin |
| POST | `/api/admin/iot-firmware-releases` | Tạo firmware release | Admin |
| POST | `/api/admin/iot-firmware-releases/{id}/publish` | Publish release | Admin |
| POST | `/api/admin/iot-firmware-releases/{id}/archive` | Archive release | Admin |
| POST | `/api/admin/iot-firmware-releases/upload-binary` | Upload file `.bin` firmware | Admin |
| GET | `/api/battery-assets/{id}/cascade-risk` | Cascade risk của 1 asset (Sprint 7 B4) | Admin/Manager/Staff/Customer |
| GET | `/api/sites/{id}/cascade-risk-summary` | Heat map cascade risk theo site (Sprint 7 B4) | Admin/Manager |
| POST | `/api/battery-assets/{id}/topology` | Set electrical topology (Sprint 7 B4) | Admin |
| GET | `/api/reports/battery-health-by-type` | Báo cáo sức khỏe pin theo loại (Sprint 7) | Admin/Manager |
| GET | `/api/reports/alert-volume` | Báo cáo số alert theo thời gian (Sprint 7) | Admin/Manager |
| GET | `/api/reports/top-anomalies` | Báo cáo top loại anomaly (Sprint 7) | Admin/Manager |
| GET | `/api/reports/asset-lifecycle` | Báo cáo vòng đời asset (Sprint 7) | Admin/Manager |
| GET | `/api/reports/warranty-expiring` | Báo cáo asset sắp hết bảo hành (Sprint 7) | Admin/Manager |
| GET | `/api/reports/environmental-incidents` | Báo cáo sự cố môi trường (Sprint 7) | Admin/Manager |
| GET | `/api/reports/ambient-trend` | Báo cáo xu hướng môi trường theo site (Sprint 7) | Admin/Manager/Staff/Customer |

> **Reports (Sprint 7):** mọi endpoint `/api/reports/*` hỗ trợ `?format=csv\|xlsx` để export file; không có `format` → JSON.

---

## Nhóm — Audit Logs nội bộ (Option C — Sprint audit)

> Endpoint **dự phòng (fallback resilience)**: query trực tiếp bảng nguồn `battery_audit_logs` ngay tại BatteryService, dùng được kể cả khi `AuditAggregatorService` (read-store hợp nhất) gặp sự cố. Enum `Severity`/`ActionCategory` dùng chung — xem [docs/api-audit.md](api-audit.md#enums--tập-giá-trị-cố-định).
>
> **Auth chung:** chỉ role `Admin` (`401` thiếu token / `403` sai role).

### `DTO BatteryAuditLogDto` (dùng cho cả 2 endpoint dưới)

| Field | Type | Nullable | Mô tả |
|---|---|---|---|
| `id` | `string` | Không | ID bản ghi audit |
| `eventId` | `string` | Không | Idempotency key của audit event |
| `actionCode` | `string` | Không | Mã hành động (xem bảng action bên dưới) |
| `actionCategory` | `string` | Không | Category (vd `DataModification`, `Configuration`) |
| `severity` | `string` | Không | Mức độ (`Info`/`Warning`/`Critical`/`Security`) |
| `targetId` | `string?` | Null nếu không gắn đối tượng | ID đối tượng (pin/cảnh báo) bị tác động |
| `targetDisplay` | `string?` | Null / `[REDACTED]` sau GDPR | Tên hiển thị đối tượng |
| `actorAccountId` | `string?` | Null nếu hệ thống | Account thực hiện |
| `isSuccess` | `bool` | Không | Thành công/thất bại |
| `reason` | `string?` | Null nếu không có | Lý do/ghi chú |
| `occurredAt` | `DateTime` | Không | Thời điểm xảy ra (UTC) |

### `GET /api/admin/battery/audit-logs`

**Mục đích:** Tra cứu audit log thao tác trên PIN (battery), có phân trang + lọc.

**Tác dụng:** Điều tra forensic IoT (ai tạo/sửa/xoá/gán pin, đổi ngưỡng, đổi trạng thái, hiệu chỉnh sensor), dùng khi Aggregator tạm ngừng.

**Auth:** Admin.

**Query params (đều optional):**

| Param | Type | Bắt buộc | Mô tả |
|---|---|---|---|
| `action` | `string?` | Không | Mã action (vd `BatteryCreated`). Bỏ trống = tất cả |
| `batteryId` | `string?` (UUID) | Không | Lọc theo pin cụ thể (target) |
| `from` | `DateTime?` | Không | Mốc đầu (UTC) |
| `to` | `DateTime?` | Không | Mốc cuối (UTC) |
| `pageNumber` | `int` | Không (mặc định 1) | Số trang |
| `pageSize` | `int` | Không (mặc định 50, trần 100) | Số item/trang |
| `sortBy` | `string?` | Không | Cột sort server-side. Whitelist: `occurredAt`, `actionCode`, `severity`, `targetDisplay`, `actorAccountId`, `isSuccess`. Ngoài whitelist → `occurredAt` |
| `sortDir` | `string?` | Không (mặc định `desc`) | `asc` \| `desc`; giá trị lạ → `desc` |

> **Sắp xếp:** mặc định `occurredAt` desc. Đã hỗ trợ sort server-side qua `sortBy`/`sortDir` (order toàn dataset trước phân trang, tie-breaker `Id ASC`) — áp dụng cho cả `GET /api/admin/alerts/audit-logs` (dùng chung handler). `targetDisplay`/`actorAccountId` nullable. Chi tiết: xem **Server-side Sort** đầu tài liệu.

**Action codes (battery):** `BatteryCreated` · `BatteryUpdated` · `BatteryDeleted` · `AssignedToCustomer` · `UnassignedFromCustomer` · `ThresholdConfigChanged` · `SensorReadingEdited` · `StatusChanged` · `MaintenanceLogged` · `CalibrationApplied`

**Response thành công `200`:** `CommonResponse<PaginationResponse<BatteryAuditLogDto>>` (mới nhất trước).

**Lỗi:** `401` / `403`.

### `GET /api/admin/alerts/audit-logs`

**Mục đích:** Tra cứu audit log thao tác trên CẢNH BÁO (alert). Alert audit host trong BatteryService (quyết định D14, route qua `batteryCluster`).

**Tác dụng:** Lịch sử xử lý cảnh báo (ai ack/suppress/đổi rule/override severity/resolve), truy trách nhiệm.

**Auth:** Admin.

**Query params:** giống endpoint trên nhưng thay `batteryId` bằng `alertId` (`string?` UUID — lọc theo cảnh báo cụ thể).

**Action codes (alert):** `AlertAcknowledged` · `AlertSuppressed` · `AlertRuleChanged` · `AlertSeverityOverridden` · `AlertManuallyResolved`

**Response thành công `200`:** `CommonResponse<PaginationResponse<BatteryAuditLogDto>>` (lọc `actionCode` bắt đầu bằng `Alert`).

**Lỗi:** `401` / `403`.

---

## Nhóm 14 — AI Module (dự đoán SOH · phân loại bất thường · kê đơn · feedback)

> **Bối cảnh:** module AI (Mamba SOH predictor + Isolation Forest + lớp prescription RAG/LLM) chấm pin
> theo cửa sổ 30 số đo, cho ra **SOH%**, **phân loại Normal/Degrading/Failed**, **mức rủi ro** và
> **đơn xử lý**. Kết quả lưu ở **bảng riêng** `soh_predictions` / `anomaly_classifications` (KHÔNG nhét
> vào `Alerts`) để giữ bằng chứng model chạy thật (score/confidence/latency/modelVersion) và làm
> **feedback loop** cho retrain.
>
> **Trạng thái (đối chiếu code + dữ liệu thật 2026-08-06):** pipeline **đang chạy**.
> `SohPredictionBackgroundService` gọi AI theo chu kỳ và ghi vào cả 2 bảng; tại thời điểm viết tài liệu
> DB có **8 454** bản ghi `soh_predictions`. BE gọi AI qua **gRPC là chính, HTTP là dự phòng** — client
> `Fallback*` tự chuyển đường khi gRPC lỗi, nên FE không thấy khác biệt.

**Toàn bộ endpoint của nhóm:**

| Endpoint | Method | Role | Mục đích |
|---|---|---|---|
| `/api/v1/soh-predictions` | GET | Admin/Manager/Staff | Lịch sử SOH đã dự đoán của 1 pin (chart) |
| `/api/v1/soh-predictions/long` | GET | Admin/Manager/Staff | SOH từ chuỗi dài 31–4096 bước (phân tích lịch sử) |
| `/api/v1/soh-predictions/batch` | GET | Admin/Manager/Staff | Chấm NHIỀU pin trong 1 kết nối stream (màn hình giám sát) |
| `/api/v1/anomaly-classifications` | GET | Admin/Manager/Staff | Lịch sử phân loại bất thường của 1 pin |
| `/api/v1/anomaly-classifications/{id}/feedback` | POST | Admin/Manager/Staff | Staff chấm đúng/sai kết quả AI → gửi ngược về AI |
| `/api/alerts/{id}/ai-prescription` | POST | Admin/Manager/Staff | Kê lại đơn cho alert ở chế độ đầy đủ |
| `/api/alerts/{id}/prescription-feedback` | POST | Admin/Manager/Staff/**Customer** | Phản hồi về đơn AI đã đưa ra |

---

### Enum của nhóm này

#### `AnomalyClassificationEnum` (output phân loại AI)

| Giá trị | Int | Ý nghĩa |
|---|---|---|
| `Normal` | 1 | Pin bình thường — AI không thấy bất thường (ứng viên false-positive nếu threshold đã raise alert) |
| `Degrading` | 2 | Pin đang xuống cấp — severity gợi ý Warning |
| `Failed` | 3 | Pin hỏng/nguy cơ cao — severity gợi ý Critical |

> ⚠️ **Cách serialize KHÔNG đồng nhất giữa 2 endpoint — đây là điểm dễ sai nhất của nhóm này:**
> - `AnomalyClassificationDto.classification` → **số nguyên** (`3`), vì DTO khai kiểu enum C#.
> - `BatchPredictionItemDto.classification` → **chuỗi** (`"Normal"`), vì handler batch gọi
>   `.ToString()` trên giá trị AI trả về.
>
> FE dùng chung một hàm parse cho cả hai sẽ hỏng ở một trong hai chỗ. Đã kiểm chứng bằng response thật,
> không phải suy từ code.

#### `StaffFeedbackEnum` (đánh giá của Staff về classification)

| Giá trị | Int | Ý nghĩa |
|---|---|---|
| `Correct` | 1 | AI phân loại **đúng** |
| `FalsePositive` | 2 | AI báo bất thường nhưng thực tế **bình thường** (dương tính giả) |
| `FalseNegative` | 3 | AI **bỏ sót** bất thường thật (âm tính giả) |

#### `healthStage` — tình trạng sức khỏe theo SOH (chuỗi, không phải enum C#)

Ngưỡng nằm ở `classify_health_stage()` phía AI (`src/models/anomaly_detector.py`); BE chỉ lưu lại chuỗi.

| Giá trị | Điều kiện | Ý nghĩa |
|---|---|---|
| `"Healthy"` | SOH ≥ 90% | Pin tốt |
| `"Degrading"` | 85% ≤ SOH < 90% | Bắt đầu xuống cấp, chưa cần hành động |
| `"Maintenance Required"` | 80% ≤ SOH < 85% | Nên lên lịch bảo trì/thay thế |
| `"End Of Life"` | SOH < 80% | Đạt ngưỡng EOL theo quy ước NASA 18650 |

#### `riskLevel` + `aiPriority` + `actionCode` (chuỗi, do `compute_risk_profile()` sinh ra cùng lúc)

Ba field này **luôn nhất quán với nhau** — chúng được tính trong cùng một hàm, không phải 3 nguồn độc lập:

| `riskLevel` | `aiPriority` | Điều kiện | `actionCode` tương ứng |
|---|---|---|---|
| `"Critical"` | `"P1"` | `healthStage = "End Of Life"` **hoặc** có warning mức `critical` | `"REPLACE_IMMEDIATELY"` (khi EOL) |
| `"High"` | `"P2"` | `healthStage = "Maintenance Required"` hoặc anomaly status = Anomaly | `"SCHEDULE_REPLACEMENT"` |
| `"Medium"` | `"P3"` | `healthStage = "Degrading"`, hoặc có warning thường | `"SCHEDULE_MAINTENANCE"` |
| `"Low"` | `"None"` | Không rơi vào các nhánh trên | `"MONITOR"` |

`actionCode` — 4 giá trị:

| Giá trị | Ý nghĩa |
|---|---|
| `"REPLACE_IMMEDIATELY"` | Thay ngay |
| `"SCHEDULE_REPLACEMENT"` | Lên lịch thay thế |
| `"SCHEDULE_MAINTENANCE"` | Lên lịch bảo trì |
| `"MONITOR"` | Chỉ theo dõi, chưa cần làm gì |

> ⚠️ **`aiPriority` KHÔNG phải Priority của ticket.** Đây là tín hiệu *urgency* thuần kỹ thuật do AI
> đưa ra. Priority thật của ticket do BE tính từ ma trận `ImpactScope × UrgencyLevel` khi Manager triage
> (xem `.claude/rules/design.md`). Hiển thị `aiPriority` như Priority ticket là sai nghiệp vụ.

#### `sohTrend` — xu hướng suy giảm (chuỗi)

| Giá trị | Ý nghĩa |
|---|---|
| `"accelerating"` | Tốc độ suy giảm nửa sau cửa sổ > 1.2× nửa đầu — pin đang xấu đi nhanh dần |
| `"stable"` | Tốc độ suy giảm không đổi đáng kể |
| `"slowing"` | Tốc độ suy giảm nửa sau < 0.8× nửa đầu |

> Với cửa sổ production 30 bước (ngắn hơn 1 chu kỳ NASA), AI **không** đủ tín hiệu liên-chu-kỳ để fit độ
> dốc thật, nên trả thẳng `"stable"` + tốc độ suy giảm trung bình quần thể. Thấy `"stable"` ở đây nghĩa
> là *"chưa đo được xu hướng"*, không phải *"đã xác nhận pin ổn định"*.

#### `status` của prescription feedback (chuỗi — hợp đồng với AI)

| Giá trị | Ý nghĩa |
|---|---|
| `"accepted"` | Kỹ thuật viên chấp nhận nguyên đơn AI → AI dùng làm ví dụ few-shot cho ca tương tự |
| `"edited"` | Có sửa — **bắt buộc** kèm `editedSteps` |
| `"rejected"` | Bác bỏ đơn |

---

### `GET /api/v1/soh-predictions`

**Mục đích:** Lịch sử **SOH dự đoán** của một pin — FE dùng vẽ chart SOH theo thời gian ở trang chi tiết
pin, kèm dải tin cậy, xu hướng và khuyến nghị hành động.

**Auth:** JWT — role `Admin` / `Manager` / `Staff` (Customer **không** được).

**Query params:**

| Param | Type | Bắt buộc | Mô tả |
|---|---|---|---|
| `batteryAssetId` | `Guid` | ✅ | Pin cần xem lịch sử dự đoán |
| `from` | `DateTime?` | ❌ | UTC — lọc `predictedAt >= from` |
| `to` | `DateTime?` | ❌ | UTC — lọc `predictedAt <= to` |
| `pageNumber` / `pageSize` | `int` | ❌ | Phân trang (kế thừa `PaginationRequest`) |

**Response thành công `200`:** `CommonResponse<PaginationResponse<SohPredictionDto>>`

Response thật (đã chạy trên dữ liệu production, rút gọn còn 1 item):

```json
{
  "data": {
    "items": [
      {
        "id": "1a5ae545-64e5-445e-930a-35ceb2883fee",
        "batteryAssetId": "8794a454-751a-4129-8cf9-d999ff359625",
        "predictedSohPercent": 62.43,
        "confidence": 0.618,
        "modelVersion": "1.6",
        "predictedAt": "2026-08-06T14:09:30.472388Z",
        "latencyMs": 21,
        "healthStage": "End Of Life",
        "stageConfidence": 1.0,
        "isBorderline": false,
        "sohStd": 2.41,
        "rulCyclesEstimate": 0,
        "aiPriority": "P1",
        "riskLevel": "Critical",
        "actionCode": "REPLACE_IMMEDIATELY",
        "sohTrend": "stable",
        "degradationRatePerCycle": 0.15,
        "cyclesToMaintenance": 0,
        "isTemperatureOod": true
      }
    ],
    "totalItems": 1037,
    "pageNumber": 1,
    "pageSize": 1,
    "totalPages": 1037,
    "hasNextPage": true,
    "hasPreviousPage": false
  },
  "listErrors": null,
  "isSuccess": true,
  "statusCode": 200,
  "message": ""
}
```

**Chi tiết `SohPredictionDto` — 19 field:**

| Field | Type | Nullable | Mô tả |
|---|---|---|---|
| `id` | `string` (GUID) | Không | ID bản ghi dự đoán |
| `batteryAssetId` | `string` (GUID) | Không | Pin được dự đoán |
| `predictedSohPercent` | `decimal` | Không | SOH dự đoán (%), `numeric(5,2)` |
| `confidence` | `decimal` | Không | Độ tự tin 0–1, `numeric(4,3)` |
| `modelVersion` | `string` | Không | Phiên bản model — thực tế hiện tại: `"1.6"` (bộ NASA) và `"2.0-lfp"` (bộ LFP). Model được chọn theo `chemistry` của pin |
| `predictedAt` | `DateTime` (UTC) | Không | Thời điểm dự đoán — trục thời gian của chart |
| `latencyMs` | `int` | Không | Độ trễ inference (ms) — monitor SLA < 100ms |
| `healthStage` | `string?` | **Có** | `"Healthy"` / `"Degrading"` / `"Maintenance Required"` / `"End Of Life"` |
| `stageConfidence` | `decimal?` | **Có** | Tỉ lệ mẫu MC Dropout rơi vào `healthStage`, 0–1, `numeric(4,3)` |
| `isBorderline` | `bool` | Không (default `false`) | `true` khi `stageConfidence < 0.7` — kết luận sát ngưỡng |
| `sohStd` | `decimal?` | **Có** | Độ lệch chuẩn MC Dropout theo điểm SOH — dùng vẽ error bar, `numeric(5,2)` |
| `rulCyclesEstimate` | `int?` | **Có** | Số chu kỳ còn lại ước tính tới EOL (SOH 80%) |
| `aiPriority` | `string?` | **Có** | `"P1"` / `"P2"` / `"P3"` / `"None"` — ⚠️ KHÔNG phải Priority ticket |
| `riskLevel` | `string?` | **Có** | `"Critical"` / `"High"` / `"Medium"` / `"Low"` |
| `actionCode` | `string?` | **Có** | `"REPLACE_IMMEDIATELY"` / `"SCHEDULE_REPLACEMENT"` / `"SCHEDULE_MAINTENANCE"` / `"MONITOR"` |
| `sohTrend` | `string?` | **Có** | `"accelerating"` / `"stable"` / `"slowing"` |
| `degradationRatePerCycle` | `decimal?` | **Có** | %SOH mất mỗi chu kỳ sạc–xả, `numeric(8,5)` |
| `cyclesToMaintenance` | `int?` | **Có** | Số chu kỳ tới ngưỡng bảo trì 85%. `0` nghĩa là **đã ở/dưới ngưỡng**, không phải "không có dữ liệu" |
| `isTemperatureOod` | `bool` | Không (default `false`) | `true` khi model đang **ngoại suy ngoài miền nhiệt độ đã train** |

> ⚠️ **12 field từ `healthStage` trở xuống được thêm ngày 2026-08-06.** Bản ghi tạo TRƯỚC ngày đó có
> toàn bộ 10 field nullable = `null`, và 2 field bool = `false`. Tại thời điểm viết tài liệu
> **7 774 / 8 454 bản ghi** rơi vào diện này — tức **đa số**, không phải trường hợp hiếm.
>
> FE **phải** xử lý `null`: không được coi `healthStage == null` là pin khỏe, và không được coi
> `isTemperatureOod == false` của bản ghi cũ là "đã kiểm tra và nhiệt độ bình thường". Muốn chỉ lấy bản
> ghi đầy đủ thì lọc theo `predictedAt`.
>
> Đếm lại tỉ lệ hiện tại:
> ```sql
> SELECT count(*) FILTER (WHERE health_stage IS NULL) AS thieu_field,
>        count(*)                                     AS tong
> FROM soh_predictions;
> ```

> ⚠️ **`isTemperatureOod = true` KHÔNG phải dấu hiệu pin xấu.** Nó nói rằng *con số SOH kém tin cậy* vì
> nhiệt độ đo nằm ngoài miền dữ liệu huấn luyện. UI phải phân biệt rõ hai chuyện này, nếu không người
> dùng đọc một cảnh báo kỹ thuật thành một cảnh báo hỏng hóc.

**Lỗi thường gặp:** `401` chưa đăng nhập · `403` role không hợp lệ.

---

### `GET /api/v1/soh-predictions/long`

**Mục đích:** Chấm SOH từ **chuỗi dài 31–4096 số đo** của một pin, dùng cho **phân tích lịch sử**.
Khác hẳn `GET /api/v1/soh-predictions` về bản chất: đường này gọi **model LONG** (bộ trọng số riêng),
**không có** MC Dropout (⇒ không `confidence`, không `healthStage`) và **không có** Isolation Forest
(⇒ không anomaly, không `riskLevel`).

> ⚠️ **KHÔNG dùng kết quả này để quyết định tạo ticket.** Phía AI cố ý bỏ anomaly vì Isolation Forest
> được fit trên phân bố feature của cửa sổ 30 bước — chấm chuỗi 4096 bước bằng nó ra con số trông hợp lệ
> mà vô nghĩa.

**Auth:** JWT — role `Admin` / `Manager` / `Staff`.

**Query params:**

| Param | Type | Bắt buộc | Mặc định | Mô tả |
|---|---|---|---|---|
| `batteryAssetId` | `Guid` | ✅ | — | Pin cần phân tích |
| `limit` | `int` | ❌ | `512` | Số timestep lấy về. **Bị kẹp vào [31, 4096] ở handler**, không trả lỗi: gõ `5000` sẽ nhận `4096`, gõ `10` sẽ nhận `31` |

**Response thành công `200`:** `CommonResponse<LongSohDto>`

Response thật (cùng pin với ví dụ trên, `limit=300`):

```json
{
  "data": {
    "batteryAssetId": "8794a454-751a-4129-8cf9-d999ff359625",
    "sohPercent": 32.25,
    "seqLen": 300,
    "device": "cpu",
    "latencyMs": 36,
    "modelVersion": "2.2"
  },
  "listErrors": null,
  "isSuccess": true,
  "statusCode": 200,
  "message": ""
}
```

**Chi tiết `LongSohDto`:**

| Field | Type | Nullable | Mô tả |
|---|---|---|---|
| `batteryAssetId` | `string` (GUID) | Không | Pin được phân tích |
| `sohPercent` | `decimal` | Không | SOH% từ model LONG |
| `seqLen` | `int` | Không | Số timestep AI **thực sự** chấm — có thể nhỏ hơn `limit` nếu pin thiếu dữ liệu hoặc số đo bị loại vì ngoại lai |
| `device` | `string` | Không | `"cpu"` hoặc `"cuda"` |
| `latencyMs` | `int` | Không | Độ trễ inference (ms) |
| `modelVersion` | `string` | Không | Phiên bản model LONG — **khác** `modelVersion` của SOH thường |

> ⚠️ **Đừng vẽ chung một chart với SOH cửa sổ 30 mà không ghi rõ nguồn.** Hai đường dùng hai bộ trọng số
> riêng: ví dụ thật ở trên cho cùng một pin ra **62.43%** (model `1.6`, cửa sổ 30) và **32.25%**
> (model `2.2`, chuỗi 300). Chênh lệch này là bình thường, **không** phải pin vừa thay đổi.

**Lỗi thường gặp** (đã kiểm chứng bằng request thật):

| Mã | Khi nào | Body thật |
|---|---|---|
| `401` | Chưa đăng nhập | — |
| `403` | Role không hợp lệ | — |
| `404` | Pin không tồn tại hoặc đã soft-delete | `{"data":null,"listErrors":null,"isSuccess":false,"statusCode":404,"message":"Không tìm thấy pin."}` |
| `409` | Pin chưa đủ 31 số đo | `{"...","statusCode":409,"message":"Pin cần ít nhất 31 số đo cho phân tích chuỗi dài, hiện có 0."}` |
| `409` | Còn < 31 số đo sau khi loại ngoại lai | `message`: `"Chỉ còn N số đo hợp lệ sau khi loại ngoại lai."` |
| `503` | AI không phản hồi (cả gRPC lẫn HTTP) | `message`: `"AI không phản hồi cho phân tích chuỗi dài."` |

---

### `GET /api/v1/soh-predictions/batch`

**Mục đích:** Chấm **nhiều pin trong MỘT kết nối gRPC bidi stream** — dùng cho màn hình giám sát nhiều
pin cùng lúc. N lần gọi đơn lẻ tốn N round-trip; stream chỉ tốn một.

**Auth:** JWT — role `Admin` / `Manager` / `Staff`.

**Query params:**

| Param | Type | Bắt buộc | Mặc định | Mô tả |
|---|---|---|---|---|
| `limit` | `int` | ❌ | `10` | Số pin tối đa mỗi lượt. **Bị kẹp vào [1, 50] ở handler** |

**Pin nào được chọn:** các `BatteryAsset` chưa xoá, `status = Active`, lấy `limit` cái đầu. Pin **chưa đủ
30 số đo hợp lệ** bị **bỏ qua âm thầm** (không gửi đi, để khỏi làm đứt stream).

**Response thành công `200`:** `CommonResponse<BatchPredictionDto>`

Response thật (`limit=3`, chỉ 2 pin đủ dữ liệu):

```json
{
  "data": {
    "items": [
      {
        "batteryAssetId": "ab80f61e-1384-493a-98d5-fd9b445fa907",
        "sohPercent": 98.1,
        "classification": "Normal",
        "healthStage": "Healthy",
        "riskLevel": "Medium",
        "actionCode": "SCHEDULE_MAINTENANCE",
        "isBorderline": false,
        "isTemperatureOod": false
      },
      {
        "batteryAssetId": "125840ea-278b-49b5-8519-05a563865439",
        "sohPercent": 96.81,
        "classification": "Normal",
        "healthStage": "Healthy",
        "riskLevel": "Medium",
        "actionCode": "SCHEDULE_MAINTENANCE",
        "isBorderline": false,
        "isTemperatureOod": false
      }
    ],
    "requestedCount": 2,
    "isComplete": true,
    "abortReason": null
  },
  "listErrors": null,
  "isSuccess": true,
  "statusCode": 200,
  "message": ""
}
```

**Chi tiết `BatchPredictionDto`:**

| Field | Type | Nullable | Mô tả |
|---|---|---|---|
| `items` | `BatchPredictionItemDto[]` | Không (có thể rỗng) | Kết quả, **đúng thứ tự đã gửi** |
| `requestedCount` | `int` | Không | Số pin **đã gửi đi chấm** — có thể **nhỏ hơn `limit`** vì pin thiếu dữ liệu đã bị loại trước |
| `isComplete` | `bool` | Không | `true` khi `abortReason == null` **và** `items.length == requestedCount` |
| `abortReason` | `string?` | **Có** | Lý do stream đứt; `null` khi nhận đủ |

**Chi tiết `BatchPredictionItemDto`:**

| Field | Type | Nullable | Mô tả |
|---|---|---|---|
| `batteryAssetId` | `string` (GUID) | Không | Pin tương ứng |
| `sohPercent` | `decimal` | Không | SOH% dự đoán |
| `classification` | `string` | Không | ⚠️ **Chuỗi** `"Normal"` / `"Degrading"` / `"Failed"` — KHÁC `AnomalyClassificationDto.classification` (số) |
| `healthStage` | `string?` | **Có** | Như bảng `healthStage` ở trên |
| `riskLevel` | `string?` | **Có** | `"Critical"` / `"High"` / `"Medium"` / `"Low"` |
| `actionCode` | `string?` | **Có** | 4 giá trị action code |
| `isBorderline` | `bool` | Không | Kết luận sát ngưỡng |
| `isTemperatureOod` | `bool` | Không | Ngoại suy ngoài miền nhiệt độ |

> 🔴 **PHẢI đọc `isComplete` trước khi hiển thị.** Bidi stream **không có lỗi theo từng message**: một pin
> có cửa sổ sai sẽ làm đứt cả lượt, kéo theo mọi pin phía sau cũng không được chấm. Vì vậy **pin không có
> trong `items` là pin CHƯA ĐƯỢC CHẤM**, không phải pin bình thường — hiển thị nó như "không có cảnh báo"
> là nói sai sự thật với người vận hành.

**Lỗi thường gặp:**

| Mã | Khi nào | `message` |
|---|---|---|
| `401` / `403` | Chưa đăng nhập / sai role | — |
| `409` | Không có pin `Active` nào | `"Không có pin Active nào để dự đoán."` |
| `409` | Có pin nhưng không pin nào đủ số đo hợp lệ | `"Không pin nào đủ số đo hợp lệ để dự đoán hàng loạt."` |

---

### `GET /api/v1/anomaly-classifications`

**Mục đích:** Lịch sử phân loại bất thường của **một pin** (do `SohPredictionBackgroundService` populate).
Dùng cho FE hiển thị timeline phân loại + gắn nút feedback.

**Auth:** JWT — role `Admin` / `Manager` / `Staff` (Customer **không** được).

**Query params:**

| Param | Type | Bắt buộc | Mô tả |
|---|---|---|---|
| `batteryAssetId` | `Guid` | ✅ | Pin cần xem lịch sử phân loại |
| `classification` | `AnomalyClassificationEnum?` | ❌ | Lọc theo kết quả (`1` Normal / `2` Degrading / `3` Failed) |
| `from` | `DateTime?` | ❌ | UTC — lọc `classifiedAt >= from` |
| `to` | `DateTime?` | ❌ | UTC — lọc `classifiedAt <= to` |
| `pageNumber` / `pageSize` | `int` | ❌ | Phân trang (kế thừa `PaginationRequest`) |

**Response thành công `200`:** `CommonResponse<PaginationResponse<AnomalyClassificationDto>>` — shape item
xem bảng **`AnomalyClassificationDto`** bên dưới.

**Lỗi thường gặp:** `401` chưa đăng nhập · `403` role không hợp lệ.

---

### `POST /api/v1/anomaly-classifications/{id}/feedback`

**Mục đích:** Staff xác nhận classification của AI (sau khi xử lý/resolve ticket) là đúng hay sai — phục
vụ đo precision/recall của model trên production + xuất dữ liệu retrain hàng tháng (§30.12). Là
**feedback loop** của tầng AI.

**Cách dùng:** sau khi Staff resolve một ticket/alert có classification AI, UI hỏi "AI phân loại `Failed`
có đúng không?" → gửi feedback về classification tương ứng.

**Auth:** JWT — role `Admin` / `Manager` / `Staff` (Customer **không** được). `staffFeedbackByUserId`
**luôn lấy từ token**, client không set được (chống mạo danh).

> **Cập nhật 2026-08-06 — feedback giờ được gửi NGƯỢC về AI.** Trước đây endpoint chỉ ghi vào DB của BE;
> AI không bao giờ biết mình phân loại sai. Nay handler gọi tiếp `SubmitClassificationFeedback` sang AI
> để AI cộng dồn precision/recall vào store riêng (`classification_feedback.jsonl`, dùng chung volume
> giữa 2 container AI).
>
> **Thứ tự là chủ ý: lưu DB TRƯỚC, gửi AI SAU.** Phản hồi của Staff phải được giữ lại kể cả khi AI đang
> sập. Vì vậy **AI lỗi KHÔNG làm endpoint này trả lỗi** — vẫn `200`, chỉ là vòng học chậm lại. Không được
> biến một thao tác đã thành công của người dùng thành lỗi.

**Path param:**

| Param | Type | Bắt buộc | Mô tả |
|---|---|---|---|
| `id` | `Guid` | ✅ | ID của bản ghi `AnomalyClassification` |

**Request body:**
```json
{
  "feedback": 2
}
```

| Field | Type | Bắt buộc | Validation | Mô tả |
|---|---|---|---|---|
| `feedback` | `StaffFeedbackEnum` (int) | ✅ | Phải là `1`/`2`/`3` | Đánh giá của Staff (xem enum trên) |

> Các field khác (`id` từ route, `staffFeedbackByUserId` từ token) **không** nhận qua body.

**Response thành công `200`:** `CommonResponse<AnomalyClassificationDto>` — trả về classification sau khi
cập nhật feedback. Response thật:

```json
{
  "data": {
    "id": "5c3e246c-8009-4a49-b485-85acc2fe9e3f",
    "alertId": null,
    "batteryAssetId": "8794a454-751a-4129-8cf9-d999ff359625",
    "classification": 3,
    "anomalyScore": 0.122300,
    "confidence": 0.122,
    "modelVersion": "1.6",
    "classifiedAt": "2026-08-06T14:09:30.472388Z",
    "latencyMs": 21,
    "staffFeedback": 2,
    "staffFeedbackByUserId": "62b55455-db76-46c3-a79b-1a58e6b1998e",
    "staffFeedbackAt": "2026-08-06T14:12:43.2299509Z"
  },
  "listErrors": null,
  "isSuccess": true,
  "statusCode": 200,
  "message": "Đã ghi nhận feedback."
}
```

**Chi tiết `AnomalyClassificationDto`:**

| Field | Type | Nullable | Mô tả |
|---|---|---|---|
| `id` | `string` (GUID) | Không | ID bản ghi classification |
| `alertId` | `string?` (GUID) | **Có** — `null` khi classify không gắn Alert cụ thể (đa số bản ghi từ job nền rơi vào diện này) | Alert liên quan |
| `batteryAssetId` | `string` (GUID) | Không | Pin được phân loại |
| `classification` | `AnomalyClassificationEnum` (**int**) | Không | Kết quả phân loại (1 Normal / 2 Degrading / 3 Failed) |
| `anomalyScore` | `decimal` | Không | Điểm Isolation Forest — **âm = bất thường hơn**; giá trị dương là bình thường, `numeric(8,6)` |
| `confidence` | `decimal` | Không | Độ tự tin 0–1, `numeric(4,3)` |
| `modelVersion` | `string` | Không | Phiên bản model — thực tế: `"1.6"` / `"2.0-lfp"` |
| `classifiedAt` | `DateTime` (UTC) | Không | Thời điểm AI phân loại |
| `latencyMs` | `int` | Không | Độ trễ inference (ms) — monitor SLA < 100ms |
| `staffFeedback` | `StaffFeedbackEnum?` (int) | **Có** — `null` khi chưa có feedback | Đánh giá của Staff |
| `staffFeedbackByUserId` | `string?` (GUID) | **Có** — `null` khi chưa có feedback | User Staff đã đánh giá (từ token) |
| `staffFeedbackAt` | `DateTime?` (UTC) | **Có** — `null` khi chưa có feedback | Thời điểm đánh giá |

**Lỗi thường gặp:**
- `400` — `feedback` không thuộc `{1,2,3}` hoặc `id` rỗng → field-level (`listErrors: [{ field, detail }]`)
- `401` — Chưa đăng nhập
- `403` — Role không nằm trong Admin/Manager/Staff
- `404` — Không tìm thấy classification → `message`, `listErrors: null`

---

### `POST /api/alerts/{id}/ai-prescription`

**Mục đích:** Kỹ thuật viên **chủ động** hỏi AI: kê lại đơn xử lý cho alert này ở **chế độ đầy đủ**
(RAG + LLM). Trả đơn về thẳng UI **và** gắn `prescriptionId` mới lên alert để vòng phản hồi dùng được.

**Khác gì với prescription tự động gắn sẵn trên alert:** đường tự động chạy theo event, phải giữ ngân sách
LLM cho **mọi** pin nên luôn `agentic=false`. Đây là thao tác thủ công cho **đúng một** pin, nên mới đáng
bật chain agentic (2 lượt LLM).

**Auth:** JWT — role `Admin` / `Manager` / `Staff`. **Không mở cho Customer** — đây là công cụ chẩn đoán
của người vận hành.

**Path param:**

| Param | Type | Bắt buộc | Mô tả |
|---|---|---|---|
| `id` | `Guid` | ✅ | ID của alert cần kê đơn |

**Query param:**

| Param | Type | Bắt buộc | Mặc định | Mô tả |
|---|---|---|---|---|
| `agentic` | `bool` | ❌ | `false` | `true` = bật chain agentic (2 lượt LLM, chậm hơn, chất lượng cao hơn) |

**Request body:** Không có.

**Response thành công `200`:** `CommonResponse<AiPrescriptionDto>`

Response thật (alert của pin EOL, `agentic=false`):

```json
{
  "data": {
    "prescription": "Battery has reached end-of-life — immediate replacement required. Current SOH 60.9%, risk Critical, priority P1. Active warnings: BATTERY_EOL, TEMP_OOD.",
    "actionSteps": [
      "Isolate the battery from the system using the Lockout/Tagout procedure.",
      "Measure open-circuit voltage and surface temperature before removal.",
      "Replace with a unit of identical specification per the maintenance SOP §3.",
      "Record final SOH, anomaly status, and replacement reason in the ticket."
    ],
    "ppeRequired": [
      "Insulated gloves (>=500V)",
      "Safety glasses (ANSI Z87.1)",
      "Steel-toed footwear"
    ],
    "sopReferences": [
      "battery_maintenance_sop §3 (Replacement Criteria)",
      "electrical_safety_sop (LOTO + isolation)"
    ],
    "safetyWarnings": [
      "Mandatory PPE missing from generated output — enforced per PPE matrix: Steel-toed footwear"
    ],
    "escalationConditions": [
      "Immediate replacement required — notify manager within 1 hour.",
      "Immediate replacement required — notify manager within 1 hour"
    ],
    "humanVerificationRequired": true,
    "enriched": false,
    "llmProvider": "none",
    "blocked": false,
    "cached": false,
    "prescriptionId": "424d0473-e501-4a35-8a66-d7f2740f8bcf"
  },
  "listErrors": null,
  "isSuccess": true,
  "statusCode": 200,
  "message": ""
}
```

**Chi tiết `AiPrescriptionDto`:**

| Field | Type | Nullable | Mô tả |
|---|---|---|---|
| `prescription` | `string` | Không (có thể **rỗng** `""`) | Mô tả tổng. Rỗng khi AI chạy đường rule-based mà không có nội dung mô tả |
| `actionSteps` | `string[]` | Không (có thể rỗng) | Các bước cụ thể — **đã qua safety gate** (LOTO/thermal được chèn thêm nếu cần) |
| `ppeRequired` | `string[]` | Không (có thể rỗng) | PPE bắt buộc |
| `sopReferences` | `string[]` | Không (có thể rỗng) | Tham chiếu SOP |
| `safetyWarnings` | `string[]` | Không (có thể rỗng) | Cảnh báo an toàn |
| `escalationConditions` | `string[]` | Không (có thể rỗng) | Điều kiện nên escalate — **tham khảo**, hệ thống KHÔNG tự escalate theo field này. ⚠️ **AI có thể trả phần tử trùng lặp** (xem ví dụ trên: 2 dòng chỉ khác dấu chấm cuối) — FE nên dedupe trước khi hiển thị |
| `humanVerificationRequired` | `bool` | Không | Luôn `true` với P1 và với mọi kết quả bị chặn |
| `enriched` | `bool` | Không | `true` = LLM+RAG đã chạy; `false` = bản rule-based |
| `llmProvider` | `string` | Không | `"deepseek"` / `"gemini"` / `"anthropic"` / `"none"` |
| `blocked` | `bool` | Không | `true` khi output LLM **bị safety gate CHẶN** — nội dung trả về là bản rule-based thay thế, KHÔNG phải thứ LLM sinh ra |
| `cached` | `bool` | Không | `true` khi AI trả từ cache idempotency (TTL 10 phút) thay vì chạy mới |
| `prescriptionId` | `string?` (GUID) | **Có** | ID để gửi phản hồi về `POST /api/alerts/{id}/prescription-feedback`. `null` khi AI không cấp (history store lỗi) |

> ⚠️ **`blocked = true` phải được nói rõ trên UI.** Người đọc đang xem bản rule-based, không phải khuyến
> nghị của LLM. Hiển thị y như nhau là để người vận hành tin nhầm nguồn gốc của lời khuyên.

> ⚠️ **`cached = true` không phải nút hỏng.** Bấm "gợi ý lại" hai lần liên tiếp với cùng dữ liệu sẽ ra
> cùng kết quả — đó là đúng thiết kế (idempotency TTL 10 phút). UI nên phân biệt để người dùng không bấm mãi.

> ℹ️ Ví dụ trên có `enriched: false` / `llmProvider: "none"` vì môi trường dev **chưa cấu hình API key
> LLM**. Khi đã cấu hình, cùng request sẽ trả `enriched: true` và tên provider tương ứng. Cấu trúc field
> không đổi.

**Lỗi thường gặp:**

| Mã | Khi nào | `message` |
|---|---|---|
| `401` / `403` | Chưa đăng nhập / role không hợp lệ (kể cả Customer) | — |
| `404` | Không tìm thấy alert | `"Không tìm thấy alert."` |
| `404` | Alert có nhưng pin của nó đã bị xoá | `"Không tìm thấy pin của alert này."` |
| `409` | Alert ở **cấp site**, không gắn pin nào | `"Alert này ở cấp site, không gắn với pin nào nên không kê đơn được."` |
| `409` | Pin chưa đủ 30 số đo | `"Pin chưa đủ 30 số đo để AI kê đơn."` |
| `409` | Còn < 30 số đo sau khi loại ngoại lai | `"Không đủ số đo hợp lệ trong dải AI chấp nhận để kê đơn."` |
| `409` | Cửa sổ thiếu `cycle_count` mà model của pin bắt buộc phải có | `"Cửa sổ thiếu cycle_count, mà model của pin này bắt buộc phải có."` |
| `503` | AI không phản hồi (cả gRPC lẫn HTTP) | `"AI không phản hồi. Thử lại sau."` |

---

### `POST /api/alerts/{id}/prescription-feedback`

**Mục đích:** Kỹ thuật viên phản hồi về **đơn mà AI đã đưa ra** cho một alert. Đơn được chấp nhận sẽ
thành ví dụ few-shot cho các ca tương tự sau. Không có đường phản hồi thì AI lặp lại cùng một lời khuyên
sai mãi mà không ai sửa được.

**Điều kiện tiên quyết:** alert phải **đã có** `aiPrescriptionId` — tức là đã qua đường prescription tự
động, hoặc vừa gọi `POST /api/alerts/{id}/ai-prescription`. Chưa có thì trả `409`.

**Auth:** JWT — role `Admin` / `Manager` / `Staff` / **`Customer`**.

> Customer **chỉ** phản hồi được alert của mình; alert của khách khác trả **`404`** (cố ý không phải
> `403` — `403` sẽ xác nhận alert đó có thật).

**Path param:**

| Param | Type | Bắt buộc | Mô tả |
|---|---|---|---|
| `id` | `Guid` | ✅ | ID của alert mang prescription cần phản hồi |

**Request body:**

```json
{
  "status": "edited",
  "editedSteps": ["Bước 1 đã sửa", "Bước 2 đã sửa"],
  "note": "Bỏ bước đo nhiệt vì thiết bị đang bảo dưỡng"
}
```

| Field | Type | Bắt buộc | Nullable | Validation | Mô tả |
|---|---|---|---|---|---|
| `status` | `string` | ✅ | Không | Chỉ nhận `"accepted"` / `"edited"` / `"rejected"` (đối chiếu **không phân biệt hoa thường**) | Kết luận của kỹ thuật viên |
| `editedSteps` | `string[]?` | ❌ (✅ khi `status = "edited"`) | **Có** | Khi `status = "edited"` phải có ≥ 1 phần tử **không rỗng** | Các bước đã sửa |
| `note` | `string?` | ❌ | **Có** | — | Ghi chú tự do |

> `alertId` lấy từ route, **không** nhận qua body.
> Phần tử rỗng/toàn khoảng trắng trong `editedSteps` bị **loại trước khi gửi sang AI** — gửi chuỗi trắng
> là dạy AI một bước vô nghĩa.

**Response thành công `200`:** `CommonResponse<string>` — `data` là **`prescriptionId`** đã được phản hồi.

```json
{
  "data": "424d0473-e501-4a35-8a66-d7f2740f8bcf",
  "listErrors": null,
  "isSuccess": true,
  "statusCode": 200,
  "message": "Đã ghi nhận phản hồi."
}
```

**Lỗi thường gặp** (đã kiểm chứng bằng request thật):

| Mã | Khi nào | Body |
|---|---|---|
| `400` | `status` rỗng hoặc không thuộc 3 giá trị | `listErrors: [{"field":"Status","detail":"Chỉ nhận: accepted, edited, rejected."}]` |
| `400` | `status = "edited"` mà thiếu `editedSteps` | `listErrors: [{"field":"EditedSteps","detail":"Bắt buộc khi status = edited."}]` |
| `401` | Chưa đăng nhập | — |
| `404` | Không tìm thấy alert, **hoặc** Customer gọi alert không thuộc mình | `"Không tìm thấy alert."` |
| `409` | Alert có thật nhưng **chưa có prescription** để phản hồi | `"Alert này chưa có prescription của AI để phản hồi."` |
| `410` | AI **không còn giữ** `prescriptionId` đó (hết TTL) — **thử lại vô ích** | `"Prescription đã hết hạn ở phía AI — không ghi nhận được phản hồi nữa."` |
| `503` | Không kết nối được AI — **nên thử lại sau** | `"Không kết nối được AI để ghi nhận phản hồi. Thử lại sau."` |

> 🔴 **`409` / `410` / `503` cố ý tách nhau** để client biết khi nào NÊN retry: `409` cần gọi
> `ai-prescription` trước; `410` retry vô ích; `503` retry được.

> **Sửa lỗi 2026-08-06:** trước ngày này, mọi lỗi validate của endpoint đều trả **`502`**
> (*"Upstream service không phản hồi hợp lệ"*) thay vì `400`. Nguyên nhân: `ValidateAsync()` không gán
> `StatusCode`, để nguyên mặc định `0`, controller gọi `StatusCode(0, result)` khiến Kestrel ghi ra dòng
> status `HTTP/1.1 0` — client nhận `BadStatusLine` và gateway dịch thành `502`. Hậu quả: người dùng gõ
> sai `status` thấy y hệt lúc AI sập, còn `listErrors` thì không bao giờ tới nơi. Đã sửa (gán
> `StatusCode = 400`, đúng quy ước của 26/28 command khác trong service).

---

## Phụ lục — Bẫy đã biết khi tích hợp AI

Ba điểm dưới đây là **lỗi im lặng**: hệ thống không báo lỗi, chỉ trả số sai. Ghi lại để không ai phải
tìm lại từ đầu.

**1. `socMode` quyết định payload gửi sang AI — gửi sai KHÔNG bị từ chối.**
Ý nghĩa của cột `soc_percent` phụ thuộc bộ artifact đang chấm: bộ NASA dùng `"window"` (SOC cục bộ trong
cửa sổ), bộ LFP dùng `"cycle"` (SOC theo chu kỳ xả). BE đọc `socMode` **từ endpoint health của AI**, không
suy từ `chemistry`. Gửi sai định nghĩa thì AI vẫn nhận, vẫn trả `200`, chỉ là SOH **lệch đi** — đo được
tới ~60 điểm SOH trên cùng một bộ số đo. Khi `socMode` là `"window"` hoặc `"unknown"`, BE gửi **4 cột**;
khi `"cycle"` mới gửi **6 cột** (kèm `cycle_count`, `soc_percent`).

**2. Bộ lọc đọc `sensor_readings` phải giống nhau ở mọi đường.**
Job nền, `ai-prescription`, `/long` và `/batch` đều lọc theo
`sensorSourceCode IN (null, "", "primary")` — **không** hardcode `SourceType`. Dữ liệu production đến từ
IoT gateway (`source_type = 2`), nên lọc theo `Bms` sẽ khiến mọi pin chạy gateway **không bao giờ** kê
được đơn, dù job nền vẫn dự đoán cho chúng bình thường.

**3. Deadline gRPC của Prescribe khác Predict.**
`enrich = true` chạy RAG + LLM nên mất vài giây; dùng deadline 5s của Predict sẽ khiến **mọi** lượt
prescribe `DeadlineExceeded` rồi mới fallback sang HTTP — tốn thêm 5 giây mỗi lần và log trông y hệt AI
đang hỏng. Đường enriched dùng `max(30, TimeoutSeconds)`; đường rule-based giữ nguyên deadline ngắn để
phát hiện AI treo thật sự.
