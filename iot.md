# IoT Implementation Plan — Solar Battery Monitoring

> **Document type:** IoT architecture, hardware checklist, backend backlog, gateway implementation plan
> **Scope:** Từ phần cứng/BMS/gateway tới backend `BatteryService` và flow alert/ticket/notification
> **Source of truth liên quan:** `overall.md` §52/§52bis
> **Cập nhật:** 2026-05-15

---

## 1. Mục tiêu

Xây hệ thống IoT để lấy dữ liệu pin mặt trời từ BMS/cảm biến, gửi về backend, lưu time-series, phát hiện bất thường và tạo alert/ticket.

Luồng chuẩn:

```text
Battery / BMS / Sensors
  -> IoT Gateway: Raspberry Pi / ESP32 / industrial gateway
  -> HTTPS REST API
  -> BatteryService
  -> TimescaleDB sensor_readings
  -> Anomaly Detection
  -> Alert
  -> TicketService
  -> NotificationService
  -> Web / Mobile
```

Nguyên tắc triển khai:
- Không để pin/BMS gọi backend trực tiếp.
- Gateway chịu trách nhiệm đọc sensor, normalize dữ liệu, retry, queue local.
- Backend chịu trách nhiệm định danh device, validate, lưu dữ liệu, phát hiện anomaly.
- MVP phải chạy được bằng simulator trước khi phụ thuộc phần cứng thật.

---

## 2. Trạng thái hiện tại

Backend hiện đã có:
- `POST /api/sensor-readings/batch`
- API key ingest cơ bản qua `X-Api-Key`
- `SensorReading` lưu TimescaleDB hypertable
- `BatteryAsset.LastSensorReadingAt`
- anomaly detection background job
- alert dedup
- outbox `BatteryAnomalyDetectedEvent`

Backend còn thiếu cho IoT production:
- `IotDevice`
- provision device
- heartbeat
- API key riêng từng device
- `X-Device-Code`
- `Idempotency-Key` trong ingest contract
- offline detection theo gateway
- calibration
- firmware OTA
- gateway simulator/hardware runbook

---

## 3. Thiết bị phần cứng cần chuẩn bị

### 3.1. MVP demo không cần pin thật

Dùng khi cần hoàn thiện backend/web/mobile trước:
- Laptop hoặc Raspberry Pi chạy gateway simulator.
- Không cần BMS thật.
- Script tự sinh `voltage`, `current`, `temperature`, `socPercent`, `sohPercent`.
- Gửi dữ liệu vào `POST /api/sensor-readings/batch`.

Mục tiêu MVP:
- Backend nhận reading.
- Web/Mobile thấy latest/history.
- Reading vượt threshold tạo alert.
- Critical alert đi tiếp sang ticket/notification flow.

### 3.2. Prototype phần cứng thật

Khuyến nghị:
- Raspberry Pi 4/5 làm IoT Gateway.
- Pin LiFePO4/NMC có BMS tích hợp.
- BMS có cổng giao tiếp rõ ràng: RS485/Modbus, CAN, UART hoặc Bluetooth.
- Tài liệu protocol/register map của BMS.
- USB-RS485 adapter nếu BMS dùng RS485/Modbus.
- CAN HAT hoặc USB-CAN nếu BMS dùng CAN.
- Nguồn cấp ổn định cho Raspberry Pi.
- Wi-Fi/Ethernet/4G để gửi dữ liệu lên backend.

Thiết bị phụ nếu cần:
- Cảm biến nhiệt độ ngoài thân pin.
- Cảm biến môi trường: nhiệt độ, độ ẩm.
- Cảm biến khói/nước rò nếu làm environmental incident.
- Enclosure, dây, cầu chì, đầu cos, terminal block.

### 3.3. Tiêu chí chọn BMS

Khi mua pin/BMS phải hỏi rõ:
- Có cổng RS485/CAN/UART/Bluetooth không?
- Có tài liệu register map/protocol không?
- Đọc được pack voltage không?
- Đọc được current không?
- Đọc được temperature không?
- Đọc được SOC không?
- Có SOH/cycle count/error code không?
- Protocol có checksum/CRC không?
- BMS dùng baud rate nào?

Không có protocol/register map thì vẫn có BMS thật nhưng rất khó đưa dữ liệu vào hệ thống.

---

## 4. Backend cần xây thêm

### 4.1. Data model

#### `IotDevice`

| Field | Mục đích |
|-------|----------|
| `Id` | PK |
| `DeviceCode` | Mã gateway, ví dụ `GW-001234` |
| `DeviceType` | Gateway hoặc standalone sensor |
| `Model` | `RaspberryPi-4B`, `ESP32-WROOM`, industrial gateway |
| `FirmwareVersion` | Version firmware/app gateway |
| `MacAddress` | Optional |
| `SiteId` | Gateway đặt tại site nào |
| `Status` | Provisioning, Active, Offline, Decommissioned |
| `ApiKeyId` | Link key metadata/hash |
| `LastSeenAt` | Cập nhật khi heartbeat/ingest |
| `BatteryAssetIds` | Mapping device quản lý battery nào |
| `ConfigJson` | Polling interval, sensors, mapping, threshold client-side |

#### `IotDeviceHeartbeat`

Time-series hypertable:
- `Time`
- `DeviceId`
- `Cpu`
- `MemoryUsageMb`
- `DiskFreeMb`
- `Temperature`
- `ConnectedSensorCount`
- `LocalQueueDepth`
- `IpAddress`
- `SignalStrengthDbm`

Retention đề xuất: 30 ngày.

#### `IotDeviceCalibration`

Dùng để hiệu chuẩn sensor:
- `DeviceId`
- `SensorMetric`: Voltage, Current, Temperature
- `OffsetValue`
- `ScaleFactor`
- `CalibratedAt`
- `CalibratedByUserId`
- `CalibrationStandard`
- `ValidUntil`

Formula:

```text
calibrated_value = raw_value * scale_factor + offset_value
```

#### `IotFirmwareRelease`

Dùng cho OTA firmware:
- `Version`
- `DeviceModel`
- `Channel`: Stable/Beta
- `FileId` từ FileStorageService
- `Sha256`
- `ReleaseNotes`
- `IsRequired`
- `ReleasedAt`

#### `IotFirmwareUpdateLog`

Gateway report trạng thái update:
- Pending
- Downloading
- Installing
- Success
- Failed
- RolledBack

### 4.2. API cần thêm

Admin:

```http
POST   /api/v1/admin/iot-devices
GET    /api/v1/admin/iot-devices?status=&siteId=
GET    /api/v1/admin/iot-devices/{id}
PUT    /api/v1/admin/iot-devices/{id}/config
DELETE /api/v1/admin/iot-devices/{id}
POST   /api/v1/admin/iot-firmware-releases
GET    /api/v1/admin/iot-firmware-releases
```

Device-side:

```http
POST   /api/v1/iot-devices/provision
POST   /api/v1/iot-devices/heartbeat
GET    /api/v1/iot-devices/firmware-check
PUT    /api/v1/iot-devices/firmware-update-log/{id}
POST   /api/sensor-readings/batch
```

Calibration:

```http
POST   /api/v1/iot-devices/{id}/calibrations
GET    /api/v1/iot-devices/{id}/calibrations
GET    /api/v1/iot-devices/calibrations-expiring?within=30d
```

Monitoring:

```http
GET    /api/v1/iot-devices/{id}/heartbeat-history?from=&to=
GET    /api/v1/iot-devices/{id}/uptime-stats
```

### 4.3. Provisioning flow

```text
1. Admin tạo IoT device trong web/admin API.
2. Backend sinh deviceCode + API key một lần.
3. Technician cài gateway tại site.
4. Technician chạy script provision trên gateway.
5. Gateway gọi /api/v1/iot-devices/provision.
6. Backend validate key + deviceCode, activate device.
7. Backend trả config cho gateway.
8. Gateway bắt đầu gửi heartbeat + sensor readings.
```

Payload provision:

```http
POST /api/v1/iot-devices/provision
X-Api-Key: <device-api-key>
Content-Type: application/json

{
  "deviceCode": "GW-001234",
  "macAddress": "AA:BB:CC:DD:EE:FF",
  "model": "RaspberryPi-4B",
  "firmwareVersion": "1.0.0"
}
```

Response:

```json
{
  "configJson": {
    "pollingIntervalSec": 30,
    "heartbeatIntervalSec": 60,
    "batchSize": 20,
    "batteryMappings": [
      {
        "batteryAssetSerial": "BAT-2026-001",
        "sensorSourceCode": "primary"
      }
    ]
  },
  "ntpServer": "pool.ntp.org",
  "syncIntervalSec": 60
}
```

### 4.4. Heartbeat flow

Gateway gửi mỗi 60 giây:

```http
POST /api/v1/iot-devices/heartbeat
X-Api-Key: <device-api-key>
X-Device-Code: GW-001234
Content-Type: application/json

{
  "timestamp": "2026-05-15T10:15:30Z",
  "cpu": 35.5,
  "memoryUsageMb": 512,
  "diskFreeMb": 14000,
  "connectedSensorCount": 4,
  "localQueueDepth": 0,
  "signalStrengthDbm": -65
}
```

Backend xử lý:
- Validate API key + `X-Device-Code`.
- Reject nếu device không Active.
- Insert `IotDeviceHeartbeat`.
- Update `IotDevice.LastSeenAt`.
- Export metrics.

### 4.5. Sensor ingest production contract

Giữ endpoint:

```http
POST /api/sensor-readings/batch
X-Api-Key: <device-api-key>
X-Device-Code: GW-001234
Idempotency-Key: <uuid>
Content-Type: application/json

{
  "deviceTimestamp": "2026-05-15T10:15:30Z",
  "readings": [
    {
      "batteryAssetSerial": "BAT-2026-001",
      "time": "2026-05-15T10:15:30Z",
      "voltage": 12.6,
      "current": -5.2,
      "temperature": 35.4,
      "socPercent": 78.5,
      "cycleCount": 120,
      "sohPercent": 94.2,
      "chargingState": 3,
      "bmsErrorCode": null,
      "sensorSourceCode": "primary"
    }
  ]
}
```

Backward compatibility MVP:
- Cho phép payload cũ dùng `items[].batteryAssetId`.
- Production ưu tiên mapping bằng `X-Device-Code` + `batteryAssetSerial`.

Validation bắt buộc:
- `deviceTimestamp` lệch server không quá 5 phút.
- `time` không nằm quá xa trong tương lai.
- `voltage` không âm và không vượt outlier hard-limit.
- `temperature` nằm trong giới hạn vật lý hợp lý.
- `socPercent` 0-100.
- `sohPercent` 0-100 nếu có.
- `BmsErrorCode` tối đa 64 ký tự nếu có.
- Device phải có quyền gửi dữ liệu cho battery đó.

---

## 5. Gateway software cần code

Gateway nên viết theo module:

```text
gateway/
  config.py
  main.py
  bms/
    base.py
    mock.py
    modbus.py
    canbus.py
  api/
    client.py
  queue/
    local_store.py
  telemetry/
    heartbeat.py
  logging_config.py
```

### 5.1. Config

Gateway cần config:
- Backend base URL.
- Device code.
- API key.
- Polling interval.
- Heartbeat interval.
- BMS adapter type: mock/modbus/canbus.
- Serial port hoặc CAN interface.
- Battery mapping.
- Local queue path.

Ví dụ:

```json
{
  "backendBaseUrl": "https://api.example.com",
  "deviceCode": "GW-001234",
  "apiKey": "secret",
  "pollingIntervalSec": 30,
  "heartbeatIntervalSec": 60,
  "adapter": "mock",
  "batteryMappings": [
    {
      "batteryAssetSerial": "BAT-2026-001",
      "source": "primary"
    }
  ]
}
```

### 5.2. Đọc dữ liệu BMS

Nếu chưa có phần cứng:
- Dùng `mock.py` sinh dữ liệu hợp lý.
- Có mode anomaly để sinh overheat/low SOC.

Nếu dùng RS485/Modbus:
- Dùng USB-RS485.
- Dùng thư viện Modbus.
- Đọc register theo tài liệu BMS.
- Convert scale: ví dụ raw voltage `1260` -> `12.60V`.

Nếu dùng CAN:
- Dùng CAN HAT/USB-CAN.
- Parse CAN frame theo protocol BMS.
- Map frame sang field backend.

### 5.3. Local queue

Gateway phải chịu được mất mạng:
- Nếu upload fail, ghi batch vào local queue.
- Retry theo exponential backoff.
- Gửi lại kèm cùng `Idempotency-Key`.
- Không xóa khỏi queue cho tới khi backend trả 2xx.

### 5.4. Heartbeat

Heartbeat gửi:
- CPU usage.
- RAM usage.
- Disk free.
- Gateway temperature nếu đọc được.
- Connected sensor count.
- Local queue depth.
- Signal strength nếu có.

### 5.5. Logging

Log tối thiểu:
- Gateway started/stopped.
- Provision success/fail.
- BMS read success/fail.
- Upload success/fail.
- Queue depth.
- Backend response status.
- Firmware update status.

---

## 6. Sprint plan

### IoT Sprint 0 — Simulator MVP

**Mục tiêu:** Chứng minh backend flow không cần phần cứng thật.

Tasks:
- [ ] Chuẩn bị `BatteryType`, `ThresholdConfig`, `BatteryAsset`.
- [ ] Viết gateway simulator gửi payload cũ vào `/api/sensor-readings/batch`.
- [ ] Sinh scenario normal, overheat, low SOC, SOH degradation.
- [ ] Kiểm tra latest/history query.
- [ ] Kiểm tra alert tạo khi vượt threshold.
- [ ] Kiểm tra critical alert publish event.

Acceptance:
- Một command chạy simulator được.
- Web/Mobile thấy dữ liệu realtime.
- Alert xuất hiện khi simulator gửi reading bất thường.

### IoT Sprint 1 — Backend Device Management

**Mục tiêu:** Có backend quản lý gateway thật.

Tasks:
- [ ] Thêm entity/migration `IotDevice`.
- [ ] Thêm entity/migration `IotDeviceHeartbeat`.
- [ ] Thêm entity/migration `IotDeviceCalibration`.
- [ ] Thêm entity/migration `IotFirmwareRelease`.
- [ ] Thêm entity/migration `IotFirmwareUpdateLog`.
- [ ] Thêm per-device API key hash.
- [ ] Thêm admin endpoints.
- [ ] Thêm device provision endpoint.
- [ ] Thêm heartbeat endpoint.
- [ ] Update ingest endpoint với `X-Device-Code`, `deviceTimestamp`, `Idempotency-Key`.
- [ ] Thêm offline detection background service.
- [ ] Thêm tests.

Acceptance:
- Admin tạo device và lấy key một lần.
- Gateway provision được.
- Heartbeat cập nhật `LastSeenAt`.
- Stop heartbeat tạo `DeviceOffline`.

### IoT Sprint 2 — Hardware Pilot

**Mục tiêu:** Raspberry Pi/gateway đọc được dữ liệu thật hoặc adapter mock-hardware.

Tasks:
- [ ] Setup Raspberry Pi OS.
- [ ] Cài gateway app.
- [ ] Cấu hình service chạy tự động bằng systemd.
- [ ] Test network/TLS.
- [ ] Kết nối USB-RS485 hoặc CAN.
- [ ] Đọc ít nhất voltage/current/temperature/SOC.
- [ ] Gửi production payload lên backend.
- [ ] Ghi runbook setup.

Acceptance:
- RPi gửi heartbeat.
- RPi gửi readings.
- Backend lưu readings.
- Dashboard hiển thị latest data.

### IoT Sprint 3 — Hardening + Demo

**Mục tiêu:** Demo ổn định và có failure scenario.

Tasks:
- [ ] Local queue khi mất mạng.
- [ ] Retry với `Idempotency-Key`.
- [ ] Metrics + Grafana dashboard.
- [ ] Calibration command + apply calibration.
- [ ] Firmware check happy path.
- [ ] Demo script: normal -> anomaly -> offline.
- [ ] Security review API key/device access.

Acceptance:
- Tắt mạng gateway rồi bật lại không mất dữ liệu.
- Gửi trùng request không duplicate.
- Stop gateway > 5 phút tạo offline alert.
- Demo chạy được bằng simulator và hardware path.

---

## 7. Thứ tự code khuyến nghị

1. Hoàn thiện simulator trước.
2. Test endpoint ingest hiện tại.
3. Tạo `IotDevice` và API key per-device.
4. Thêm provision.
5. Thêm heartbeat.
6. Update ingest contract production.
7. Thêm offline detection.
8. Thêm calibration.
9. Thêm firmware OTA.
10. Tích hợp Raspberry Pi/BMS thật.

Lý do: backend + simulator giúp chứng minh flow nghiệp vụ trước. Phần cứng thật chỉ nên đưa vào sau khi API và dashboard đã ổn định.

---

## 8. Security checklist

- [ ] API key per-device, không dùng global key dài hạn.
- [ ] API key chỉ hiện một lần khi tạo/provision.
- [ ] Lưu hash key, không lưu plaintext.
- [ ] Hỗ trợ rotate/revoke key.
- [ ] Bắt buộc TLS khi deploy.
- [ ] Rate limit theo device.
- [ ] Validate `X-Device-Code` khớp API key.
- [ ] Device chỉ gửi dữ liệu cho battery được mapping.
- [ ] Reject clock skew > 5 phút.
- [ ] Reject sensor outlier.
- [ ] Log reject reason nhưng không log API key.

---

## 9. Demo checklist

- [ ] Có ít nhất 1 `BatteryAsset`.
- [ ] Có threshold đủ để trigger anomaly.
- [ ] Gateway simulator gửi normal readings.
- [ ] Web/Mobile thấy latest reading.
- [ ] Gateway simulator gửi overheat hoặc low SOC.
- [ ] Backend tạo alert.
- [ ] Critical alert đi sang ticket/notification nếu service tương ứng đã chạy.
- [ ] Gateway gửi heartbeat.
- [ ] Dừng gateway > 5 phút.
- [ ] Backend mark device Offline và tạo `DeviceOffline` alert.
- [ ] Có script/reset data để chạy lại demo.
