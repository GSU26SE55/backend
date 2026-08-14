# IoT Zero-Touch — Phương án A với WiFi khách hàng

> **Ngày:** 2026-08-07
> **Bối cảnh:** Kế hoạch để ESP32 gateway "cắm điện là chạy" — tự nối WiFi, tự nối MQTT, tự nối backend.
> **Quyết định đã chốt:** dùng **WiFi của khách hàng** (không cấp router 4G riêng).
> **Phạm vi:** repo `backend` (BatteryService) + repo `iot` (firmware-esp32).

---

## Mục lục

1. [Quyết định này thay đổi những gì](#phần-1--quyết-định-này-thay-đổi-những-gì)
2. [Ba tầng thông tin](#phần-2--ba-tầng-thông-tin-bản-mới)
3. [Cách hoạt động](#phần-3--cách-hoạt-động)
4. [Toàn bộ việc phải làm](#phần-4--toàn-bộ-việc-phải-làm)
5. [Kiểm chứng](#phần-5--kiểm-chứng-t59)
6. [Việc ngoài code](#phần-6--việc-ngoài-code-đừng-bỏ-qua)
7. [Rủi ro còn lại](#phần-7--rủi-ro-còn-lại)
8. [Quyết định đã chốt](#quyết-định-đã-chốt-2026-08-07)

---

# PHẦN 1 — Quyết định này thay đổi những gì

## Cái thay đổi cốt lõi

Trước (router 4G riêng): SSID/PASS **giống nhau mọi site** → nhúng firmware, xong.
Giờ (WiFi khách): SSID/PASS **khác nhau từng nhà** → phải cấu hình tại chỗ, và **phải sửa lại được** khi khách đổi.

## Hệ quả kéo theo: captive portal từ "tuỳ chọn" thành **bắt buộc**

Với WiFi khách, sự cố **phổ biến nhất** sẽ không phải hỏng pin hay hỏng cảm biến. Nó là:

> *"Khách đổi mật khẩu WiFi / đổi router / nhà mạng thay modem."*

Chuyện này xảy ra vài lần mỗi năm mỗi nhà, và khách **không hề biết** mình vừa làm thiết bị của bạn chết. Bạn chỉ phát hiện khi alert `DeviceOffline` nổ.

Lúc đó cần một cách để nạp WiFi mới. Có hai cách:

| Cách | Ai làm được | Chi phí mỗi lần |
|---|---|---|
| Serial CLI qua USB | Chỉ kỹ thuật viên **có laptop** | Cử người + đi lại: vài trăm nghìn ~ 1 buổi |
| Captive portal qua điện thoại | Kỹ thuật viên, **hoặc chính khách hàng** theo hướng dẫn | Có thể 0đ nếu khách tự làm |

Với router 4G riêng, sự cố này không tồn tại nên serial CLI là đủ. Với WiFi khách, nó là sự cố thường trực — nên **cơ chế phục hồi phải rẻ**. Đó là lý do captive portal chuyển từ "nên có" sang "phải có".

## Chi phí của quyết định

| | Router 4G riêng | WiFi khách (đã chọn) |
|---|---|---|
| Chi phí phần cứng | +500k–1.5tr/site | 0 |
| Chi phí vận hành | ~50–100k/tháng SIM | 0 |
| Công code thêm | 0 | **+13h** (WiFi NVS + captive portal + AP fallback) |
| Sự cố "khách đổi WiFi" | Không có | Vài lần/năm/site |
| Sóng không tới tủ pin | Không có (router đặt cạnh tủ) | Có thể xảy ra |
| Vấn đề riêng tư | Không | Cần điều khoản hợp đồng |

---

# PHẦN 2 — Ba tầng thông tin (bản mới)

Mọi thông tin thiết bị cần đều thuộc **một trong ba tầng**, phân loại theo câu hỏi: *"Giá trị này giống nhau hay khác nhau giữa các thiết bị?"*

## Tầng 1 — Nhúng trong firmware, chung cả fleet

| Thông tin | Đổi bằng cách nào |
|---|---|
| `BACKEND_URL` | Rebuild + OTA |
| CA cert | Rebuild + OTA (hoặc ghi đè qua LittleFS) |
| Mật khẩu AP setup (`SolarGW-Setup`) | Rebuild + OTA |
| Giá trị mặc định (polling, ngưỡng) | Rebuild + OTA |

## Tầng 2 — Riêng từng thiết bị, lưu NVS, cấu hình tại chỗ

| Thông tin | Ai nạp | Khi nào |
|---|---|---|
| **`wifiSsid`** ⭐mới | Kỹ thuật viên | Lúc lắp + mỗi khi khách đổi WiFi |
| **`wifiPass`** ⭐mới | Kỹ thuật viên | Lúc lắp + mỗi khi khách đổi WiFi |
| `deviceCode` | Kỹ thuật viên | Lúc lắp, **một lần duy nhất** |
| `apiKey` | Kỹ thuật viên | Lúc lắp, đổi khi rotate |

Bốn giá trị, nhập cùng một lúc trên **một trang web trong điện thoại**.

## Tầng 3 — Backend cấp lúc chạy, không chạm thiết bị

| Thông tin | Nguồn |
|---|---|
| MQTT host/port/TLS/prefix/user/pass | `/provision` |
| `pollingInterval`, `heartbeatInterval` | `/provision` |
| `siteId`, `ntpServer` | `/provision` |
| `batteryMappings` (danh sách pin) | `/provision` — nếu chốt gộp T4.1 |
| Hệ số calibration | Backend tự áp, không gửi xuống |

> **Nguyên tắc bất biến:** Tầng 1 đổi phải reflash — tránh tối đa. Tầng 2 phải có mặt tại nhà khách — làm cho nó rẻ nhất có thể. Tầng 3 chỉ cần click web — đẩy được gì xuống đây thì đẩy.

---

# PHẦN 3 — Cách hoạt động

## 3.1 — Máy trạng thái WiFi (thiết kế cốt lõi)

ESP32 có ba chế độ sóng, ta dùng cả ba:

| Chế độ | Nghĩa là gì |
|---|---|
| `WIFI_STA` | Máy trạm — nối vào router như điện thoại |
| `WIFI_AP` | Điểm phát — tự phát sóng cho máy khác nối vào |
| `WIFI_AP_STA` | **Cả hai cùng lúc** — vừa nối router vừa phát sóng |

```
                 ┌──────────────────────────────────┐
   cắm điện ───► │  BOOT: đọc NVS                   │
                 └────────────┬─────────────────────┘
                              │
              chưa có wifiSsid│         đã có wifiSsid
              ┌───────────────┴───────────────┐
              ▼                               ▼
    ┌──────────────────┐            ┌──────────────────┐
    │  SETUP MODE      │            │  CONNECTING      │
    │  WIFI_AP         │            │  WIFI_STA        │
    │  phát SolarGW-   │            │  thử 30s         │
    │  Setup           │            └────┬────────┬────┘
    │  LED tím nháy    │           nối OK│        │thất bại
    │  chờ vô hạn      │                 ▼        ▼
    └────────┬─────────┘        ┌──────────────┐ │
             │lưu 4 giá trị     │   ONLINE     │ │
             └──────► reboot    │  provision → │ │
                                │  MQTT →      │ │
                                │  ingest      │ │
                                │  LED xanh    │ │
                                └──────┬───────┘ │
                                 mất   │         │
                                 sóng  ▼         ▼
                            ┌────────────────────────────┐
                            │  RECOVERY                  │
                            │  0–5 phút:  WIFI_STA,      │
                            │             thử lại mỗi 5s │
                            │             LED cam        │
                            │  > 5 phút:  WIFI_AP_STA    │
                            │             VỪA thử lại    │
                            │             VỪA phát AP    │
                            │             LED tím/cam    │
                            │  vẫn lấy mẫu + xếp hàng    │
                            └──────┬──────────────┬──────┘
                          nối lại  │              │ có người
                          được     ▼              ▼ cấu hình lại
                            ONLINE (tắt AP)    lưu → reboot
```

### Ba điểm tinh tế

**1. Ngưỡng 5 phút.** Vì sao không mở AP ngay?
- Router khách khởi động lại mất ~2 phút. Mở AP ngay thì mỗi lần cúp điện khu phố, hàng loạt thiết bị phát sóng lạ trong nhà khách — khách hoảng.
- Vì sao không đợi lâu hơn? Vì kỹ thuật viên đến nơi phải chờ chừng đó mới cấu hình được.

**2. `WIFI_AP_STA` chứ không phải `WIFI_AP`.** Ở chế độ phục hồi, thiết bị vừa phát sóng setup **vừa tiếp tục thử nối** WiFi cũ. Nếu router khách sống lại, nó tự nối, tự tắt AP, không cần ai làm gì. Nếu chỉ `WIFI_AP` thì thiết bị mắc kẹt ở chế độ setup mãi mãi dù mạng đã có lại.

**3. Không mất dữ liệu.** Suốt quá trình mất mạng, `sampleAndQueueOffline()` (GH-737) vẫn đọc BMS và xếp hàng vào LittleFS, MQ-2 và cảm biến rò nước vẫn lấy mẫu (GH-736).

---

## 3.2 — Kịch bản lắp đặt lần đầu

### Chuẩn bị (văn phòng, trước khi đi)

Admin tạo device trên web → in **nhãn dán** lên vỏ thiết bị:

```
┌────────────────────────────────────┐
│  SOLAR GATEWAY                     │
│  GW-ESP32-001                      │
│                                    │
│      ▄▄▄▄▄  ▄ ▄▄  ▄▄▄▄▄            │
│      █ ▄ █  ▀█▄▀  █ ▄ █            │  ← QR: iot://provision?dc=…&key=…
│      █▄▄▄█  ▄▄ ▀  █▄▄▄█            │
│                                    │
│  Setup WiFi: SolarGW-Setup         │
│  Mật khẩu:   solar2026             │
└────────────────────────────────────┘
```

Backend đã sinh sẵn `ProvisioningQrCode` cho việc này — chỉ cần render ra ảnh và in.

### Tại nhà khách (~5 phút)

| Bước | Việc | Ai |
|---|---|---|
| 1 | Lắp tủ, đấu RS485 từ BMS về ESP32, cấp nguồn | KTV |
| 2 | LED **tím nháy** — thiết bị đang phát `SolarGW-Setup` | — |
| 3 | Điện thoại nối vào `SolarGW-Setup`, mật khẩu `solar2026` | KTV |
| 4 | Trình duyệt **tự mở** trang cấu hình (đó là ý nghĩa của "captive portal") | — |
| 5 | Chọn WiFi khách từ danh sách thiết bị tự dò được, hỏi khách mật khẩu, gõ vào | KTV + khách |
| 6 | Quét QR trên nhãn → 2 ô `deviceCode`/`apiKey` **tự điền** | KTV |
| 7 | Bấm **Lưu** → ghi NVS → tự reboot | — |
| 8 | LED **cam** (đang nối) → **xanh** (xong) trong ~15 giây | — |
| 9 | Mở web admin, thấy device `Active`, có số liệu về | KTV |

> Bước 6 rất đáng làm: `apiKey` dài 47 ký tự, gõ tay sẽ sai. Quét QR là 1 giây và không bao giờ sai.

### Từ lúc bấm Lưu đến khi có số liệu — chi tiết từng giây

| t | Việc |
|---|---|
| 0s | Reboot, đọc NVS: có đủ 4 giá trị |
| ~2s | `WIFI_STA` → nối WiFi khách → có IP |
| ~3s | `identityBegin()` nạp `deviceCode` + `apiKey` |
| ~3s | `mqttcfg::begin()` → chưa có cấu hình MQTT → `mqttBegin()` im lặng no-op |
| ~3s | `httpClientBegin()` nạp CA nhúng → TLS sẵn sàng |
| ~5s | NTP sync xong |
| ~5s | `POST /api/iot-devices/provision` với `X-Api-Key` + `X-Device-Code` |
| | Backend: verify khoá → khớp mã → lệch giờ ≤ 300s → `Status = Active` → **trả 6 field MQTT + config** |
| ~6s | Ghi NVS, `mqttApplyConfig()` |
| ~6–11s | MQTT CONNECT (LWT `offline` retain) → publish `online` retain → subscribe `/cmd` |
| ~11s | Đọc BMS → publish `solar/gw-esp32-001/{serial}/telemetry` mỗi pin |
| ~11s | Bridge tra `MqttUsername` → khớp → row vào `sensor_readings` |

**~15 giây từ lúc bấm Lưu.**

---

## 3.3 — Chạy thường ngày

| Việc | Chu kỳ | Đường đi |
|---|---|---|
| Telemetry | `pollingInterval` (backend cấp, mặc định 10s) | **MQTT**, rơi về HTTPS sau 3 lần fail |
| Heartbeat | 60s | HTTPS |
| Ambient SHT31 | 60s | HTTPS |
| MQ-2 / rò nước | 1s / 0.5s | HTTPS, lấy mẫu **vô điều kiện** |
| OTA check | 1h | HTTPS |
| Đẩy bù hàng đợi | theo backoff | HTTPS (luôn) |

Thiết bị **không bao giờ** hỏi lại provision khi đã có `provd=1`, trừ khi bị kích hoạt re-provision (mục 3.6).

---

## 3.4 — ⭐ Khách đổi mật khẩu WiFi (kịch bản quan trọng nhất)

```
Khách đổi pass lúc 14:00
  │
14:00  ESP32 mất kết nối. WIFI_STA thử lại mỗi 5s — đều fail (sai pass).
       LED cam. Vẫn đọc BMS, vẫn xếp hàng vào LittleFS.
  │
14:00  Broker mất keep-alive (30s) → phát LWT retain "offline"
       → Bridge: Status=Offline, tạo Alert(DeviceOffline) cho MỖI pin trong site,
         đẩy IotDeviceWentOfflineEvent → NotificationService báo Staff
  │
14:05  Quá 5 phút → chuyển WIFI_AP_STA:
       vừa phát "SolarGW-Setup", vừa tiếp tục thử WiFi cũ.
       LED tím/cam xen kẽ.
  │
14:05→ Staff thấy alert, gọi khách: "Anh/chị vừa đổi WiFi phải không ạ?"
  │
       ┌─ Khách tự làm được (có hướng dẫn 1 trang):
       │    nối SolarGW-Setup → nhập WiFi mới → Lưu → xong.  Chi phí: 0đ
       │
       └─ Khách không rành → cử KTV, dùng điện thoại, 3 phút tại chỗ.
  │
       Reboot → nối WiFi mới → provision KHÔNG chạy lại (provd=1 còn nguyên)
       → mqttcfg đọc NVS → MQTT nối lại
       → tryFlushQueue() đẩy bù toàn bộ số liệu tích trong lúc offline,
         khoá Idempotency-Key chống ghi trùng
       → heartbeat kế lật Status: Offline → Active
       → Alert(DeviceOffline) đóng lại
```

**Không mất một điểm dữ liệu nào** — miễn là hàng đợi LittleFS chưa đầy.

> Nên chuẩn bị sẵn **1 trang A4 hướng dẫn** dán trong nắp tủ. Đầu tư 30 phút, tiết kiệm rất nhiều chuyến đi.

---

## 3.5 — Mất mạng tạm thời (router reboot, cúp điện khu phố)

Giống 3.4 nhưng **kết thúc ở phút thứ 2–3**: router sống lại, `WIFI_STA` tự nối, chưa kịp mở AP. Thiết bị tự đẩy bù hàng đợi. **Không ai cần làm gì.**

Đây là lý do ngưỡng 5 phút quan trọng — nó lọc sự cố thật khỏi nhiễu thường ngày.

---

## 3.6 — Admin xoay khoá

- **Xoay riêng MQTT:** thiết bị fail CONNECT với `state=4`, sau 5 lần → tự `clearProvisionFlag()` → provision lại → lấy credential mới. **Tự lành.**
- **Xoay `apiKey`:** mất **cả hai** đường (HTTPS cũng 401). **Không tự lành** — phải nạp lại qua captive portal.

⇒ Nên tách hai endpoint `rotate-key` và `rotate-mqtt`. Bình thường chỉ dùng `rotate-mqtt`.

---

## 3.7 — Mất điện / reboot

`loadProvisioned()` thấy `provd=1` → **không gọi provision** → đọc WiFi + MQTT từ NVS → nối thẳng. Lên mạng sau ~10 giây.

> ⚠️ **Chỗ dễ sót nhất trong toàn bộ plan:** nhánh "đã provisioned" ([main.cpp:100-107](../iot/firmware-esp32/src/main.cpp#L100-L107)) **cũng phải** gọi `mqttApplyConfig()`. Quên là boot lần đầu chạy đẹp, boot lần hai trở đi âm thầm chạy HTTPS-only **mà không có log lỗi nào** (vì HTTPS vẫn hoạt động). Bắt buộc test bằng cách reboot **hai lần**.

---

## 3.8 — OTA

NVS **sống sót qua OTA**. Sau khi flash firmware mới, WiFi + deviceCode + apiKey + MQTT config vẫn còn nguyên, thiết bị nối lại không cần cấu hình. Rollback cũng vậy.

---

# PHẦN 4 — Toàn bộ việc phải làm

## Nhóm 0 — Hai việc chặn, ship riêng trước

### T0.1 · Chuẩn hoá case `DeviceCode` ở ranh giới MQTT `[BE]` `~2h`

Bug đang sống — MQTT hỏng cả hai chiều dù cấu hình đúng.

| # | File | Sửa |
|---|---|---|
| a | [MqttBridgeBackgroundService.cs:184-189](services/BatteryService/src/BatteryService.Infrastructure/Mqtt/MqttBridgeBackgroundService.cs#L184-L189) | Telemetry lookup: `d.DeviceCode ==` → `d.MqttUsername ==` |
| b | `:214-215` | Heartbeat — như trên |
| c | `:238-240` | Status/LWT — như trên |
| d | `MqttTopicMap.Command()` | Lowercase deviceCode khi dựng topic |
| e | [AdminIotDevicesController.cs:441](services/BatteryService/src/BatteryService.Api/Controllers/Admin/AdminIotDevicesController.cs#L441) | Trường `Topic` trả về phải khớp topic thật |
| f | `MqttBridgeE2ETests` | Seed `DeviceCode = "GW-TEST-A"` + `MqttUsername = "gw-test-a"` — hiện seed lowercase nên test **giả xanh** |

**Bối cảnh lỗi:**

| Nơi | Giá trị | Nguồn |
|---|---|---|
| DB `iot_devices.device_code` | `GW-ESP32-001` | `ToUpperInvariant()` — `CreateIotDeviceCommandHandler.cs:30` |
| MQTT username + ACL `%u` + topic prefix | `gw-esp32-001` | `ToLowerInvariant()` — `IotApiKeyService.cs:149` |
| Topic downlink backend publish | `solar/GW-ESP32-001/cmd` | `AdminIotDevicesController.cs:420-441` |

- **Uplink chết:** device buộc publish `solar/gw-esp32-001/...` (ACL `pattern write solar/%u/+/telemetry`). Bridge parse ra `gw-esp32-001` rồi tra `d.DeviceCode == deviceCode` — Postgres so chuỗi phân biệt hoa/thường ⇒ **không khớp** ⇒ log `"MQTT telemetry from unknown device"` ⇒ số liệu rơi im lặng.
- **Downlink chết:** backend publish `solar/GW-ESP32-001/cmd`, ACL cho device đọc `solar/gw-esp32-001/cmd` ⇒ không bao giờ nhận.

### T0.2 · Nối đường credential DB → broker `[Infra]` `~1h`

`MqttPasswordFileSyncService` viết xong nhưng **chưa từng chạy** — thiếu `Mqtt__PasswordFilePath`.

| # | File | Sửa |
|---|---|---|
| a | [.env.Docker:216-226](.env.Docker#L216-L226) | Thêm `Mqtt__PasswordFilePath=/mqtt-config/passwd` + `Mqtt__CredentialSyncIntervalSeconds=60` |
| b | [docker-compose.yml](docker-compose.yml) service `batteryservice` | Thêm khối `volumes:` (hiện **không có**): `./infra/mqtt/mosquitto:/mqtt-config` **rw** |
| c | [docker-compose.yml:151](docker-compose.yml#L151) | Bỏ `:ro` ở volume `passwd` |
| d | `docker-compose.prod.yml:386-394` | Mirror a+b+c |
| e | `.env.Docker.example`, `env.prod.example` | Thêm 2 biến |

> **Bẫy quyền:** service ghi file mode 0600, Mosquitto 2.0 **từ chối nạp file người khác đọc được**. Hai container khác UID có thể không đọc nổi. Kiểm bằng `docker exec solar-mosquitto cat /mosquitto/config/passwd`.

> **Bẫy thứ hai:** `bootstrap.sh` set passwd `0700`, sync service ghi `0600`. Chạy bootstrap sau sync sẽ đè. Chốt thứ tự: bootstrap **một lần trước**, sync chạy sau và giữ nguyên dòng `backend-bridge` nhờ vùng có mốc trong [`MosquittoPasswordFile.Compose`](services/BatteryService/src/BatteryService.Application/Mqtt/MosquittoPasswordFile.cs).

---

## Nhóm 1 — Backend

### T1.1 · Cột `MqttPasswordPlaintext` `~1.5h`

| # | File | Việc |
|---|---|---|
| a | [IotDevice.cs](services/BatteryService/src/BatteryService.Domain/Entities/IotDevice.cs) | Thêm `public string? MqttPasswordPlaintext { get; set; }` dưới `MqttPasswordHash` |
| b | [IotDeviceConfiguration.cs](services/BatteryService/src/BatteryService.Infrastructure/Persistence/Configurations/IotDeviceConfiguration.cs) | `.HasColumnName("mqtt_password_plaintext").HasMaxLength(64)` |
| c | Migration | `dotnet ef migrations add AddIotDeviceMqttPasswordPlaintext -p ../BatteryService.Infrastructure -s .` |
| d | Checklist | Cột nullable ⇒ không cần `defaultValue`. **Vẫn phải test rollback** (be.md §14) |

> Tiền lệ đã có: `ApiKeyPlaintext` lưu plaintext từ 2026-07-16, `GET /api/admin/iot-devices/{id}` trả full key. Cột này **không mở ra loại phơi nhiễm mới** — cùng bảng, cùng endpoint, cùng lớp quyền Admin.

### T1.2 · Thêm 6 field MQTT vào `IotDeviceProvisionResultDto` `~30ph`

[IotDeviceDto.cs:197-224](services/BatteryService/src/BatteryService.Application/DTOs/IotDeviceDto.cs#L197-L224):

```
MqttBrokerHost   string?   // null = MQTT chưa bật → device chạy HTTPS-only
MqttBrokerPort   int?
MqttUseTls       bool?
MqttTopicPrefix  string?   // "solar/gw-esp32-001" — device nối đuôi, KHÔNG tự ghép
MqttUsername     string?
MqttPassword     string?   // plaintext
```

**Không** thêm CA cert PEM vào đây — xem T2.4e.

### T1.3 · `ProvisionIotDeviceCommandHandler` điền MQTT `~1h`

| # | Việc |
|---|---|
| a | Inject `IMqttBrokerEndpointProvider` — đã đăng ký DI ([ManageDependencyInjection.cs:85-86](services/BatteryService/src/BatteryService.Infrastructure/DependencyInjection/ManageDependencyInjection.cs#L85-L86)), hiện chỉ dùng ở create handler |
| b | `_brokerEndpoint.Resolve(device.DeviceCode)` → điền `MqttBrokerHost/Port/UseTls/TopicPrefix` |
| c | `MqttUsername = device.MqttUsername`, `MqttPassword = device.MqttPasswordPlaintext` |
| d | `broker.Host is null` (MQTT tắt) ⇒ để **cả 6 field null**, không trả nửa vời |

### T1.4 · Tự vá device thiếu credential `~1h`

```
nếu MqttUsername == null HOẶC MqttPasswordPlaintext == null:
    cred = _apiKeyService.GenerateMqttCredential(device.DeviceCode)
    device.MqttUsername          = cred.Username
    device.MqttPasswordHash      = cred.PasswordHash
    device.MqttPasswordPlaintext = cred.RawPassword
```

Cần inject `IIotApiKeyService` (hiện handler chỉ inject `IBatteryUnitOfWork`).

> Vá tại provision chứ không viết script backfill: device không bao giờ boot thì cũng không cần credential; cách này tự đúng cho cả device mới lẫn cũ.

### T1.5 · Đồng bộ passwd tức thì `~45ph`

| # | Việc |
|---|---|
| a | `SyncOnceAsync` đã để `public` **đúng cho mục đích này** ([dòng 96-101](services/BatteryService/src/BatteryService.Infrastructure/Mqtt/MqttPasswordFileSyncService.cs#L96-L101)) nhưng nằm ở Infrastructure, handler ở Application — không được tham chiếu ngược |
| b | Tách `IMqttCredentialSync` ở `Application/Interfaces`, đăng ký **cùng khuôn** `IMqttBridgePublisher` ([dòng 92](services/BatteryService/src/BatteryService.Infrastructure/DependencyInjection/ManageDependencyInjection.cs#L92)) |
| c | Đổi `AddHostedService<...>()` → `AddSingleton` + `AddHostedService(sp => sp.GetRequiredService<...>())` |
| d | Gọi sau `SaveChangesAsync` trong provision, **bọc try-catch** — sync hỏng không được làm provision fail |
| e | Gọi luôn trong `CreateIotDeviceCommandHandler` và `RotateIotDeviceApiKeyCommandHandler` |

> Vẫn còn độ trễ tới 5s vì vòng `passwd-watch` trong compose so mtime mỗi 5 giây. Chấp nhận được: firmware retry mỗi 5s nên tự lành.

### T1.6 · Sửa `rotate-key` bỏ quên MQTT `~45ph`

[IotDeviceCommandHandlers.cs:105-130](services/BatteryService/src/BatteryService.Application/CQRS/Handler/IotDevice/IotDeviceCommandHandlers.cs#L105-L130): rotate **chỉ xoay API key**, và [`ToCreatedDto()`](services/BatteryService/src/BatteryService.Application/Mapping/IotDeviceMapper.cs#L67) **không map field Mqtt nào** ⇒ admin nhận DTO toàn null.

| # | Việc |
|---|---|
| a | Rotate cũng `GenerateMqttCredential()` → ghi cả 3 field MQTT |
| b | Điền 6 field MQTT vào DTO |
| c | Gọi `IMqttCredentialSync.SyncOnceAsync()` |
| d | Cân nhắc đưa phần gán MQTT vào `ToCreatedDto()` để create + rotate không lệch nhau lần nữa |
| e | **Cân nhắc tách endpoint `rotate-mqtt` riêng** (mục 3.6) |

### T1.7 · Sửa `Channel` dùng nhầm làm `SensorSourceCode` `~30ph` ⭐mới

[DeviceLifecycleHandlers.cs:85](services/BatteryService/src/BatteryService.Application/CQRS/Handler/IotDevice/DeviceLifecycleHandlers.cs#L85):

```csharp
m.SensorSourceCode = cal?.Channel ?? "primary";
```

`Channel` mang `"voltage"/"current"/"temperature"`. `SensorSourceCode` phải là `"primary"/"redundant"/"external-temp"`. **Hai khái niệm hoàn toàn khác nhau.**

Chưa nổ vì firmware không đọc field này, nhưng là quả mìn: ngày firmware đọc `batteryMappings` là ngày mọi truy vấn lọc `primary` bắt đầu rơi số liệu (mọi truy vấn tính toán phải lọc `SensorSourceCode == "primary" || null`, không thì đếm gấp 3 lần).

### T1.8 · Cập nhật tài liệu API `~20ph`

`DeviceCode` lưu UPPERCASE, `MqttUsername` = lowercase, topic dùng `MqttTopicPrefix`, tuyệt đối không tự ghép topic từ `DeviceCode`.

---

## Nhóm 2 — Firmware: cấu hình runtime

### T2.1 · Khoá NVS mới `~1h`

> ⚠️ **Khoá NVS tối đa 15 ký tự** (Preferences spec, ghi ở [nvs_store.h](../iot/firmware-esp32/src/config/nvs_store.h)).

| Khoá | Kiểu | Nội dung |
|---|---|---|
| `wifissid` ⭐ | string | SSID |
| `wifipass` ⭐ | string | mật khẩu |
| `mqhost` | string | broker host |
| `mqport` | int32 | port |
| `mqtls` | uint8 | 0/1 |
| `mqprefix` | string | `solar/gw-esp32-001` |
| `mquser` | string | MQTT username |
| `mqpass` | string | MQTT password |

Cập nhật khối chú thích liệt kê khoá ở đầu `nvs_store.h`.

### T2.2 · `config/wifi_config.{h,cpp}` `~2h` ⭐mới

Nhân bản khuôn [`device_identity.cpp`](../iot/firmware-esp32/src/config/device_identity.cpp): NVS → fallback compile-time → hot-reload.

```
namespace wificfg {
  void begin();
  const char* ssid(); const char* password();
  bool isConfigured();
  bool save(const char* ssid, const char* pass);
  bool clear();
}
```

### T2.3 · `config/mqtt_config.{h,cpp}` `~2.5h`

```
namespace mqttcfg {
  void begin();
  const char* host(); int port(); bool useTls();
  const char* topicPrefix();      // fallback: "solar/" + lowercase(deviceCode)
  const char* username(); const char* password();
  bool applyFromProvision(host, port, useTls, prefix, user, pass);
  bool isConfigured();
  void printStatus();             // mask password
}
```

Dùng lại `core::validateIdentityField()` để chặn giá trị quá khổ — bài học GH-749.

### T2.4 · Nhúng CA cert, bỏ `uploadfs` `~2h`

Hiện có **hai** chỗ nạp CA độc lập: `mqtt_client.cpp::loadCaCert()` và `http_client.cpp::httpConfigureTls()`. Thiếu file thì **cả HTTPS lẫn MQTT chết** — `postJsonInternal` return luôn, không gửi request nào.

| # | Việc |
|---|---|
| a | Sinh `src/net/ca_cert_embedded.h` từ `ca.crt` trong CI |
| b | Cả 2 chỗ: **ưu tiên LittleFS**, fallback bản nhúng (đổi CA không cần reflash) |
| c | Giữ `tls::isLikelyPemCertificate()` cho cả hai nguồn |
| d | Sửa comment [`tls_ca.h`](../iot/firmware-esp32/src/net/tls_ca.h) — nhắc tới `tls_ca_device.cpp` mà file đó **không tồn tại** |
| e | Vì đã nhúng, provision **không** cần trả CA — tránh tràn `respBuf` |

### T2.5 · `mqtt_client.cpp` đọc runtime `~2.5h`

| # | Sửa |
|---|---|
| a | [dòng 229-231](../iot/firmware-esp32/src/net/mqtt_client.cpp#L229-L231): `setServer(mqttcfg::host(), mqttcfg::port())` |
| b | [dòng 125-132](../iot/firmware-esp32/src/net/mqtt_client.cpp#L125-L132): `connect(clientId, mqttcfg::username(), mqttcfg::password(), ...)` |
| c | **4 hàm publish + LWT + subscribe**: đổi sang `mqttcfg::topicPrefix()` thay vì ghép `MQTT_TOPIC_PREFIX` + `identity::deviceCode()` — xoá sạch lớp lỗi hoa/thường phía thiết bị |
| d | `mqttBegin()` trả false khi `!mqttcfg::isConfigured()` — chưa provision thì im lặng, không spam log |
| e | `warnIfCaseMismatch()` viết lại: so `mqttcfg::topicPrefix()` với `"solar/" + lowercase(deviceCode)` |
| f | Thêm `mqttApplyConfig()` — cấu hình đổi → `disconnect()` + `s_lastReconnectMs = 0` (khuôn có sẵn ở `mqttOnIdentityChanged()`) |

> **Cần chốt:** `#if MQTT_USE_TLS` là compile-time nên `WiFiClientSecure` vs `WiFiClient` chọn lúc build. Đề xuất **giữ TLS compile-time** (`MQTT_USE_TLS=1` cố định), runtime chỉ mang host/port/prefix/user/pass.

### T2.6 · `provision.cpp` parse + áp dụng `~1.5h`

| # | Sửa |
|---|---|
| a | Parse 6 field mới từ `data{}` |
| b | `mqttcfg::applyFromProvision(...)` → ghi NVS |
| c | `net::mqttApplyConfig()` để phiên MQTT dựng lại ngay, không đợi reboot |
| d | `MqttBrokerHost` null ⇒ **không ghi đè NVS**, log "MQTT chưa bật — HTTPS-only" |
| e | ⚠️ **Nâng `respBuf` 2048 → 4096** ([dòng 93](../iot/firmware-esp32/src/provision/provision.cpp#L93)) — `batteryMappings[]` đã ăn phần lớn buffer |
| f | Cập nhật log dòng 154 in thêm trạng thái MQTT |

### T2.7 · Re-provision khi credential hỏng `~1.5h`

| # | Việc |
|---|---|
| a | Đếm riêng `state()` = **4** (BAD_CREDENTIALS) / **5** (UNAUTHORIZED) — khác hẳn `-2`/`-4` (mạng). Bảng map lý do có sẵn ở [dòng 138-149](../iot/firmware-esp32/src/net/mqtt_client.cpp#L138-L149) |
| b | Đủ 5 lần liên tiếp → `provision::clearProvisionFlag()` + `s_provisionDone = false` |
| c | **Cooldown 15 phút** chống vòng lặp khi backend chết |
| d | Chỉ áp cho lỗi 4/5. Lỗi mạng **không** kích hoạt re-provision |

### T2.8 · `main.cpp` sắp thứ tự khởi động `~1h`

| # | Việc |
|---|---|
| a | Gọi `wificfg::begin()` + `mqttcfg::begin()` sau `identity::identityBegin()` |
| b | `mqttBegin()` giữ nguyên vị trí ([dòng 595](../iot/firmware-esp32/src/main.cpp#L595)), tự no-op khi chưa cấu hình |
| c | ⚠️ **Nhánh đã-provisioned** ([dòng 100-107](../iot/firmware-esp32/src/main.cpp#L100-L107)) **cũng phải** gọi `mqttApplyConfig()` — bỏ sót là boot lần 2 chạy HTTPS-only âm thầm |
| d | `logStatsPeriodic()` in thêm `wifi cfg=nvs\|compile`, `mqtt cfg=nvs\|compile` |

### T2.9 · Cập nhật `config.h` / `config.example.h` `~30ph`

Khối WiFi + MQTT thành **fallback**, không còn là nguồn chân lý. Giá trị bắt buộc điền tay chỉ còn `BACKEND_URL` (và mật khẩu AP setup).

---

## Nhóm 3 — Firmware: WiFi tại hiện trường ⭐ toàn bộ là mới

### T3.1 · `wifi_manager.cpp` đọc NVS `~1.5h`

`wifiBegin()` lấy từ `wificfg::` thay tham số truyền vào. Giữ throttle 5s ở `wifiTick()`. Thêm `wifiReconfigure(ssid, pass)` — đổi nóng, không reboot.

> Lưu ý: [`WiFi.persistent(false)`](../iot/firmware-esp32/src/net/wifi_manager.cpp#L57) đang tắt cơ chế nhớ WiFi riêng của ESP32. Giữ nguyên — ta tự quản NVS, một nguồn chân lý duy nhất.

### T3.2 · Máy trạng thái + AP fallback `~3h`

Hiện thực đúng sơ đồ mục 3.1:
- Chưa cấu hình → `WIFI_AP`, chờ vô hạn
- Mất mạng < 5 phút → `WIFI_STA`, thử lại mỗi 5s
- Mất mạng ≥ 5 phút → `WIFI_AP_STA`, **vừa phát AP vừa thử lại**
- Nối lại được → tắt AP, về `WIFI_STA`
- **Không** chặn `loop()` — lấy mẫu + xếp hàng vẫn chạy suốt

### T3.3 · Captive portal `~6h`

| # | Việc |
|---|---|
| a | Thêm `tzapu/WiFiManager` hoặc tự viết `WebServer` + `DNSServer` (~250 dòng) |
| b | AP `SolarGW-Setup`, **có mật khẩu WPA2** in trên nhãn — không để mở |
| c | DNS wildcard trỏ mọi tên miền về `192.168.4.1` → điện thoại tự bật trang |
| d | Trang HTML: dropdown WiFi (dò được), ô mật khẩu, ô `deviceCode`, ô `apiKey` |
| e | Nút **Quét QR** đọc `iot://provision?dc=…&key=…` tự điền 2 ô |
| f | Validate trước khi lưu: `deviceCode` ≤ 64 ký tự, `apiKey` bắt đầu `iotk_` |
| g | Lưu NVS → hiện "Đã lưu, đang khởi động lại…" → reboot sau 2s |
| h | Timeout 10 phút ở chế độ RECOVERY; chế độ SETUP lần đầu chờ vô hạn |
| i | Hiển thị **RSSI** khi chọn mạng; < −75 dBm thì cảnh báo |
| j | Lọc và cảnh báo khi chỉ dò được mạng 5GHz |

### T3.4 · Đèn LED phân biệt trạng thái `~1h`

`led_palette.h` thêm:

| Màu | Trạng thái |
|---|---|
| Tím nháy | SETUP — đang phát AP, chờ cấu hình |
| Cam | CONNECTING / RECOVERY < 5 phút |
| Tím/cam xen kẽ | RECOVERY ≥ 5 phút — có AP |
| Xanh | ONLINE |
| Xanh nháy | ONLINE nhưng còn hàng đợi chưa đẩy |

> Đây là công cụ chẩn đoán duy nhất mà **khách hàng dùng được qua điện thoại**: "anh chụp giúp em cái đèn đang màu gì".

### T3.5 · Lệnh CLI dự phòng `~1.5h`

`set wifi <ssid> <pass>`, `set mqttuser/mqttpass/mqttbroker/mqttprefix`, mở rộng `show`, thêm `wifiscan`.

> ⚠️ `set devcode` hiện gọi `core::decideDeviceCodeChange(val, MQTT_USERNAME, ...)` — so với **macro compile-time**. Khi username thành runtime phải đổi thành `mqttcfg::username()`, không thì lệnh này từ chối sai/chấp nhận sai.

> ✅ `set apikey` và `set devcode` **đã hoạt động sẵn** hôm nay ([serial_cli.cpp:126-160](../iot/firmware-esp32/src/cli/serial_cli.cpp#L126-L160)) — dùng được ngay mà không cần chờ gì cả.

---

## Nhóm 4 — Tuỳ chọn

### T4.1 · `batteryMappings` runtime `~3h`

Backend đã trả `batteryMappings[]` nhưng [`provision.cpp`](../iot/firmware-esp32/src/provision/provision.cpp#L127-L131) **chỉ parse 4 field** (`pollingIntervalSeconds`, `heartbeatIntervalSeconds`, `siteId`, `ntpServer`) — mảng này bị **bỏ qua hoàn toàn**. Firmware dùng bảng cứng [`config::kBatteryMappings`](../iot/firmware-esp32/src/config/battery_mapping.h#L38).

⇒ Danh sách pin cũng đang là Tầng 1. Kéo xuống Tầng 3: parse → NVS → `bms_source` đọc runtime.

Đây là lý do triệu chứng `[ingest] ⚠ NHẬN THIẾU: 2/4 reading vào được` (GH-748) xuất hiện: firmware khai serial không có ở site đó, backend bỏ vì `mapping_invalid`.

> **Với WiFi khách nên LÀM** — bạn đã phải ra hiện trường vì WiFi rồi; đừng để phải ra thêm lần nữa chỉ vì khách lắp thêm pin.

---

## Nhóm 5 — Test

| # | Loại | Nội dung |
|---|---|---|
| T5.1 | BE unit | Provision: MQTT bật → 6 field; tắt → 6 null; thiếu credential → tự sinh; sync ném exception → vẫn 200 |
| T5.2 | BE unit | Rotate xoay cả MQTT + DTO đủ field |
| T5.3 | BE unit | `MqttTopicMap.Command("GW-ABC")` → `solar/gw-abc/cmd` |
| T5.4 | BE integ | `MqttBridgeE2ETests` seed uppercase — chặn regression T0.1 |
| T5.5 | BE integ | Passwd sync: `Disabled` biến mất, `backend-bridge` còn nguyên |
| T5.6 | FW native | `wificfg` + `mqttcfg` fallback/validate; tách phần thuần khỏi NVS (khuôn `tls_ca.cpp`), thêm vào `build_src_filter` của `env:native` |
| T5.7 | FW native | Máy trạng thái WiFi: ngưỡng 5 phút, mở/đóng AP, không mở AP khi < 5 phút |
| T5.8 | FW native | Re-provision chỉ với state 4/5 + cooldown |
| T5.9 | E2E tay | Xem Phần 5 |

> Coverage gate BE là **≥ 80%** ([workflow.md](.claude/rules/workflow.md)). Nhớ chạy `make ci-build` **trước** `make ci-test` — `ci-test` dùng `--no-build` và sẽ đo bản cũ.

---

## Ước lượng

| Nhóm | Giờ |
|---|---|
| 0 — Chặn | 3h |
| 1 — Backend | 6h |
| 2 — FW cấu hình runtime | 14.5h |
| 3 — FW WiFi hiện trường ⭐ | 13h |
| 4 — batteryMappings (tuỳ chọn) | 3h |
| 5 — Test | 8h |
| **Tổng** | **~44–47h ≈ 6 ngày công** |

Nhánh Backend (1) và Firmware (2+3) **chạy song song được** sau khi T1.2 chốt shape DTO.

## Thứ tự

```
T0.1 ─┐
T0.2 ─┴──► [MỐC 1: MQTT chạy được khi cấu hình tay — demo được ở đây]
             │
   ┌─────────┴─────────┐
   ▼                   ▼
T1.1→…→T1.8        T2.1→…→T2.9  ──► [MỐC 2: zero-touch trừ WiFi]
   │                   │
   └─────────┬─────────┘
             ▼
         T3.1→…→T3.5  ──► [MỐC 3: zero-touch đầy đủ]
             │
         T4.1 (tuỳ chọn)
             │
         T5.* → T5.9 E2E
```

**Ba mốc đều demo được.** Hết thời gian ở giữa thì dừng ở mốc gần nhất, hệ thống vẫn nguyên vẹn.

---

# PHẦN 5 — Kiểm chứng (T5.9)

```bash
# Hạ tầng
./infra/mqtt/mosquitto/bootstrap.sh          # 1 lần, copy password → .env.Docker
# đặt Mqtt__Enabled=true + Mqtt__PasswordFilePath
docker compose --profile mqtt up -d mosquitto batteryservice

# Sync có chạy không
docker logs solar-batteryservice 2>&1 | grep MqttPasswordFileSync
#   "started (file=…)" = OK   |   "tắt" = T0.2 chưa xong
docker logs solar-batteryservice 2>&1 | grep "MQTT bridge connected"
docker exec solar-mosquitto cat /mosquitto/config/passwd

# Nghe toàn tree
docker exec solar-mosquitto mosquitto_sub -h 127.0.0.1 -u backend-bridge -P "$PW" -t 'solar/#' -v
```

## Bảy kịch bản bắt buộc chạy

| # | Kịch bản | Kỳ vọng |
|---|---|---|
| 1 | Thiết bị mới, chưa cấu hình | LED tím, phát `SolarGW-Setup`, trang tự mở |
| 2 | Cấu hình đủ 4 giá trị → Lưu | ~15s sau có row trong `sensor_readings` |
| 3 | **Reboot 2 LẦN** | Lần 2 vẫn nối MQTT (bắt lỗi T2.8c) |
| 4 | Rút WiFi 2 phút | Không mở AP; nối lại tự động; đẩy bù đủ, không trùng |
| 5 | Đổi mật khẩu WiFi router | 5 phút sau mở AP; cấu hình lại qua điện thoại; đẩy bù đủ |
| 6 | Tắt mosquitto | Rơi về HTTPS sau 3 fail; số liệu **vẫn vào DB đủ** |
| 7 | Rotate MQTT credential | Sau ≤5 lần fail tự re-provision, nối lại |

## ⚠️ Phép thử quyết định — đừng bỏ

Sau khi serial báo `[mqtt-ingest] pub … → OK`, kiểm log backend **không** có `MQTT telemetry from unknown device`, và bảng `sensor_readings` **thật sự** có row mới.

Publish là **QoS 0**; `ok` chỉ nghĩa là "đã đẩy vào socket TCP", **không** phải "broker đã nhận" ([mqtt_client.cpp:307-311](../iot/firmware-esp32/src/net/mqtt_client.cpp#L307-L311)). Đây chính xác là cách lỗi T0.1 ẩn mình bấy lâu — mọi tầng đều báo thành công trong khi dữ liệu rơi.

## Bảng chẩn đoán cho vận hành

| Triệu chứng | Nguyên nhân khả dĩ |
|---|---|
| `[mqtt] connect FAIL state=4` | passwd chưa sync — kiểm `MqttPasswordFileSync` + `Mqtt__PasswordFilePath` |
| `state=5 (UNAUTHORIZED)` | Auth OK, ACL từ chối — topic prefix lệch với `MqttUsername` |
| `pub → OK` nhưng DB không có row | Bridge tra không ra device — T0.1 chưa xong hoặc thiếu `MqttUsername` |
| Boot lần 2 im lặng chạy HTTPS-only | Quên T2.8c |
| `[http] begin() failed` / không request nào | CA không nạp được — cả HTTPS lẫn MQTT chết cùng lúc |
| `[provision] FAIL: HTTP 422` | Clock skew > 5 phút — NTP chưa sync hoặc DNS chặn |
| Provision 200 nhưng thiếu field mqtt | `Mqtt__Enabled=false` hoặc `Resolve()` trả `Disabled` |
| Mosquitto `unhealthy` | `Mqtt__Password` trong `.env.Docker` không khớp `passwd` |
| passwd đổi mà broker không nhận | Vòng `passwd-watch` chết, hoặc quyền file |
| `[wifi] reconnecting...` lặp vô hạn | Sai mật khẩu, mạng 5GHz-only, hoặc **WPA2-Enterprise** |

---

# PHẦN 6 — Việc ngoài code (đừng bỏ qua)

| # | Việc | Vì sao |
|---|---|---|
| 1 | **Điều khoản hợp đồng** về việc thiết bị dùng mạng của khách | Vấn đề pháp lý/riêng tư. Nêu rõ: chỉ gửi số liệu pin, không truy cập thiết bị khác trong mạng |
| 2 | **Nhãn dán** in `deviceCode` + QR + tên/mật khẩu AP setup | Không có nhãn thì phải tra web mỗi lần |
| 3 | **Hướng dẫn A4** dán trong nắp tủ | Để khách tự cấu hình lại khi đổi WiFi — chỗ tiết kiệm chi phí lớn nhất |
| 4 | **Quy trình lắp đặt** cho KTV (checklist 9 bước ở mục 3.2) | Chuẩn hoá, tránh sót bước |
| 5 | **Kịch bản hỗ trợ** cho Staff: alert `DeviceOffline` → gọi khách hỏi "có đổi WiFi không" | Đây sẽ là loại ticket phổ biến nhất |
| 6 | Ghi vào **báo cáo KLTN**: chọn WiFi khách + đánh đổi + captive portal là cơ chế phục hồi | Hội đồng chắc chắn sẽ hỏi |

---

# PHẦN 7 — Rủi ro còn lại

| Rủi ro | Mức | Xử lý |
|---|---|---|
| WiFi khách là **WPA2-Enterprise** (mạng trường/công ty) | **Cao** | `WiFi.begin(ssid,pass)` không hỗ trợ. Cần API `esp_wpa2_*`. **Kiểm tra trước khi demo** |
| WiFi 5GHz-only hoặc mesh chung SSID | Trung bình | ESP32-S3 chỉ bắt 2.4GHz. Trang setup nên lọc và cảnh báo (T3.3j) |
| Captive portal có nơi không tự bật | Trung bình | In sẵn `http://192.168.4.1` trên nhãn để gõ tay |
| Sóng WiFi không tới tủ pin | Trung bình | Trang setup hiển thị RSSI (T3.3i); < −75 dBm cảnh báo, đề xuất repeater |
| Khách đổi WiFi khi không ai ở đó | Cao (đã có cách xử lý) | AP fallback + hướng dẫn A4 |
| Hàng đợi LittleFS đầy khi offline dài ngày | Thấp | Đo dung lượng thực; cân nhắc nới `pollingInterval` khi offline |
| Backend chết trong lúc device publish MQTT | Trung bình | QoS 0 + không retain ⇒ phần telemetry đó **mất thật**. Chỉ phần qua queue HTTPS được cứu |

> **Rủi ro số 1 nên kiểm tra ngay** — 10 phút. Nếu mạng định demo là Enterprise, mọi việc trong tài liệu này đều không chạy, và triệu chứng duy nhất là `[wifi] reconnecting...` lặp vô hạn, không gợi ý gì về nguyên nhân.

---

# Quyết định đã chốt (2026-08-07)

Sáu câu hỏi treo của bản trước nay đã có đáp án. Bảng đầy đủ 10 quyết định: xem
[iot-co-che-hoat-dong.md § Quyết định đã chốt](iot-co-che-hoat-dong.md#quyết-định-đã-chốt-2026-08-07).

| # | Câu hỏi | Quyết định | Ảnh hưởng task |
|---|---|---|---|
| Q1 | TLS của MQTT compile-time hay runtime? (T2.5d) | **Compile-time** — `MQTT_USE_TLS=1` cố định, runtime chỉ mang host/port/prefix/user/pass | T2.3, T2.5 |
| Q2 | Ngưỡng re-provision? (T2.7) | **5 lần** fail liên tiếp state 4/5, **cooldown 15 phút** | T2.7 |
| Q3 | Gộp gán MQTT vào `ToCreatedDto()`? (T1.6d) | **Có** | T1.6 |
| Q4 | Tách issue thế nào? | **3 issue**: `T0.*` (fix) · `T1.*` (feat BE) · `T2.*`+`T3.*` (feat FW, repo iot) | — |
| Q5 | Làm T4.1 (`batteryMappings` runtime)? | **Có** — chuyển từ ⚪ tuỳ chọn sang 🟢 trong phạm vi | T4.1 |
| Q6 | Tách `rotate-mqtt` khỏi `rotate-key`? | **Có** | T1.6 + endpoint mới |

## Quyết định phần cứng (mới, ảnh hưởng repo `iot`)

| # | Nội dung |
|---|---|
| Q7 | BMS là **JK-BD6A24S10P** — 100 A liên tục / **200 A đỉnh** / 8–24S |
| Q8 | Shunt INA226: **200A/75mV = 0,375 mΩ**, `INA226_MAX_CURRENT_A = 200.0f` |
| Q9 | Cần **`-DINA226_MINIMAL_SHUNT_OHM=0.0001`** (thư viện chặn shunt < 1 mΩ) |
| Q10 | Cần **`normalize = false`** ở `setMaxCurrentShunt` (khối làm tròn chặn ở 163,8 A) |

Chi tiết tính toán + cách kiểm chứng: [iot-co-che-hoat-dong.md § LỖI 2](iot-co-che-hoat-dong.md#lỗi-2--cấu-hình-ina226-sai-về-mặt-vật-lý-chip-không-khởi-tạo-được).

---

## Phụ lục — Ghi chú nhanh về Calibration

Calibration **không** nằm trong plan này vì nó đã hoạt động đầy đủ và **hoàn toàn ở backend** — firmware không có một dòng calibration nào (đúng thiết kế).

**Công thức:** `giá_trị_thật = giá_trị_đo × Scale + Offset`

**Luồng:** đo đối chứng bằng đồng hồ chuẩn tại 2 điểm → tính Scale/Offset → `POST /api/iot-devices/{deviceId}/calibrations` → backend áp lúc nhận số liệu ([BatchIngestSensorReadingsCommandHandler.cs:282-284](services/BatteryService/src/BatteryService.Application/CQRS/Handler/SensorReading/BatchIngestSensorReadingsCommandHandler.cs#L282-L284)).

**Ví dụ tính:**

| Điểm | Đo được (raw) | Chuẩn (thật) |
|---|---|---|
| A | 12.45 V | 12.60 V |
| B | 11.20 V | 11.34 V |

```
Scale  = (12.60 − 11.34) / (12.45 − 11.20) = 1.008
Offset = 12.60 − (12.45 × 1.008)           = 0.0504
Kiểm B: 11.20 × 1.008 + 0.0504 = 11.3400 ✓
```

**Quy tắc tra cứu** (2 tầng, ưu tiên cụ thể hơn):
1. `(channel, batteryAssetId)` — riêng cho pin này
2. `(channel, null)` — chung cho cả thiết bị
3. Không có → giữ nguyên số thô

Cache Redis **TTL 5 phút** — sửa trên web thì tối đa 5 phút sau mới có hiệu lực.

**Khi nào cần calibrate:**

| Nguồn | Cần? |
|---|---|
| BMS qua RS485 (`primary`) | Thường không — đã hiệu chuẩn tại nhà máy |
| INA226 (`redundant`) | **Có** — phụ thuộc điện trở shunt thực tế |
| DS18B20 (`external-temp`) | **Có** — ±0.5°C datasheet |
| SHT31 ambient | Nên, nếu cần chính xác cao |
| MQ-2, rò nước | Không — nhị phân có/không |

Với `USE_MOCK_BMS=1` thì calibration **vô nghĩa** — số giả không có gì để đối chứng.
