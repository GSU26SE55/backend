# API Documentation — BatteryService

> Base URL: `http://localhost:{port}/api`
> Content-Type: `application/json`
> Response wrapper chuẩn: `CommonResponse<T>`

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
    "totalCount": 100,
    "pageNumber": 1,
    "pageSize": 10
  }
}
```

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
| `batteryAssetId` | `Guid?` | Không | Lọc alert của một asset cụ thể |
| `severity` | `AlertSeverityEnum?` | Không | Lọc theo mức độ nghiêm trọng |
| `status` | `AlertStatusEnum?` | Không | Lọc theo trạng thái |
| `from` | `DateTime?` | Không | Lọc từ thời điểm phát sinh (UTC) |
| `to` | `DateTime?` | Không | Lọc đến thời điểm phát sinh (UTC) |

**Response thành công `200`:** `PaginationResponse<AlertDto>`

**Chi tiết `AlertDto`:**

| Field | Type | Nullable | Mô tả |
|---|---|---|---|
| `id` | `Guid` | Không | ID cảnh báo |
| `batteryAssetId` | `Guid` | Không | ID battery asset phát sinh cảnh báo |
| `batterySerialNumber` | `string` | Không | Serial number của pin (để hiển thị) |
| `anomalyType` | `AnomalyTypeEnum` | Không | Loại bất thường (xem enum) |
| `severity` | `AlertSeverityEnum` | Không | Mức độ nghiêm trọng (xem enum) |
| `thresholdValue` | `decimal` | Không | Giá trị ngưỡng đã cấu hình |
| `actualValue` | `decimal` | Không | Giá trị thực tế tại thời điểm phát sinh |
| `unit` | `string` | Không | Đơn vị đo (e.g., `V`, `°C`, `%`) |
| `detectedAt` | `DateTime` | Không | Thời điểm phát hiện bất thường (UTC) |
| `status` | `AlertStatusEnum` | Không | Trạng thái hiện tại (xem enum) |
| `ticketId` | `Guid?` | Null nếu chưa có ticket | ID ticket liên kết (nếu đã auto-tạo hoặc link thủ công) |
| `acknowledgedByUserId` | `Guid?` | Null nếu chưa ack | ID user đã acknowledge |
| `acknowledgedAt` | `DateTime?` | Null nếu chưa ack | Thời điểm acknowledge (UTC) |
| `resolvedAt` | `DateTime?` | Null nếu chưa resolve | Thời điểm resolve (UTC) |
| `dedupWindowEndUtc` | `DateTime` | Không | Thời điểm kết thúc cửa sổ deduplication (Sprint 3) |
| `createdAt` | `DateTime` | Không | Thời điểm tạo record (UTC) |

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
- `400 isSuccess=false` — Alert không ở trạng thái có thể acknowledge (e.g., đã Resolved)

---

### `PATCH /api/alerts/{id}/resolve`

**Mục đích:** Đánh dấu cảnh báo đã được xử lý xong. Chuyển trạng thái → `Resolved`.

**Auth:** Bắt buộc (chỉ Admin/Manager/Staff — Customer không được phép)

**Path param:** `id` — Guid của alert

**Request body:** Không có

**Response thành công `200`:** `isSuccess = true`

---

## Nhóm 2 — Battery Assets (Tài sản Pin)

Base route: `/api/battery-assets`

---

### `GET /api/battery-assets`

**Mục đích:** Danh sách battery asset với phân trang và lọc (dành cho Admin/Manager/Staff).

**Auth:** Bắt buộc (Admin/Manager/Staff)

**Query params:**

| Param | Type | Bắt buộc | Mô tả |
|---|---|---|---|
| `pageNumber` | `int` | Không (mặc định 1) | Số trang |
| `pageSize` | `int` | Không (mặc định 10) | Số item mỗi trang |
| `keyword` | `string?` | Không | Tìm theo serial number hoặc location |
| `customerId` | `Guid?` | Không | Lọc theo khách hàng |
| `batteryTypeId` | `Guid?` | Không | Lọc theo loại pin |
| `siteId` | `Guid?` | Không | Lọc theo site |
| `batteryGroupId` | `Guid?` | Không | Lọc theo nhóm pin |
| `status` | `BatteryStatusEnum?` | Không | Lọc theo trạng thái |
| `includeDeleted` | `bool` | Không (mặc định `false`) | Bao gồm cả asset đã soft-delete |

**Response thành công `200`:** `PaginationResponse<BatteryAssetDto>`

**Chi tiết `BatteryAssetDto`:**

| Field | Type | Nullable | Mô tả |
|---|---|---|---|
| `id` | `Guid` | Không | ID battery asset |
| `serialNumber` | `string` | Không | Serial number duy nhất của pin (chữ in hoa, số, dấu gạch ngang, 5–64 ký tự) |
| `batteryTypeId` | `Guid` | Không | ID loại pin |
| `batteryTypeName` | `string` | Không | Tên loại pin (để hiển thị) |
| `siteId` | `Guid?` | Null nếu chưa gán site | ID site lắp đặt |
| `siteName` | `string?` | Null nếu chưa gán site | Tên site |
| `batteryGroupId` | `Guid?` | Null nếu không thuộc nhóm | ID nhóm pin |
| `batteryGroupName` | `string?` | Null nếu không thuộc nhóm | Tên nhóm pin |
| `customerId` | `Guid` | Không | ID khách hàng sở hữu |
| `installDate` | `DateTime` | Không | Ngày lắp đặt (UTC) |
| `warrantyEndDate` | `DateTime?` | Null nếu không có bảo hành | Ngày hết bảo hành (UTC) |
| `warrantyStatus` | `WarrantyStatusEnum` | Không | Trạng thái bảo hành (xem enum) |
| `location` | `string?` | Null nếu không có mô tả vị trí | Mô tả vị trí lắp đặt (text) |
| `latitude` | `decimal?` | Null nếu không có tọa độ | Vĩ độ (-90 đến 90) |
| `longitude` | `decimal?` | Null nếu không có tọa độ | Kinh độ (-180 đến 180) |
| `status` | `BatteryStatusEnum` | Không | Trạng thái hoạt động của pin |
| `notes` | `string?` | Null nếu không có | Ghi chú |
| `lastSensorReadingAt` | `DateTime?` | Null nếu chưa nhận reading nào | Thời điểm nhận sensor reading gần nhất |
| `createdAt` | `DateTime` | Không | Thời điểm tạo record (UTC) |

---

### `GET /api/battery-assets/my`

**Mục đích:** Danh sách battery asset của Customer đang đăng nhập.

**Auth:** Bắt buộc (Customer)

**Query params:** `pageNumber`, `pageSize`

**Response thành công `200`:** `PaginationResponse<BatteryAssetDto>`

---

### `GET /api/battery-assets/{id}`

**Mục đích:** Xem chi tiết một battery asset.

**Auth:** Bắt buộc

**Response thành công `200`:** `CommonResponse<BatteryAssetDto>`

---

### `GET /api/battery-assets/{id}/realtime`

**Mục đích:** Lấy snapshot realtime của pin: trạng thái hiện tại + sensor reading mới nhất + số alert đang mở.

**Auth:** Bắt buộc

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
| `assetId` | `Guid` | Không | ID battery asset |
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

### `POST /api/battery-assets`

**Mục đích:** Tạo battery asset mới (đăng ký pin vào hệ thống).

**Auth:** Bắt buộc (Admin/Manager)

**Request body:**

| Field | Type | Bắt buộc | Validation | Mô tả |
|---|---|---|---|---|
| `serialNumber` | `string` | **Bắt buộc** | 5–64 ký tự, chỉ `A-Z`, `0-9`, `-` (chữ in hoa) | Serial number duy nhất |
| `batteryTypeId` | `Guid` | **Bắt buộc** | Phải tồn tại trong DB | Loại pin |
| `customerId` | `Guid` | **Bắt buộc** | Phải tồn tại trong DB | Khách hàng sở hữu |
| `siteId` | `Guid?` | Không | Nếu truyền phải là Guid hợp lệ | Site lắp đặt |
| `batteryGroupId` | `Guid?` | Không | Nếu truyền phải là Guid hợp lệ | Nhóm pin |
| `installDate` | `DateTime` | **Bắt buộc** | Không ở tương lai, không cũ hơn 5 năm | Ngày lắp đặt |
| `warrantyEndDate` | `DateTime?` | Không | Phải sau `installDate` nếu truyền | Ngày hết bảo hành |
| `location` | `string?` | Không | Max 255 ký tự | Mô tả vị trí |
| `latitude` | `decimal?` | Không | -90 đến 90 | Vĩ độ |
| `longitude` | `decimal?` | Không | -180 đến 180 | Kinh độ |
| `notes` | `string?` | Không | Max 1000 ký tự | Ghi chú |

**Response thành công `200`:** `CommonResponse<BatteryAssetDto>`

---

### `PUT /api/battery-assets/{id}`

**Mục đích:** Cập nhật thông tin battery asset.

**Auth:** Bắt buộc (Admin/Manager)

**Path param:** `id` — Guid của asset

**Request body:** Giống POST, thêm:

| Field | Type | Bắt buộc | Mô tả |
|---|---|---|---|
| `warrantyStatus` | `WarrantyStatusEnum` | Không (mặc định `Active`) | Trạng thái bảo hành |
| `status` | `BatteryStatusEnum` | Không (mặc định `Active`) | Trạng thái hoạt động |

**Response thành công `200`:** `CommonResponse<BatteryAssetDto>`

---

### `DELETE /api/battery-assets/{id}`

**Mục đích:** Xóa mềm (soft delete) battery asset.

**Auth:** Bắt buộc (Admin)

**Response thành công `200`:** `isSuccess = true`

---

### `PATCH /api/battery-assets/{id}/restore`

**Mục đích:** Khôi phục battery asset đã bị soft-delete.

**Auth:** Bắt buộc (Admin)

**Response thành công `200`:** `isSuccess = true`

---

### `PATCH /api/battery-assets/{id}/transfer`

**Mục đích:** Chuyển quyền sở hữu battery asset sang khách hàng khác.

**Auth:** Bắt buộc (Admin/Manager)

**Request body:**

| Field | Type | Bắt buộc | Validation | Mô tả |
|---|---|---|---|---|
| `newCustomerId` | `Guid` | **Bắt buộc** | Khác empty GUID | ID khách hàng mới |
| `reason` | `string?` | Không | Max 500 ký tự | Lý do chuyển chủ sở hữu |

**Response thành công `200`:** `isSuccess = true`

---

## Nhóm 3 — Battery Groups (Nhóm Pin)

Base route: `/api/battery-groups`

---

### `GET /api/battery-groups`

**Mục đích:** Danh sách nhóm pin với phân trang và lọc.

**Auth:** Bắt buộc

**Query params:**

| Param | Type | Mô tả |
|---|---|---|
| `pageNumber` | `int` | Trang |
| `pageSize` | `int` | Số item/trang |
| `keyword` | `string?` | Tìm theo tên nhóm |
| `siteId` | `Guid?` | Lọc theo site |
| `batteryTypeId` | `Guid?` | Lọc theo loại pin |
| `includeDeleted` | `bool` | Bao gồm đã xóa (mặc định `false`) |

**Response thành công `200`:** `PaginationResponse<BatteryGroupDto>`

**Chi tiết `BatteryGroupDto`:**

| Field | Type | Nullable | Mô tả |
|---|---|---|---|
| `id` | `Guid` | Không | ID nhóm pin |
| `siteId` | `Guid` | Không | ID site chứa nhóm |
| `siteName` | `string` | Không | Tên site |
| `name` | `string` | Không | Tên nhóm pin |
| `batteryTypeId` | `Guid` | Không | ID loại pin trong nhóm |
| `batteryTypeName` | `string` | Không | Tên loại pin |
| `batteryCount` | `int` | Không | Số pin trong nhóm (cập nhật khi thêm/xóa asset) |
| `createdAt` | `DateTime` | Không | Thời điểm tạo (UTC) |

---

### `GET /api/battery-groups/{id}`

**Mục đích:** Xem chi tiết một nhóm pin.

**Response thành công `200`:** `CommonResponse<BatteryGroupDto>`

---

### `POST /api/battery-groups`

**Mục đích:** Tạo nhóm pin mới trong một site.

**Auth:** Bắt buộc (Admin/Manager)

**Request body:**

| Field | Type | Bắt buộc | Validation | Mô tả |
|---|---|---|---|---|
| `siteId` | `Guid` | **Bắt buộc** | Khác empty GUID | Site chứa nhóm |
| `name` | `string` | **Bắt buộc** | Max 100 ký tự | Tên nhóm |
| `batteryTypeId` | `Guid` | **Bắt buộc** | Khác empty GUID | Loại pin trong nhóm |

**Response thành công `200`:** `CommonResponse<BatteryGroupDto>`

---

### `PUT /api/battery-groups/{id}`

**Mục đích:** Cập nhật thông tin nhóm pin.

**Request body:** Giống POST thêm `id` từ route.

---

### `DELETE /api/battery-groups/{id}`

**Mục đích:** Xóa mềm nhóm pin.

**Auth:** Bắt buộc (Admin)

---

### `PATCH /api/battery-groups/{id}/restore`

**Mục đích:** Khôi phục nhóm pin đã xóa.

---

## Nhóm 4 — Battery Types (Loại Pin)

Base route: `/api/battery-types`

---

### `GET /api/battery-types`

**Mục đích:** Danh sách loại pin với phân trang.

**Auth:** Bắt buộc

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
| `id` | `Guid` | Không | ID loại pin |
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

**Response thành công `200`:** `CommonResponse<BatteryTypeDto>`

---

### `POST /api/battery-types`

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
| `description` | `string?` | Không | Max 1000 ký tự | Mô tả |

---

### `PUT /api/battery-types/{id}`

Giống POST, thêm `id` từ route.

---

### `DELETE /api/battery-types/{id}`

Soft delete loại pin.

---

### `PATCH /api/battery-types/{id}/restore`

Khôi phục loại pin đã xóa.

---

## Nhóm 5 — Sensor Readings (Dữ liệu Cảm biến)

Base route: `/api/sensor-readings`

> **Lưu ý:** SensorReading không extend `AuditableEntity`. Đây là time-series append-only data lưu trong TimescaleDB hypertable.

---

### `GET /api/sensor-readings/{batteryAssetId}/latest`

**Mục đích:** Lấy sensor reading mới nhất của một pin.

**Auth:** Bắt buộc

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
| `batteryAssetId` | `Guid` | Không | ID battery asset |
| `voltage` | `decimal` | Không | Điện áp (V) |
| `current` | `decimal` | Không | Dòng điện (A) — âm = đang xả |
| `temperature` | `decimal` | Không | Nhiệt độ (°C) |
| `socPercent` | `decimal` | Không | State of Charge (%) — 0 đến 100 |
| `cycleCount` | `int?` | Null nếu BMS không report | Số chu kỳ sạc/xả đã thực hiện |
| `sourceDeviceId` | `string?` | Null nếu không có | ID thiết bị IoT gửi data |

---

### `GET /api/sensor-readings/{batteryAssetId}/history`

**Mục đích:** Lịch sử sensor readings của một pin trong khoảng thời gian, có phân trang theo cursor.

**Auth:** Bắt buộc

**Query params:**

| Param | Type | Bắt buộc | Mô tả |
|---|---|---|---|
| `pageNumber` | `int` | Không | Không dùng cho time-series (dùng cursor thay thế) |
| `pageSize` | `int` | Không (mặc định 100) | Số record mỗi trang, max khuyến nghị 1000 |
| `from` | `DateTime?` | Không | Từ thời điểm (UTC) |
| `to` | `DateTime?` | Không | Đến thời điểm (UTC) |

**Response thành công `200`:** `PaginationResponse<SensorReadingDto>`

**Lưu ý hiệu suất:** TimescaleDB có thể chứa hàng triệu rows. Luôn truyền `from`/`to` để giới hạn scan range. FE dùng endpoint này cho hiển thị chart lịch sử, không dùng để hiển thị realtime (dùng `/latest` thay vì).

---

### `POST /api/sensor-readings/batch`

**Mục đích:** Ingest batch sensor readings từ IoT device/gateway (endpoint nội bộ).

**Auth:** API Key (IoT gateway xác thực bằng header `X-Api-Key`, không dùng JWT)

**Request body:**
```json
{
  "readings": [
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
| `batteryAssetId` | `Guid` | Bắt buộc | Phải tồn tại và Active | ID pin |
| `time` | `DateTime` | Bắt buộc | Không ở tương lai | Timestamp (UTC) |
| `voltage` | `decimal` | Bắt buộc | > 0 | Điện áp (V) |
| `current` | `decimal` | Bắt buộc | — | Dòng điện (A) |
| `temperature` | `decimal` | Bắt buộc | — | Nhiệt độ (°C) |
| `socPercent` | `decimal` | Bắt buộc | 0–100 | State of Charge (%) |
| `cycleCount` | `int?` | Không | >= 0 | Số chu kỳ |
| `sohPercent` | `decimal?` | Không | 0–100 nếu truyền | SOH từ AI module |
| `chargingState` | `ChargingStateEnum?` | Không | — | Trạng thái nạp/xả |
| `sourceDeviceId` | `string?` | Không | Max 64 ký tự | Device ID |

**Response thành công `200`:**
```json
{
  "isSuccess": true,
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
| `skipped` | `int` | Số reading bị bỏ qua (duplicate timestamp hoặc validation fail) |

---

## Nhóm 6 — Sites (Địa điểm lắp đặt)

Base route: `/api/sites`

---

### `GET /api/sites`

**Mục đích:** Danh sách site với phân trang và lọc (Admin/Manager/Staff).

**Auth:** Bắt buộc (Admin/Manager/Staff)

**Query params:**

| Param | Type | Mô tả |
|---|---|---|
| `pageNumber` | `int` | Trang |
| `pageSize` | `int` | Số item/trang |
| `keyword` | `string?` | Tìm theo tên site hoặc địa chỉ |
| `customerId` | `Guid?` | Lọc theo khách hàng |
| `status` | `SiteStatusEnum?` | Lọc theo trạng thái |
| `includeDeleted` | `bool` | Bao gồm đã xóa (mặc định `false`) |

**Response thành công `200`:** `PaginationResponse<SiteDto>`

**Chi tiết `SiteDto`:**

| Field | Type | Nullable | Mô tả |
|---|---|---|---|
| `id` | `Guid` | Không | ID site |
| `name` | `string` | Không | Tên site (e.g., `Nhà máy mặt trời An Giang 1`) |
| `customerId` | `Guid` | Không | ID khách hàng sở hữu |
| `address` | `string?` | Null nếu chưa cung cấp | Địa chỉ |
| `latitude` | `decimal?` | Null nếu không có tọa độ | Vĩ độ (-90 đến 90) |
| `longitude` | `decimal?` | Null nếu không có tọa độ | Kinh độ (-180 đến 180) |
| `capacityKw` | `decimal?` | Null nếu chưa cung cấp | Công suất hệ thống (kW) |
| `installDate` | `DateTime` | Không | Ngày lắp đặt hệ thống (UTC) |
| `status` | `SiteStatusEnum` | Không | Trạng thái site (xem enum) |
| `contactPersonName` | `string?` | Null nếu chưa cung cấp | Tên người liên hệ tại site |
| `contactPersonPhone` | `string?` | Null nếu chưa cung cấp | SĐT người liên hệ |
| `batteryGroupCount` | `int` | Không | Số nhóm pin tại site |
| `batteryAssetCount` | `int` | Không | Tổng số pin tại site |
| `activeBatteryAssetCount` | `int` | Không | Số pin đang Active |
| `createdAt` | `DateTime` | Không | Thời điểm tạo record (UTC) |

---

### `GET /api/sites/my`

**Mục đích:** Danh sách site của Customer đang đăng nhập.

**Auth:** Bắt buộc (Customer)

**Query params:** `pageNumber`, `pageSize`

---

### `GET /api/sites/{id}`

**Mục đích:** Xem chi tiết một site.

**Response thành công `200`:** `CommonResponse<SiteDto>`

---

### `GET /api/sites/{id}/dashboard`

**Mục đích:** Dashboard tổng hợp cho site: số pin, số alert, health score.

**Auth:** Bắt buộc

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
| `siteId` | `Guid` | Không | ID site |
| `name` | `string` | Không | Tên site |
| `customerId` | `Guid` | Không | ID khách hàng |
| `totalAssets` | `int` | Không | Tổng số pin (bao gồm cả Inactive, Decommissioned) |
| `activeAssets` | `int` | Không | Số pin đang Active |
| `assetsWithActiveAlerts` | `int` | Không | Số pin có ít nhất 1 alert Open/Acknowledged |
| `totalCapacityKw` | `decimal?` | Null nếu site chưa có `capacityKw` | Tổng công suất (kW) |
| `lastAlertAt` | `DateTime?` | Null nếu chưa có alert nào | Thời điểm alert gần nhất (UTC) |
| `healthScore` | `int` | Không | Điểm sức khỏe hệ thống 0–100 (tính từ SOH và alert) |

---

### `GET /api/sites/{siteId}/assets`

**Mục đích:** Danh sách battery asset thuộc một site.

**Auth:** Bắt buộc

**Query params:**

| Param | Type | Mô tả |
|---|---|---|
| `pageNumber` | `int` | Trang |
| `pageSize` | `int` | Số item/trang |
| `batteryGroupId` | `Guid?` | Lọc theo nhóm pin cụ thể |
| `status` | `BatteryStatusEnum?` | Lọc theo trạng thái |

**Response thành công `200`:** `PaginationResponse<BatteryAssetDto>`

---

### `POST /api/sites`

**Mục đích:** Tạo site mới.

**Auth:** Bắt buộc (Admin/Manager)

**Request body:**

| Field | Type | Bắt buộc | Validation | Mô tả |
|---|---|---|---|---|
| `name` | `string` | **Bắt buộc** | Max 200 ký tự | Tên site |
| `customerId` | `Guid` | **Bắt buộc** | Khác empty GUID | Khách hàng sở hữu |
| `address` | `string?` | Không | — | Địa chỉ |
| `latitude` | `decimal?` | Không | -90 đến 90 | Vĩ độ |
| `longitude` | `decimal?` | Không | -180 đến 180 | Kinh độ |
| `capacityKw` | `decimal?` | Không | > 0 nếu truyền | Công suất (kW) |
| `installDate` | `DateTime` | **Bắt buộc** | — | Ngày lắp đặt |
| `status` | `SiteStatusEnum` | Không (mặc định `Active`) | — | Trạng thái ban đầu |
| `contactPersonName` | `string?` | Không | — | Tên người liên hệ |
| `contactPersonPhone` | `string?` | Không | — | SĐT người liên hệ |

---

### `PUT /api/sites/{id}`

Cập nhật thông tin site. Giống POST thêm `id` từ route.

---

### `DELETE /api/sites/{id}`

Soft delete site.

---

### `PATCH /api/sites/{id}/restore`

Khôi phục site đã xóa.

---

## Nhóm 7 — Threshold Configs (Ngưỡng Cảnh báo)

Base route: `/api/threshold-configs`

> ThresholdConfig định nghĩa ngưỡng cho từng `BatteryType`. Khi sensor reading vượt ngưỡng, AnomalyDetector tạo Alert tự động.

---

### `GET /api/threshold-configs`

**Mục đích:** Danh sách cấu hình ngưỡng có phân trang.

**Auth:** Bắt buộc (Admin/Manager)

**Query params:**

| Param | Type | Mô tả |
|---|---|---|
| `pageNumber` | `int` | Trang |
| `pageSize` | `int` | Số item/trang |
| `batteryTypeId` | `Guid?` | Lọc theo loại pin |
| `isActive` | `bool?` | Mặc định `true` — chỉ lấy config đang active |

**Response thành công `200`:** `PaginationResponse<ThresholdConfigDto>`

**Chi tiết `ThresholdConfigDto`:**

| Field | Type | Nullable | Mô tả |
|---|---|---|---|
| `id` | `Guid` | Không | ID cấu hình |
| `batteryTypeId` | `Guid` | Không | ID loại pin áp dụng |
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

### `GET /api/threshold-configs/by-battery-type/{batteryTypeId}`

**Mục đích:** Lấy cấu hình ngưỡng đang active cho một loại pin.

**Auth:** Bắt buộc

**Query params:**

| Param | Type | Mô tả |
|---|---|---|
| `includeInactive` | `bool` | Mặc định `false` — trả luôn config inactive nếu không có active |

**Response thành công `200`:** `CommonResponse<ThresholdConfigDto>`

---

### `PUT /api/threshold-configs`

**Mục đích:** Upsert (tạo mới hoặc cập nhật) cấu hình ngưỡng cho một loại pin.

**Auth:** Bắt buộc (Admin)

**Request body:**

| Field | Type | Bắt buộc | Validation | Mô tả |
|---|---|---|---|---|
| `batteryTypeId` | `Guid` | **Bắt buộc** | Khác empty GUID | Loại pin |
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

**Cách hoạt động:**
- Nếu chưa có config cho `batteryTypeId` → tạo mới với `isActive = true`
- Nếu đã có config đang active → deactivate config cũ + tạo config mới

**Response thành công `200`:** `CommonResponse<ThresholdConfigDto>`

---

## Bảng tổng hợp Endpoints

| Method | Path | Mục đích | Auth |
|---|---|---|---|
| GET | `/api/alerts` | Danh sách cảnh báo | Mọi role |
| GET | `/api/alerts/{id}` | Chi tiết cảnh báo | Mọi role |
| PATCH | `/api/alerts/{id}/acknowledge` | Acknowledge cảnh báo | Mọi role |
| PATCH | `/api/alerts/{id}/resolve` | Resolve cảnh báo | Admin/Manager/Staff |
| GET | `/api/battery-assets` | Danh sách pin (admin) | Admin/Manager/Staff |
| GET | `/api/battery-assets/my` | Danh sách pin (customer) | Customer |
| GET | `/api/battery-assets/{id}` | Chi tiết pin | Mọi role |
| GET | `/api/battery-assets/{id}/realtime` | Realtime snapshot pin | Mọi role |
| POST | `/api/battery-assets` | Tạo pin | Admin/Manager |
| PUT | `/api/battery-assets/{id}` | Cập nhật pin | Admin/Manager |
| DELETE | `/api/battery-assets/{id}` | Xóa pin | Admin |
| PATCH | `/api/battery-assets/{id}/restore` | Khôi phục pin | Admin |
| PATCH | `/api/battery-assets/{id}/transfer` | Chuyển chủ sở hữu | Admin/Manager |
| GET | `/api/battery-groups` | Danh sách nhóm pin | Mọi role |
| GET | `/api/battery-groups/{id}` | Chi tiết nhóm pin | Mọi role |
| POST | `/api/battery-groups` | Tạo nhóm pin | Admin/Manager |
| PUT | `/api/battery-groups/{id}` | Cập nhật nhóm pin | Admin/Manager |
| DELETE | `/api/battery-groups/{id}` | Xóa nhóm pin | Admin |
| PATCH | `/api/battery-groups/{id}/restore` | Khôi phục nhóm pin | Admin |
| GET | `/api/battery-types` | Danh sách loại pin | Mọi role |
| GET | `/api/battery-types/{id}` | Chi tiết loại pin | Mọi role |
| POST | `/api/battery-types` | Tạo loại pin | Admin |
| PUT | `/api/battery-types/{id}` | Cập nhật loại pin | Admin |
| DELETE | `/api/battery-types/{id}` | Xóa loại pin | Admin |
| PATCH | `/api/battery-types/{id}/restore` | Khôi phục loại pin | Admin |
| GET | `/api/sensor-readings/{id}/latest` | Reading mới nhất | Mọi role |
| GET | `/api/sensor-readings/{id}/history` | Lịch sử readings | Mọi role |
| POST | `/api/sensor-readings/batch` | Ingest batch (IoT) | API Key |
| GET | `/api/sites` | Danh sách site | Admin/Manager/Staff |
| GET | `/api/sites/my` | Site của customer | Customer |
| GET | `/api/sites/{id}` | Chi tiết site | Mọi role |
| GET | `/api/sites/{id}/dashboard` | Dashboard site | Mọi role |
| GET | `/api/sites/{siteId}/assets` | Pin tại site | Mọi role |
| POST | `/api/sites` | Tạo site | Admin/Manager |
| PUT | `/api/sites/{id}` | Cập nhật site | Admin/Manager |
| DELETE | `/api/sites/{id}` | Xóa site | Admin |
| PATCH | `/api/sites/{id}/restore` | Khôi phục site | Admin |
| GET | `/api/threshold-configs` | Danh sách ngưỡng | Admin/Manager |
| GET | `/api/threshold-configs/by-battery-type/{id}` | Ngưỡng theo loại pin | Mọi role |
| PUT | `/api/threshold-configs` | Upsert ngưỡng | Admin |
