# API Documentation — BatteryService

> Base URL: `http://localhost:{port}/api`
> Content-Type: `application/json`
> Response wrapper chuẩn: `CommonResponse<T>`
> **ID fields:** Tất cả `id` trong response đều là `string` (UUID dạng `"xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx"`). Entity C# dùng `Guid` nhưng serialize thành `string` trong JSON — TypeScript dùng `string` cho mọi id field.

---

## Cấu trúc Response chung

```json
{
  "isSuccess": true,
  "statusCode": 200,
  "message": "...",
  "data": { ... },
  "listErrors": []
}
```

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
| `HighAmbientTemp` | 9 | Nhiệt độ môi trường xung quanh site vượt ngưỡng | Ambient `Temperature > AmbientThresholdConfig.HighAmbientTempCritical` |
| `HighHumidity` | 10 | Độ ẩm môi trường vượt ngưỡng | Ambient `Humidity > AmbientThresholdConfig.HighHumidityCritical` |
| `HighTempHumidityCombo` | 11 | Combo nhiệt độ cao + độ ẩm cao đồng thời | Temp ≥ `ComboTempThreshold` AND Humidity ≥ `ComboHumidityThreshold` |
| `HighInternalResistance` | 12 | Điện trở trong tăng (Tier 2 battery health) | Tính từ Voltage/Current pattern |
| `CellImbalance` | 13 | Mất cân bằng giữa các cell | Tier 2 battery health analysis |
| `EnvironmentalIncident` | 14 | Liên kết tới `EnvironmentalIncident` cấp site (smoke/fire/flood…) | Tạo từ incident raise |
| `SensorMismatch` | 15 | BMS reading lệch IoT reading vượt ngưỡng (Sprint 7) | Cross-source mismatch check |

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
| `severity` | `AlertSeverityEnum?` | Không | Lọc theo mức độ nghiêm trọng |
| `status` | `AlertStatusEnum?` | Không | Lọc theo trạng thái; nếu truyền thì `excludeMerged` bị bỏ qua |
| `excludeMerged` | `bool` | Không (mặc định `true`) | Loại trừ alert có `status = Merged` — mặc định `true` nên FE chỉ thấy alert gốc. Truyền `false` để xem tất cả kể cả Merged (dành cho debug/admin) |
| `from` | `DateTime?` | Không | Lọc từ thời điểm phát sinh (UTC) |
| `to` | `DateTime?` | Không | Lọc đến thời điểm phát sinh (UTC) |

**Response thành công `200`:** `PaginationResponse<AlertDto>`

**Chi tiết `AlertDto`:**

| Field | Type | Nullable | Mô tả |
|---|---|---|---|
| `id` | `string` | Không | ID cảnh báo |
| `batteryAssetId` | `string` | Không | ID battery asset phát sinh cảnh báo |
| `batterySerialNumber` | `string` | Không | Serial number của pin (để hiển thị) |
| `anomalyType` | `AnomalyTypeEnum` | Không | Loại bất thường (xem enum) |
| `severity` | `AlertSeverityEnum` | Không | Mức độ nghiêm trọng (xem enum) |
| `thresholdValue` | `decimal` | Không | Giá trị ngưỡng đã cấu hình |
| `actualValue` | `decimal` | Không | Giá trị thực tế tại thời điểm phát sinh |
| `unit` | `string` | Không | Đơn vị đo (e.g., `V`, `°C`, `%`) |
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
- `400` — `id` là empty GUID
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
- `400` — `id` là empty GUID
- `404` — Alert không tìm thấy hoặc đã bị soft-delete
- `409 isSuccess=false` — Alert đang ở trạng thái `Merged`; phải resolve alert gốc thay vì alert đã merge

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

> **Sắp xếp mặc định:** `createdAt` giảm dần (mới nhất trước). Hiện không hỗ trợ sort params động — FE cần sort trên client nếu cần thứ tự khác.

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
| `serialNumber` | `string` | **Bắt buộc** | 5–64 ký tự, chỉ `A-Z`, `0-9`, `-` (chữ in hoa) | Serial number duy nhất |
| `batteryTypeId` | `string` | **Bắt buộc** | UUID hợp lệ, phải tồn tại trong DB | Loại pin |
| `customerId` | `string` | **Bắt buộc** | UUID hợp lệ, phải tồn tại trong DB | Khách hàng sở hữu |
| `siteId` | `string?` | Không | UUID hợp lệ nếu truyền | Site lắp đặt |
| `installDate` | `DateTime` | **Bắt buộc** | Không ở tương lai | Ngày lắp đặt |
| `warrantyEndDate` | `DateTime?` | Không | Phải sau `installDate` nếu truyền | Ngày hết bảo hành |
| `location` | `string?` | Không | Max 255 ký tự | Mô tả vị trí |
| `latitude` | `decimal?` | Không | -90 đến 90 | Vĩ độ |
| `longitude` | `decimal?` | Không | -180 đến 180 | Kinh độ |
| `notes` | `string?` | Không | Max 1000 ký tự | Ghi chú |

**Response thành công `200`:** `CommonResponse<BatteryAssetDto>`

**Lỗi thường gặp:**
- `400` — Validation field lỗi (xem `listErrors`)
- `409` — Serial number đã tồn tại trong hệ thống
- `409` — `siteId` không thuộc `customerId` đã truyền

> **Lưu ý:** Error responses có thể trả HTTP `400`/`404`/`409`; `statusCode` trong body khớp với HTTP status thật. FE nên kiểm tra `isSuccess` để xác định thành công.

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
| `keyword` | `string?` | Tìm theo tên loại pin |
| `includeDeleted` | `bool` | Bao gồm đã xóa (mặc định `false`) |

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
| `name` | `string` | **Bắt buộc** | Max 100 ký tự | Tên model |
| `manufacturer` | `string?` | Không | Max 100 ký tự | Nhà sản xuất |
| `nominalCapacityAh` | `decimal` | **Bắt buộc** | > 0 | Dung lượng danh định (Ah) |
| `nominalVoltage` | `decimal` | **Bắt buộc** | > 0 | Điện áp danh định (V) |
| `chemistry` | `BatteryChemistryEnum` | Không (mặc định `LiFePO4`) | — | Loại hóa học |
| `maxCycleCount` | `int` | Không (mặc định 2000) | > 0 | Số chu kỳ tối đa |
| `description` | `string?` | Không | Max 500 ký tự | Mô tả |

**Response thành công `200`:** `CommonResponse<BatteryTypeDto>`

**Lỗi thường gặp:**
- `400` — Validation field lỗi (xem `listErrors`)
- `409` — Tên loại pin đã tồn tại trong hệ thống

> **Lưu ý:** Controller dùng `Ok()` → HTTP `200`. `CommonResponse.statusCode` trong body cũng là `200`. FE nên kiểm tra `isSuccess` để xác định thành công.

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
| `cursor` | `DateTime?` | Không | Timestamp của record cuối trang trước; BE lấy record có `time < cursor` |

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
- `400` — `batteryAssetId` empty, `limit` ngoài `1–1000`, hoặc `from > to`

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

**Response thành công `200`:** `CommonResponse<SensorReadingAggregateDto[]>` — danh sách bucket sắp xếp tăng dần theo thời gian.

```json
{
  "isSuccess": true,
  "data": [
    {
      "bucket": "2026-06-12T07:00:00Z",
      "avgVoltage": 52.34,
      "avgCurrent": -1.82,
      "avgTemperature": 28.5,
      "avgSocPercent": 76.4,
      "avgSohPercent": 91.2,
      "sampleCount": 60
    }
  ]
}
```

**Chi tiết `SensorReadingAggregateDto`:**

| Field | Type | Nullable | Mô tả |
|---|---|---|---|
| `bucket` | `DateTime` | Không | Thời điểm bắt đầu bucket (UTC) |
| `avgVoltage` | `decimal` | Không | AVG điện áp (V) trong bucket |
| `avgCurrent` | `decimal` | Không | AVG dòng điện (A) trong bucket |
| `avgTemperature` | `decimal` | Không | AVG nhiệt độ (°C) |
| `avgSocPercent` | `decimal` | Không | AVG SOC (%) |
| `avgSohPercent` | `decimal?` | Null nếu bucket không có reading nào có SohPercent | AVG SOH (%) |
| `sampleCount` | `int` | Không | Số reading trong bucket |

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
      "time": "2026-05-16T08:00:00Z",
      "voltage": 52.3,
      "current": -2.5,
      "temperature": 28.4,
      "socPercent": 78.5,
      "cycleCount": 234,
      "sourceDeviceId": "DEVICE-001"
    }
  ]
}
```

**Validation mỗi reading:**

| Field | Type | Bắt buộc | Validation | Mô tả |
|---|---|---|---|---|
| `batteryAssetId` | `string` | Bắt buộc | UUID hợp lệ, phải tồn tại và Active | ID pin |
| `time` | `DateTime` | Bắt buộc | Không ở tương lai (cho phép lệch tối đa +5 phút) | Timestamp (UTC) |
| `voltage` | `decimal` | Bắt buộc | >= 0 | Điện áp (V) |
| `current` | `decimal` | Bắt buộc | — | Dòng điện (A) — âm = đang xả |
| `temperature` | `decimal` | Bắt buộc | -50 đến 120 (°C) | Nhiệt độ (°C) |
| `socPercent` | `decimal` | Bắt buộc | 0–100 | State of Charge (%) |
| `cycleCount` | `int?` | Không | >= 0 | Số chu kỳ |
| `sohPercent` | `decimal?` | Không | 0–100 nếu truyền | SOH từ AI module |
| `chargingState` | `ChargingStateEnum?` | Không | — | Trạng thái nạp/xả |
| `sourceDeviceId` | `string?` | Không | Max 64 ký tự | Device ID |

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
| `skipped` | `int` | Số reading bị bỏ qua vì `batteryAssetId` không tồn tại hoặc bị outlier |

**Error responses:**

| Status | Trường hợp |
|---|---|
| `400` | `items` rỗng, vượt giới hạn 1000 readings, hoặc có item không hợp lệ (trả `listErrors` chi tiết từng item) |
| `401` | Thiếu/sai `X-Api-Key` header hoặc scope `SensorIngest` không match |
| `500` | Duplicate `(BatteryAssetId, Time)` raise unique constraint từ TimescaleDB |

> **Lưu ý hiệu suất:** Không gửi quá nhiều batch nhỏ liên tiếp. Gateway nên gom readings trong 1 batch mỗi 30–60 giây. Không có rate limit cứng trong Sprint 3, nhưng sẽ thêm khi scale lên.

---

## Nhóm 5 — Sites (Địa điểm lắp đặt)

Base route: `/api/sites`

---

### `GET /api/sites`

**Mục đích:** Danh sách site với phân trang và lọc (Admin/Manager).

**Auth:** Bắt buộc (Admin/Manager)

**Query params:**

| Param | Type | Mô tả |
|---|---|---|
| `pageNumber` | `int` | Trang |
| `pageSize` | `int` | Số item/trang |
| `keyword` | `string?` | Tìm theo tên site hoặc địa chỉ |
| `customerId` | `string?` | Lọc theo khách hàng (UUID string) |
| `status` | `SiteStatusEnum?` | Lọc theo trạng thái |
| `includeDeleted` | `bool` | Bao gồm đã xóa (mặc định `false`) |

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
| `capacityKw` | `decimal?` | Null nếu chưa cung cấp | Công suất hệ thống (kW) |
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

**Response thành công `200`:** `CommonResponse<PaginationResponse<SiteDto>>` — danh sách site (có thể rỗng nếu Customer chưa có site nào).

**Error responses:**

| Status | Trường hợp |
|---|---|
| `401` | Token thiếu/invalid (middleware `[Authorize]` trả) |
| `403` | Không có role Customer |
| `500` | `UserId` claim trong JWT không parse được sang Guid — server-side data integrity issue (JWT do AuthService cấp sai/missing claim). Client không thể fix; cần alert dev team. |

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
    "totalCapacityKw": 500.0,
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
| `totalCapacityKw` | `decimal?` | Null nếu site chưa có `capacityKw` | Tổng công suất (kW) |
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

### `GET /api/sites/{siteId}/assets`

**Mục đích:** Danh sách battery asset thuộc một site.

**Auth:** Bắt buộc (Admin/Manager/Staff/Customer)

**Query params:**

| Param | Type | Mô tả |
|---|---|---|
| `pageNumber` | `int` | Trang |
| `pageSize` | `int` | Số item/trang |
| `status` | `BatteryStatusEnum?` | Lọc theo trạng thái |

**Response thành công `200`:** `PaginationResponse<BatteryAssetDto>`

---

### `POST /api/admin/sites`

**Mục đích:** Tạo site mới.

**Auth:** Bắt buộc (Admin)

**Request body:**

| Field | Type | Bắt buộc | Validation | Mô tả |
|---|---|---|---|---|
| `name` | `string` | **Bắt buộc** | Max 200 ký tự | Tên site |
| `customerId` | `string` | **Bắt buộc** | UUID hợp lệ, phải tồn tại trong DB | Khách hàng sở hữu |
| `address` | `string?` | Không | — | Địa chỉ |
| `latitude` | `decimal?` | Không | -90 đến 90 | Vĩ độ |
| `longitude` | `decimal?` | Không | -180 đến 180 | Kinh độ |
| `capacityKw` | `decimal?` | Không | > 0 nếu truyền | Công suất (kW) |
| `installDate` | `DateTime` | **Bắt buộc** | Không ở tương lai | Ngày lắp đặt |
| `status` | `SiteStatusEnum` | Không (mặc định `Active`) | — | Trạng thái ban đầu |
| `contactPersonName` | `string?` | Không | — | Tên người liên hệ |
| `contactPersonPhone` | `string?` | Không | — | SĐT người liên hệ |

---

### `PUT /api/admin/sites/{id}`

Cập nhật thông tin site. Giống POST thêm `id` từ route.

**Auth:** Bắt buộc (Admin)

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
| `sohCriticalThreshold` | `decimal?` | Không | < `sohWarningThreshold` nếu cả hai truyền | SOH Critical (%) |
| `effectiveFromUtc` | `DateTime` | Không (mặc định `UtcNow`) | — | Thời điểm có hiệu lực |

**Path param:** `batteryTypeId` — Guid của loại pin cần cấu hình.

**Cách hoạt động:**
- Nếu chưa có config cho `batteryTypeId` → tạo mới với `isActive = true`
- Nếu đã có config active → ghi đè các field threshold trên record đó

**Response thành công `200`:** `CommonResponse<ThresholdConfigDto>`

---

## Nhóm 7 — Dashboard

Base route: `/api/battery/dashboard`

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
| `offlineAssets` | `int` | Không | Số asset không nhận sensor reading trong 15 phút gần nhất |
| `openAlerts` | `int` | Không | Tổng alert đang `Open` hoặc `Acknowledged` |
| `openAlertsCritical` | `int` | Không | Alert severity `Critical` đang mở |
| `openAlertsWarning` | `int` | Không | Alert severity `Warning` đang mở |
| `openEnvironmentalIncidents` | `int` | Không | Số `EnvironmentalIncident` đang `Open` hoặc `Acknowledged` |
| `sites` | `int` | Không | Tổng số site (theo filter) |
| `assetStatusDistribution` | `AssetStatusBucketDto[]` | Không | Donut chart: mỗi bucket gồm `status` (enum int), `statusName`, `count` |
| `sohDistribution` | `SohBucketDto` | Không | Donut SOH: `healthy` (≥90%), `normal` (80–89%), `warning` (75–79%), `eol` (<75%), `unknown` (chưa có reading SOH) |
| `alertSeverityBreakdown` | `AlertSeverityBreakdownDto` | Không | Donut alert by severity toàn thời gian: `critical`, `warning`, `info` |
| `openAlertsByType` | `AlertByTypeDto[]` | Không | Bar chart: alert đang mở phân theo `AnomalyTypeEnum` |
| `alertTrend7Days` | `DailyTrendPointDto[]` | Không | Line chart 7 ngày — mỗi điểm: `date` (DateOnly), `critical`, `warning`, `info`, `total` |
| `ambientTrend24Hours` | `AmbientTrendPointDto[]` | Không | Line chart 24h gần nhất — mỗi điểm: `hourUtc`, `avgTemperature?`, `avgHumidity?`, `avgSolarIrradiance?` (có thể null nếu giờ đó không có data) |
| `sensorAggregate24Hours` | `SensorAggregateDto` | Không | Aggregated sensor metrics 24h: `avgVoltage?`, `avgCurrent?`, `avgTemperature?`, `avgSoc?`, `avgSoh?`, `readingsCount` (decimal fields nullable nếu không có data) |
| `topAlertingAssets` | `TopAlertingAssetDto[]` | Không | Top 5 asset có nhiều alert nhất 30 ngày qua |
| `environmentalIncidentsByType` | `EnvironmentalIncidentByTypeDto[]` | Không | Donut incident `Open`+`Acknowledged` theo type |
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

**Response thành công `201 Created`:** Tạo resource mới (ambient readings trong TimescaleDB). `CommonResponse<int>` — `data` là số reading đã insert.

```json
{
  "isSuccess": true,
  "statusCode": 201,
  "message": null,
  "data": 100,
  "listErrors": []
}
```

> Handler không set `message` (chỉ trả `isSuccess`, `statusCode`, `data`). `data` = `request.Items.Count` (số reading client gửi lên — handler chưa filter duplicate ở application layer, dedup phụ thuộc DB constraint / TimescaleDB hypertable).

**Error responses:**

| Status | Trường hợp |
|---|---|
| `400` | `items` rỗng, vượt 100 record, hoặc item không hợp lệ (xem `listErrors` chi tiết từng item: `Items[0].SiteId`, `Items[0].Humidity`, …) |
| `401` | Thiếu `X-Api-Key` |
| `403` | ApiKey không có scope `EnvironmentalIngest` |

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
| `200 OK` | **Dedup** (idempotency) | Đã tồn tại incident active đang `Open`/`Acknowledged` cho cùng `SiteId` (+ cùng `IncidentType`) → trả lại incident cũ thay vì tạo mới. KHÔNG phát event lần nữa. Idempotency-friendly cho IoT gateway spam cùng sự cố. |

Cả 2 path đều trả `CommonResponse<EnvironmentalIncidentDto>` — payload đầy đủ incident (shape giống `GET /{id}` bên dưới).

```json
{
  "isSuccess": true,
  "statusCode": 201,
  "data": { "id": "…", "siteId": "…", "status": 1, "incidentType": 2, "severity": 3, "detectedAt": "…", "acknowledgedAt": null, "resolvedAt": null, "falseAlarmAt": null }
}
```

**Error responses:**

| Status | Trường hợp |
|---|---|
| `400` | `SiteId` rỗng, `IncidentType`/`Severity` không hợp lệ, `DetectedAt` rỗng/vượt quá hiện tại 5' |
| `401` | Thiếu/sai `X-Api-Key` |
| `403` | ApiKey không có scope `EnvironmentalIngest` |

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
- `409` — State đang là `Resolved`

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
| GET | `/api/sites` | Danh sách site | Admin/Manager |
| GET | `/api/sites/me` | Site của customer | Customer |
| GET | `/api/sites/{id}` | Chi tiết site | Mọi role |
| GET | `/api/sites/{id}/dashboard` | Dashboard site | Mọi role |
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
