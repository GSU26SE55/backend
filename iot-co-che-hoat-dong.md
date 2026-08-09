# IoT ↔ Backend — Cơ chế kết nối, quy trình setup, phân vai, và các vấn đề đã phát hiện

> **Cập nhật:** 2026-08-07 (bản 2 — sau khi rà lại repo `iot` @ `bea80a9`)
> **Mục đích:** Giải thích toàn cảnh cách ESP32 gateway và backend nói chuyện với nhau, ai làm gì, setup ra sao, và những lỗi/thiếu sót đã tìm thấy khi rà soát code.
> **Đọc cùng:** [iot-zero-touch-wifi-khach.md](iot-zero-touch-wifi-khach.md) — kế hoạch triển khai zero-touch (task list T0–T5, ước lượng, rủi ro).
>
> ⚠️ **Phần 1–7** mô tả trạng thái **sau khi hoàn thành Phương án A**. **Phần 8–16** là kết quả rà soát code thực tế, có cả **lỗi đang sống** cần sửa ngay.
>
> Mọi khẳng định trong tài liệu này đều đã đối chiếu trực tiếp với mã nguồn của 4 repo (`backend`, `frontend`, `mobile`, `iot`). Chỗ nào chưa kiểm chứng được trên phần cứng đều ghi rõ.

---

## Nhật ký rà soát — bản 2 (repo `iot` @ `bea80a9`)

Bản 1 viết trước khi pull. Sau khi pull `dev` (4 commit mới), các mục sau **đã thay đổi**:

| Mục | Bản 1 | Bản 2 |
|---|---|---|
| Dòng điện tràn số ở 32,7 A | 🔴 Lỗi nghiêm trọng | ✅ **ĐÃ SỬA** — commit `39a4b2f`, có test capture phần cứng thật |
| Bản đồ thanh ghi JK chưa kiểm chứng | ⚠️ Cảnh báo | ✅ **ĐÃ KIỂM CHỨNG** — `test_jk_realtime_values_match_hardware_capture` |
| Điện áp hệ 24V hay 48V? | ❓ Câu hỏi treo | ✅ **CHỐT 8S 24V** — INA226 an toàn |
| CA cert nhúng vào firmware | ❌ Chưa có | ⚠️ **XONG 50%** — MQTT có, HTTPS chưa |
| ACL case mismatch | 🔴 Bug đang sống | 🔴 **Vẫn sống** + đã có **workaround vá tay** cho 1 thiết bị |
| `BMS_UNIT_ID_COUNT` | (chưa phát hiện) | 🔴 **LỖI MỚI** — lệch với `battery_mapping.h`, chu kỳ đội lên 13× |
| `BMS_POLL_TIMEOUT_MS` | Tưởng là 500 ms | 🔴 **LỖI MỚI** — hằng chết, timeout thật là **2000 ms** |
| Đường HTTPS | Bình thường | 🔴 **RỦI RO MỚI** — có thể đang chết hoàn toàn |
| Sàn thời gian chu kỳ | ~1,03 s | 🔴 **~13,2 s** với cấu hình hiện tại |
| Thông số BMS | ❓ chưa biết | ✅ **JK-BD6A24S10P — 100 A liên tục / 200 A đỉnh / 8–24S** (đọc từ nhãn) |
| Cấu hình INA226 | 🔴 sai shunt | 🔴 **sai shunt + 2 ràng buộc thư viện** mới phát hiện |

Chi tiết ở [Phần 13](#13--bốn-lỗi-cấu-hình-phần-cứng-phải-sửa-trước-khi-cắm-bms-thật), [Phần 14](#14--chu-kỳ-gửi-số-liệu--có-giảm-dưới-10-giây-được-không), [Phần 15](#15--rủi-ro-đường-https-có-thể-đang-chết-hoàn-toàn).

---

## Quyết định đã chốt (2026-08-07)

Mười câu hỏi treo trong hai tài liệu nay đã có đáp án. **Không cần hỏi lại** — mọi phần bên dưới đã viết theo các quyết định này.

| # | Câu hỏi | Quyết định | Kéo theo |
|---|---|---|---|
| **Q1** | Dòng tối đa của hệ? | **100 A liên tục / 200 A đỉnh** — JK-BD6A24S10P (đọc từ nhãn) | Shunt **200A/75mV = 0,375 mΩ**; `INA226_MAX_CURRENT_A = 200.0f`; cần build flag + `normalize=false` — xem [LỖI 2](#lỗi-2--cấu-hình-ina226-sai-về-mặt-vật-lý-chip-không-khởi-tạo-được) |
| **Q2** | TLS của MQTT compile-time hay runtime? | **Compile-time** — giữ `MQTT_USE_TLS=1` cố định | `mqttcfg` chỉ mang host/port/prefix/user/pass; không phải xử lý chuyện `WiFiClientSecure` vs `WiFiClient` là hai kiểu khác nhau |
| **Q3** | Ngưỡng tự re-provision? | **5 lần** fail liên tiếp với `state()` = 4 hoặc 5, **cooldown 15 phút** | Chỉ lỗi credential/ACL mới kích hoạt; lỗi mạng (`-2`/`-4`) thì không |
| **Q4** | Gộp phần gán MQTT vào `ToCreatedDto()`? | **Có** | Chính việc không gộp đã gây bug rotate-key trả DTO toàn null |
| **Q5** | Làm `batteryMappings` runtime (T4.1)? | **Có** | Kéo danh sách pin từ Tầng 1 xuống Tầng 3 — đỡ phải ra hiện trường lần nữa khi khách lắp thêm pin |
| **Q6** | Tách `rotate-mqtt` khỏi `rotate-key`? | **Có** | `rotate-key` làm thiết bị mất **cả hai** đường và **không tự lành**; `rotate-mqtt` thì tự lành qua re-provision |
| **Q7** | Staff xem mọi thiết bị hay chỉ site được gán? | **Mọi thiết bị — không lọc** | Nhất quán tiền lệ spec §34.10.6 *"Staff xem được mọi asset là CỐ Ý"* |
| **Q8** | Lưu `MqttPasswordPlaintext` hay dùng nút xoay khoá? | **Lưu plaintext** | Cùng khuôn `ApiKeyPlaintext` đã chốt 2026-07-16 — không mở ra loại phơi nhiễm mới |
| **Q9** | Chu kỳ đo đặt bao nhiêu? | **5 giây** | Sàn sau khi sửa `BMS_UNIT_ID_COUNT=1` là ~1,0 s → 5 s có biên an toàn khi lắp thêm pin. Kèm nén + retention |
| **Q10** | Xử lý `BMS_POLL_TIMEOUT_MS` thế nào? | **Xoá hằng + ghi comment** | Patch thư viện `ModbusMaster` là gánh nặng bảo trì; sau khi sửa `COUNT=1` thì timeout gần như không còn ảnh hưởng |

---

## Mục lục

**Bối cảnh**
0. [Quyết định đã chốt (10 quyết định)](#quyết-định-đã-chốt-2026-08-07)

**Phần khái niệm**
1. [Bốn thành phần và ai nói chuyện với ai](#1--bốn-thành-phần-và-ai-nói-chuyện-với-ai)
2. [Hai kênh liên lạc — và vì sao phải có hai](#2--hai-kênh-liên-lạc--và-vì-sao-phải-có-hai)
3. [Chuỗi chìa khoá — cơ chế cốt lõi](#3--chuỗi-chìa-khoá--cơ-chế-cốt-lõi)
4. [Ai đóng vai trò gì](#4--ai-đóng-vai-trò-gì)
5. [Setup — năm giai đoạn](#5--setup--năm-giai-đoạn)
6. [Chạy thường ngày](#6--chạy-thường-ngày--không-ai-chạm-vào)
7. [Bảng tra nhanh](#7--bảng-tra-nhanh)

**Phần rà soát + giải pháp**

8. [Topic MQTT do ai tạo, ở đâu](#8--topic-mqtt-do-ai-tạo-ở-đâu)
9. [Đưa MQTT lên production](#9--đưa-mqtt-lên-production)
10. [Hiển thị QR trên UI thay vì in giấy](#10--hiển-thị-qr-trên-ui-thay-vì-in-giấy)
11. [Trang cấu hình thiết bị — đã có chưa](#11--trang-cấu-hình-thiết-bị--đã-có-chưa)
12. [Staff theo dõi thiết bị IoT](#12--staff-theo-dõi-thiết-bị-iot)
13. [⚠️ Bốn lỗi cấu hình phần cứng](#13--bốn-lỗi-cấu-hình-phần-cứng-phải-sửa-trước-khi-cắm-bms-thật)
14. [Chu kỳ gửi số liệu — có giảm dưới 10 giây được không](#14--chu-kỳ-gửi-số-liệu--có-giảm-dưới-10-giây-được-không)
15. [⚠️ Rủi ro: đường HTTPS có thể đang chết](#15--rủi-ro-đường-https-có-thể-đang-chết-hoàn-toàn)
16. [Tổng hợp việc phát sinh](#16--tổng-hợp-việc-phát-sinh)

[Ba câu tóm gọn nhất](#ba-câu-tóm-gọn-nhất) · [Phần nào đã có, phần nào chưa](#phần-nào-đã-có-phần-nào-chưa) · [Phụ lục: Calibration](#phụ-lục--calibration)

---

# 1 — Bốn thành phần và ai nói chuyện với ai

```
   NHÀ KHÁCH HÀNG                    │        MÁY CHỦ CỦA BẠN
                                     │
  ┌──────────────────────────┐       │
  │ Tủ pin                   │       │
  │  [Pin1][Pin2][Pin3][Pin4]│       │
  │     │    │    │    │     │       │
  │     └────┴────┴────┘     │       │
  │        RS485 (dây)       │       │
  │           │              │       │
  │      ┌─────────┐         │       │
  │      │ ESP32   │         │       │
  │      │ Gateway │         │       │
  │      └────┬────┘         │       │
  └───────────┼──────────────┘       │
              │ WiFi khách           │
              ▼                      │
        [Router khách]               │
              │                      │
              │   Internet           │
              └──────────────────────┼──────┐
                                     │      │
                                     │      ▼
                                     │  ┌────────────────────────┐
                                     │  │ Mosquitto (MQTT broker)│
                                     │  │  cổng 1883/8883        │
                                     │  └───────────┬────────────┘
                                     │              │
                                     │  ┌───────────▼────────────┐
                                     │  │ BatteryService         │
                                     │  │  · REST API (HTTPS)    │
                                     │  │  · MQTT Bridge         │
                                     │  └───────────┬────────────┘
                                     │              │
                                     │  ┌───────────▼────────────┐
                                     │  │ PostgreSQL/TimescaleDB │
                                     │  └────────────────────────┘
```

| Thành phần | Là gì | Đặt ở đâu |
|---|---|---|
| **BMS** | Mạch quản lý pin, đo điện áp/dòng/nhiệt của từng pin | Trong tủ pin, gắn sẵn theo pin |
| **ESP32 Gateway** | Máy tính nhỏ, gom số liệu từ BMS rồi gửi lên mạng | Trong tủ pin |
| **Mosquitto** | **Bưu điện trung chuyển** tin nhắn MQTT | Máy chủ, container Docker |
| **BatteryService** | Nhận, kiểm tra, lưu số liệu; phục vụ web/app | Máy chủ, container Docker |

> **Điểm cần hiểu rõ:** Mosquitto và BatteryService là **hai tiến trình riêng biệt**. ESP32 không nói chuyện trực tiếp với BatteryService qua MQTT — nó gửi tin nhắn cho Mosquitto, rồi BatteryService cũng nối vào Mosquitto để **nghe** những tin nhắn đó.
>
> Giống hai người cùng gửi/nhận thư qua một bưu điện, không đưa tận tay nhau.

---

# 2 — Hai kênh liên lạc — và vì sao phải có hai

## Kênh 1 — HTTPS (REST API)

Giống trình duyệt gọi web. ESP32 mở kết nối, gửi yêu cầu, **chờ câu trả lời**, đóng kết nối.

```
ESP32  ──── POST /api/iot-devices/provision ────►  BatteryService
       ◄─── 200 {config, MQTT credential} ───────
```

**Đặc điểm:** có hỏi có đáp, chắc chắn đến nơi, nhưng nặng (mỗi lần phải bắt tay TLS lại).

**Dùng cho:** provision, heartbeat, OTA, sự cố môi trường (khói/rò nước), đẩy bù hàng đợi, và **dự phòng khi MQTT hỏng**.

## Kênh 2 — MQTT

Giống nhóm chat. ESP32 nối vào Mosquitto **một lần rồi giữ mãi**, sau đó "đăng bài" vào các "chủ đề" (topic). Ai quan tâm chủ đề đó thì tự nhận được.

```
ESP32  ──publish──►  [Mosquitto]  ──deliver──►  BatteryService (đang subscribe)
       topic: solar/gw-esp32-001/BAT-2026-001/telemetry
```

**Đặc điểm:** nhẹ, nhanh, giữ kết nối sẵn nên gửi liên tục rất rẻ. Nhưng **không chờ câu trả lời** — gửi xong là xong.

**Dùng cho:** telemetry (số liệu pin), trạng thái online/offline, lệnh điều khiển từ backend xuống.

## Vì sao không dùng một kênh?

| | Chỉ HTTPS | Chỉ MQTT |
|---|---|---|
| Telemetry vài giây/lần | Bắt tay TLS hàng trăm lần/giờ — tốn pin, tốn CPU, tốn băng thông | ✅ |
| Backend gửi lệnh **xuống** thiết bị | ❌ Không được. Thiết bị nằm sau router khách, không có IP công khai — backend không "gọi vào" được | ✅ Thiết bị đã nối sẵn, lệnh đi ngược đường đó |
| Biết thiết bị chết ngay lập tức | ❌ Phải chờ hết hạn heartbeat (5 phút) | ✅ LWT — broker phát hộ "tôi chết rồi" sau ~30 giây |
| Cần chắc chắn nhận được câu trả lời | ✅ | ❌ |
| Đẩy bù dữ liệu tích luỹ, chống ghi trùng | ✅ Có `Idempotency-Key` | ❌ |

⇒ **Mỗi kênh làm việc nó giỏi.** MQTT lo luồng số liệu liên tục + điều khiển; HTTPS lo những việc cần chắc chắn.

## Các topic MQTT

```
solar/gw-esp32-001/BAT-2026-001/telemetry   ESP32 ──► backend   số liệu 1 pin
solar/gw-esp32-001/heartbeat                ESP32 ──► backend   (hiện đi HTTPS)
solar/gw-esp32-001/status                   ESP32 ──► backend   "online"/"offline"
solar/gw-esp32-001/cmd                      backend ──► ESP32   lệnh điều khiển
solar/gw-esp32-001/cmd/ack                  ESP32 ──► backend   báo đã làm xong
```

Backend nghe 4 wildcard: `solar/+/+/telemetry`, `solar/+/heartbeat`, `solar/+/status`, `solar/+/cmd/ack`.

Chi tiết topic được tạo ra như thế nào: **[xem Phần 8](#8--topic-mqtt-do-ai-tạo-ở-đâu)**.

## LWT — Last Will and Testament

Lúc nối vào, ESP32 dặn Mosquitto:

> *"Nếu tôi mất tích đột ngột, hãy đăng hộ tôi chữ `offline` lên topic `solar/gw-esp32-001/status`."*

Mất điện, đứt mạng, cháy — khoảng 30 giây sau (1,5 × keep-alive) Mosquitto tự đăng. Backend nhận được → đánh dấu thiết bị `Offline` → tạo `Alert(DeviceOffline)` cho từng pin trong site → đẩy event sang NotificationService báo Staff.

**Thiết bị báo tin cái chết của chính nó mà không cần còn sống.**

> ⚠️ Publish telemetry là **QoS 0**. Hàm `publishWithStats` trả `true` nghĩa là *"đã đẩy được vào socket TCP"*, **không** phải *"broker đã nhận"*. Tham số `qos` giả trước đây đã bị bỏ (GH-746), có test `test_mqtt_qos_contract` chặn nó quay lại.

---

# 3 — Chuỗi chìa khoá — cơ chế cốt lõi

Thiết bị phải **chứng minh mình là ai** ở cả hai kênh, bằng **hai loại chìa khoá khác nhau**:

| Kênh | Chìa khoá | Ai kiểm |
|---|---|---|
| HTTPS | `X-Api-Key: iotk_…` + `X-Device-Code` | BatteryService (so hash trong DB) |
| MQTT | username + password | **Mosquitto** (so file `passwd`) |

> **Mosquitto không biết gì về database của bạn.** Nó chỉ đọc một file văn bản chứa danh sách user/mật khẩu. Đây là nguồn gốc của rất nhiều rắc rối.

## Chuỗi mở khoá

```
   ┌────────────────────────────────────────────────────────────┐
   │ Nạp tay lúc lắp:  deviceCode + apiKey                      │
   └───────────────────────────┬────────────────────────────────┘
                               │ mở khoá
                               ▼
   ┌────────────────────────────────────────────────────────────┐
   │ Kênh HTTPS  →  gọi được /provision                         │
   └───────────────────────────┬────────────────────────────────┘
                               │ backend TRẢ VỀ
                               ▼
   ┌────────────────────────────────────────────────────────────┐
   │ broker host/port + MQTT username/password + topic prefix   │
   └───────────────────────────┬────────────────────────────────┘
                               │ mở khoá
                               ▼
   ┌────────────────────────────────────────────────────────────┐
   │ Kênh MQTT  →  gửi telemetry, nhận lệnh                     │
   └────────────────────────────────────────────────────────────┘
```

**Chỉ cần nạp tay 1 cặp chìa khoá. Cặp thứ hai backend tự đưa.**

> ✅ Đó là toàn bộ ý nghĩa của **Phương án A**, và nó **đã xong** (IOT3-42). `/provision` trả về sáu trường MQTT, `applyMqttFromProvision` (`provision.cpp:49`) ghi thẳng vào NVS rồi `mqttApplyConfig()` nối lại — **không cần nạp lại firmware, không cần khởi động lại**.
>
> Giá trị `MQTT_USERNAME`/`MQTT_PASSWORD` trong [`config.h`](../iot/firmware-esp32/include/config.example.h#L120-L130) nay chỉ còn là **mặc định lúc biên dịch**: `mqttcfg::begin()` nạp chúng trước, rồi NVS ghi đè từng trường. Một bản build dùng chung cho mọi thiết bị.
>
> ⚠️ Và chuỗi này **phụ thuộc hoàn toàn vào HTTPS** — HTTPS chết là mắt xích thứ hai không bao giờ mở được. Xem [Phần 15](#15--rủi-ro-đường-https-có-thể-đang-chết-hoàn-toàn).

## Song song đó, backend phải dạy Mosquitto biết thiết bị

```
Admin tạo device
      │
      ├─► DB: lưu username + hash mật khẩu MQTT
      │
      └─► MqttPasswordFileSyncService ghi vào file passwd
                    │
                    ▼
          Mosquitto tự phát hiện file đổi (vòng lặp so mtime, 5 giây/lần)
                    │
                    ▼
              kill -HUP → nạp lại → giờ nó biết thiết bị này
```

> ⚠️ Thiếu bước này, thiết bị cầm đúng mật khẩu vẫn bị Mosquitto từ chối (`state=4 BAD_CREDENTIALS`) — **và đây là tình trạng hiện tại**, vì `Mqtt__PasswordFilePath` chưa được đặt ở đâu cả. Xem task **T0.2**.

## Và Mosquitto còn kiểm quyền theo topic (ACL)

```
pattern write solar/%u/+/telemetry     ← %u = username
pattern write solar/%u/heartbeat
pattern write solar/%u/status
pattern write solar/%u/cmd/ack
pattern read  solar/%u/cmd             ← chỉ ĐỌC, không cho ghi (chống giả mạo)

user backend-bridge
topic readwrite solar/#                ← cầu nối backend: toàn quyền
topic read $SYS/#
```

Thiết bị `gw-esp32-001` chỉ ghi được lên `solar/gw-esp32-001/...`. Nó **không thể** giả mạo số liệu của thiết bị khác — kể cả khi đã đăng nhập thành công.

---

# 4 — Ai đóng vai trò gì

## Con người trong hệ thống

| Vai trò | Trong hệ thống IoT, làm gì |
|---|---|
| **Admin** | Tạo hồ sơ thiết bị, cấp `deviceCode` + `apiKey`, xoay/thu hồi khoá, quản lý firmware release |
| **Manager** | Duyệt hồ sơ hiệu chuẩn, phân loại và gán mức ưu tiên cho ticket sinh ra từ cảnh báo |
| **Staff** | **Đi lắp đặt tại nhà khách**, xử lý cảnh báo `DeviceOffline`, đo hiệu chuẩn cảm biến |
| **Customer** | Chủ hệ pin. Xem số liệu trên app. Cung cấp mật khẩu WiFi lúc lắp |

## Con người ngoài hệ thống (đội phát triển)

| Vai trò | Làm gì |
|---|---|
| **BE Dev** | Dựng hạ tầng lần đầu (Mosquitto, bootstrap credential, cấu hình biến môi trường) |
| **FW Dev** | Build và nạp firmware vào phần cứng |

## Máy móc đóng vai trò gì

| Thành phần | Vai trò |
|---|---|
| **Mosquitto** | Bưu điện. Kiểm chìa khoá MQTT, chuyển tin nhắn, giữ LWT |
| **MqttBridgeBackgroundService** | Cái tai của backend. Nối vào Mosquitto bằng `backend-bridge`, nghe 4 topic, chuyển thành MediatR command |
| **MqttPasswordFileSyncService** | Người chép danh sách. Từ DB → file `passwd` |
| **ApiKeyAuthenticationHandler** | Người gác cổng HTTPS. Kiểm `X-Api-Key`, `X-Device-Code`, scope |
| **IotDeviceOfflineDetectionService** | Người điểm danh. 60 giây quét một lần, im lặng quá 5 phút → offline |
| **ThresholdCheckBackgroundService** | Người soi ngưỡng. Quét mỗi **30 giây** → sinh cảnh báo |
| **IotCalibrationCache** | Bộ nhớ đệm Redis cho hệ số hiệu chuẩn, TTL 5 phút |

---

# 5 — Setup — năm giai đoạn

## Giai đoạn 0 — Dựng hạ tầng (BE Dev, **một lần cho cả hệ thống**)

```bash
# 1. Sinh tài khoản cho cầu nối backend↔broker
./infra/mqtt/mosquitto/bootstrap.sh
#    → in ra mật khẩu, copy vào `.env` (KHÔNG phải .env.Docker — xem ghi chú dưới)

# 2. Bật MQTT   (task T0.2)
#    Sửa `.env`:
#      Mqtt__Enabled=true
#      Mqtt__Host=mosquitto        # `localhost` trong container = chính nó, không phải broker
#      Mqtt__Password=<mật khẩu vừa sinh>

# 3. Khởi động
docker compose --profile mqtt up -d --force-recreate mosquitto batteryservice
```

> ⚠️ **Sửa `.env`, KHÔNG phải `.env.Docker`.** Đây là chỗ mất nhiều thời gian nhất.
>
> `docker-compose.yml` khai batteryservice với **cả hai**: `env_file: .env.Docker` **và**
> `environment: Mqtt__Host: ${Mqtt__Host:-mosquitto}`. Theo đặc tả Compose, `environment:`
> **thắng** `env_file:`; còn `${Mqtt__Host}` thì Compose nội suy từ **`.env`** (file mặc định),
> không phải từ `.env.Docker`. Kết quả: sửa `.env.Docker` cho các khoá `Mqtt__*` là **không có
> tác dụng gì** — giá trị trong `.env` mới là thứ vào được container.
>
> Kiểm tra bằng chính Compose thay vì đoán:
> ```bash
> # Cắt đúng khối service rồi mới lọc — KHÔNG dùng `grep -A20`: `environment:` sắp theo bảng
> # chữ cái nên `Mqtt__` nằm sau hàng chục dòng ASPNETCORE_/ConnectionStrings__/Jwt__.
> docker compose config | awk '/^  batteryservice:$/{f=1} f&&/^  [a-z0-9_-]+:$/&&!/batteryservice/{f=0} f&&/Mqtt__/'
> ```
>
> Cũng cần `--force-recreate`: biến môi trường được cố định lúc **tạo** container, nên
> `up -d` với container đã tồn tại sẽ giữ nguyên giá trị cũ.

**Xác nhận đã xong:**

```bash
docker logs solar-batteryservice | grep "MQTT bridge connected"
docker logs solar-batteryservice | grep MqttPasswordFileSync   # phải "started", không phải "tắt"
```

> Production làm khác — **[xem Phần 9](#9--đưa-mqtt-lên-production)**.

## Giai đoạn 1 — Tạo hồ sơ thiết bị (Admin, mỗi thiết bị, trên web)

```
POST /api/admin/iot-devices
{ "deviceCode": "GW-ESP32-001", "siteId": "…", "displayName": "Nhà anh Nam - tủ 1" }
```

Backend làm 5 việc trong một nhịp:

| # | Việc |
|---|---|
| 1 | Lưu thiết bị, `Status = Pending` |
| 2 | Sinh `apiKey` (`iotk_…`, ~47 ký tự) → lưu hash + bản gốc |
| 3 | Sinh MQTT username (`gw-esp32-001`) + mật khẩu ngẫu nhiên |
| 4 | Ghi credential vào file `passwd` → Mosquitto nạp lại sau ≤5 giây |
| 5 | Trả về + sinh chuỗi QR `iot://provision?dc=…&key=…` |

> **Broker biết thiết bị trước cả khi nó tồn tại về mặt vật lý.** (`Pending` nằm trong `MqttPasswordFileSyncService.AllowedStatuses`.)

Admin in **nhãn dán** — **[cách hiển thị QR trên UI: xem Phần 10](#10--hiển-thị-qr-trên-ui-thay-vì-in-giấy)**.

## Giai đoạn 2 — Nạp firmware (FW Dev, tại xưởng)

```bash
pio run -t upload
```

**Một bản firmware duy nhất cho mọi thiết bị.** Trong firmware chỉ có thứ dùng chung: địa chỉ backend, chứng thư CA, mật khẩu trang setup.

## Giai đoạn 3 — Lắp đặt tại nhà khách (Staff, ~5 phút)

| Bước | Việc | Ai |
|---|---|---|
| 1 | Lắp tủ, đấu dây RS485 từ BMS về ESP32, cấp nguồn | Staff |
| 2 | LED **tím nháy** — thiết bị đang phát WiFi `SolarGW-A4C1` | — |
| 3 | Điện thoại nối vào WiFi đó (mật khẩu in trên nhãn) | Staff |
| 4 | Trang cấu hình **tự mở** (captive portal) | — |
| 5 | Chọn WiFi nhà khách, **hỏi khách mật khẩu**, gõ vào | Staff + Customer |
| 6 | Quét QR trên nhãn → `deviceCode` + `apiKey` tự điền | Staff |
| 7 | Bấm **Lưu** → thiết bị tự khởi động lại | — |
| 8 | LED **cam** → **xanh** trong ~15 giây | — |
| 9 | Mở web admin xác nhận: `Active`, đã có số liệu | Staff |

> ⚠️ Trang cấu hình này **chưa tồn tại** — **[xem Phần 11](#11--trang-cấu-hình-thiết-bị--đã-có-chưa)**.

### Chuyện gì xảy ra trong 15 giây đó

```
 0s  Khởi động lại, đọc 4 giá trị vừa lưu
 2s  Nối WiFi nhà khách → có địa chỉ IP
 5s  Đồng bộ đồng hồ với máy chủ thời gian (NTP)
 5s  POST /provision  (dùng apiKey)
     └─ backend: kiểm khoá → kiểm lệch giờ ≤5 phút → Status=Active
        └─ trả về: broker ở đâu, username/password MQTT, topic prefix,
                   chu kỳ đo, siteId, danh sách pin
 6s  Lưu vào bộ nhớ, kích hoạt MQTT
11s  Nối Mosquitto → dặn LWT → đăng "online" → đăng ký nhận lệnh
11s  Đọc BMS → gửi số liệu
11s  Backend nhận → lưu vào sensor_readings
```

## Giai đoạn 4 — Hiệu chuẩn (Staff + Manager, tuỳ chọn)

Chi tiết: **[Phụ lục Calibration](#phụ-lục--calibration)**. Với phần cứng cụ thể của bạn: **[Phần 13](#13--bốn-lỗi-cấu-hình-phần-cứng-phải-sửa-trước-khi-cắm-bms-thật)**.

---

# 6 — Chạy thường ngày — không ai chạm vào

## Nhịp đều đặn

| Việc | Bao lâu một lần | Đường nào |
|---|---|---|
| Số liệu pin | `pollingInterval` (hiện **cứng 10 giây** ở backend) | **MQTT** |
| Báo còn sống (heartbeat) | 60 giây | HTTPS |
| Nhiệt độ/độ ẩm môi trường | 60 giây | HTTPS |
| Cảm biến khói / rò nước | 1 giây / **0,1 giây** | HTTPS khi có sự cố |
| Kiểm tra firmware mới | 1 giờ | HTTPS |
| Đẩy bù hàng đợi | theo backoff | HTTPS (luôn) |

> ⚠️ Chu kỳ **thực tế** hiện đang bị chặn ở ~13 giây vì lỗi cấu hình — **[xem Phần 14](#14--chu-kỳ-gửi-số-liệu--có-giảm-dưới-10-giây-được-không)**.

## Khi có sự cố — hệ thống tự xử

| Tình huống | Hệ thống làm gì |
|---|---|
| **Mất mạng < 5 phút** | Vẫn đọc pin, ghi vào LittleFS. Có mạng lại thì đẩy bù đủ, không trùng nhờ `Idempotency-Key` |
| **Mất mạng > 5 phút** | Thêm: tự phát lại WiFi setup (`WIFI_AP_STA`). Backend tạo cảnh báo `DeviceOffline` |
| **Mosquitto chết** | Sau 3 lần gửi hỏng, tự chuyển sang HTTPS. **Số liệu vẫn vào đủ** |
| **Backend chết, broker sống** | MQTT vẫn nối được nhưng tin nhắn rơi (QoS 0). **Phần telemetry lúc đó mất thật** |
| **Khách đổi mật khẩu WiFi** | Staff gọi khách → cấu hình lại qua điện thoại |
| **Admin xoay khoá MQTT** | Thiết bị bị từ chối 5 lần → tự `/provision` lại → **tự lành** |
| **Admin xoay `apiKey`** | Mất **cả hai** đường. **Không tự lành** — phải nạp lại qua trang setup |
| **Mất điện** | Bật lại là chạy. Cấu hình nằm trong NVS |
| **Cập nhật firmware** | Tự tải, verify SHA-256, tự cài, hỏng thì tự quay về bản cũ. NVS sống sót qua OTA |

## Lệnh điều khiển từ xa

```
Admin bấm nút trên web
  → POST /api/admin/iot-devices/{id}/command
  → backend publish lên solar/gw-esp32-001/cmd  (QoS 1)
  → thiết bị nhận trong ~1 giây → thực thi
  → publish ack lên solar/gw-esp32-001/cmd/ack
```

Ba lệnh: `set_interval`, `request_heartbeat`, `trigger_ota`. Tên nào khác cũng vẫn được 202 nhưng
thiết bị trả `unknown` và không làm gì — xem §17.5.

> ⚠️ `set_interval` chỉ đổi RAM, **không ghi NVS** — reboot là quay về giá trị provision.
>
> ⚠️ Thiết bị **offline thì lệnh mất luôn**, không nằm chờ: nó nối broker ở chế độ phiên sạch nên
> broker không giữ lệnh hộ. Backend vẫn trả 202.

## Kiến trúc vòng lặp (cập nhật 2026-08-07)

Toàn bộ logic đã chuyển từ `loop()` của Arduino sang một **task FreeRTOS riêng**:

```cpp
void appTask(void* pv) { for (;;) { appLoopBody(); vTaskDelay(pdMS_TO_TICKS(10)); } }
void loop()            { vTaskDelay(pdMS_TO_TICKS(1000)); }
```

Việc này tách được watchdog của Arduino loop, nhưng **không** thay đổi sàn thời gian — các thao tác chặn (DS18B20, Modbus timeout) vẫn chặn chính task đó.

---

# 7 — Bảng tra nhanh

## Ai làm gì, bao lâu một lần

| Ai | Việc | Tần suất |
|---|---|---|
| BE Dev | Dựng Mosquitto + bật cấu hình | **1 lần** cho cả hệ thống |
| FW Dev | Build + nạp firmware | 1 lần/thiết bị (và khi có bản mới) |
| Admin | Tạo hồ sơ + in nhãn | 1 lần/thiết bị |
| Staff | Lắp đặt + cấu hình WiFi | 1 lần/thiết bị (+ khi khách đổi WiFi) |
| Staff | Hiệu chuẩn cảm biến | 1 lần/năm, tuỳ chọn |
| Customer | Cho mật khẩu WiFi | 1 lần lúc lắp |
| **Không ai** | Vận hành hằng ngày | — |

## Thông tin nằm ở đâu

| Thông tin | Nằm ở | Đổi bằng cách |
|---|---|---|
| Địa chỉ backend, chứng thư CA | Firmware | Build lại + OTA |
| WiFi nhà khách | NVS thiết bị | Trang setup qua điện thoại |
| `deviceCode`, `apiKey` | NVS thiết bị | Trang setup qua điện thoại |
| Broker, khoá MQTT, chu kỳ đo, danh sách pin | Backend cấp lúc chạy | **Sửa trên web** |
| Hệ số hiệu chuẩn | Chỉ ở backend | Sửa trên web |

## Trạng thái đèn LED

| Màu | Nghĩa |
|---|---|
| Tím nháy | Chưa cấu hình — đang phát WiFi setup |
| Cam | Đang tìm WiFi |
| Tím/cam xen kẽ | Mất mạng lâu — đã mở lại WiFi setup |
| Xanh | Bình thường |
| Xanh nháy | Bình thường, đang đẩy bù dữ liệu tồn |

---
---

# 8 — Topic MQTT do ai tạo, ở đâu

## Trả lời ngắn: **không ai tạo cả**

MQTT topic **không phải là tài nguyên**. Không có bảng trong DB, không có API `create-topic`, không có file khai báo danh sách. Nó chỉ là **một chuỗi ký tự** gắn vào mỗi tin nhắn.

Khi ESP32 publish lên `solar/gw-esp32-001/BAT-2026-001/telemetry`, Mosquitto không kiểm tra "topic này có tồn tại không" — nó chỉ so chuỗi đó với danh sách người đang đăng ký nghe rồi chuyển tiếp. Topic "sinh ra" lúc có tin nhắn đầu tiên và "biến mất" khi không còn ai dùng.

> Khác căn bản với RabbitMQ — nơi queue/exchange **phải** được khai báo trước.

## Nhưng "hợp đồng" về topic được khai ở **ba nơi**, phải khớp nhau bằng tay

| Nơi | File | Vai trò |
|---|---|---|
| **Backend** | [MqttTopicMap.cs](services/BatteryService/src/BatteryService.Infrastructure/Mqtt/MqttTopicMap.cs) | Định nghĩa chuẩn: 4 wildcard subscribe + 5 hàm dựng topic + `TryParse()` bóc `deviceCode` |
| **Firmware** | [mqtt_client.cpp](../iot/firmware-esp32/src/net/mqtt_client.cpp) — 6 chỗ (dòng 137, 185, 354, 361, 368, 375) | Dựng chuỗi bằng `snprintf("%s/%s/...", MQTT_TOPIC_PREFIX, identity::deviceCode(), ...)` |
| **Broker** | [acl.conf](../iot/infra/mqtt/mosquitto/config/acl.conf) | **Nơi DUY NHẤT có tính cưỡng chế** — `pattern write solar/%u/+/telemetry` |

## Chỉ ACL mới thực sự "có răng"

Hai nơi đầu chỉ là quy ước. Nếu firmware gõ sai `solar/gw-esp32-001/telemetri`:

- Firmware: publish **thành công** (không ai chặn)
- Backend: wildcard `solar/+/+/telemetry` không khớp → tin nhắn rơi
- **Không có lỗi nào ở đâu cả**

Còn ACL thì chặn thật: sai topic → broker trả `state=5 UNAUTHORIZED`.

## Đây chính là gốc của bug T0.1

Ba nơi trên **không có cơ chế nào đảm bảo khớp nhau**, và thực tế chúng đang lệch:

```
DB lưu:                    GW-ESP32-001              (ToUpperInvariant)
ACL cho phép ghi:          solar/gw-esp32-001/...    (%u = username chữ thường)
Backend publish lệnh:      solar/GW-ESP32-001/cmd    ← KHÔNG khớp ACL
Backend tra device:        WHERE DeviceCode = 'gw-esp32-001'  ← KHÔNG khớp DB
```

Hai đầu đều đứt, và **cả hai đều đứt trong im lặng**.

## 🔴 CẬP NHẬT 2026-08-07 — bug này **đã xảy ra ngoài thực tế** và bị vá tay

[acl.conf](../iot/infra/mqtt/mosquitto/config/acl.conf) vừa được thêm khối sau:

```conf
# ===== Luật riêng cho ESP-2 =====
# Cần vì DEVICE_CODE trong firmware là "ESP-2" IN HOA nên topic là
# solar/ESP-2/..., trong khi username Mosquitto là "esp-2" chữ thường.
# Pattern %u ở trên chỉ khớp chữ thường nên không phủ được, phải khai tay.
user esp-2
topic write solar/ESP-2/+/telemetry
topic write solar/ESP-2/heartbeat
topic write solar/ESP-2/status
topic write solar/ESP-2/cmd/ack
topic read  solar/ESP-2/cmd
```

Comment mô tả **chính xác** cơ chế lỗi. Nhưng đây là **vá triệu chứng, không sửa gốc**:

| Vấn đề | Hệ quả |
|---|---|
| Chỉ chạy cho **đúng một** thiết bị `ESP-2` | Thiết bị thứ hai lại chết, phải thêm 5 dòng nữa |
| `DEVICE_CODE` trong `config.example.h` là `gw-esp32-mvp-001` | Luật này **vô dụng với cấu hình mặc định** |
| Không đụng backend | Bridge vẫn tra `d.DeviceCode == deviceCode` — chỉ tình cờ khớp vì `ESP-2` viết hoa trùng DB |
| Trái với chính comment `config.example.h` dòng 53 | *"⚠ Sprint 4 CONVENTION: DEVICE_CODE PHẢI LOWERCASE"* |

⇒ **T0.1 vẫn phải làm.** Và sau khi làm xong thì **phải xoá khối `user esp-2`**, nếu không sẽ có hai luật chồng nhau gây khó debug.

## ✅ Giải pháp

**Ngắn hạn (T0.1, ~2h):** chuẩn hoá chữ thường ở ranh giới MQTT — chi tiết trong [iot-zero-touch-wifi-khach.md](iot-zero-touch-wifi-khach.md#t01--chuẩn-hoá-case-devicecode-ở-ranh-giới-mqtt-be-2h).

**Dài hạn (quyết định Đ2 của Phương án A):** backend trả thẳng `mqttTopicPrefix` qua `/provision`, firmware chỉ nối đuôi:

```cpp
// TRƯỚC — firmware tự ghép, tự đoán chữ hoa/thường  (HIỆN TẠI VẪN THẾ)
snprintf(topicBuf, len, "%s/%s/%s/telemetry",
         MQTT_TOPIC_PREFIX, identity::deviceCode(), batterySerial);

// SAU — backend đã nói rõ prefix, firmware chỉ nối đuôi
snprintf(topicBuf, len, "%s/%s/telemetry",
         mqttcfg::topicPrefix(), batterySerial);
//        └─ "solar/gw-esp32-001", do MqttBrokerEndpointProvider.TopicPrefixFor() sinh
```

Lúc đó chỉ còn **một nơi** quyết định chuỗi prefix. Không còn ba nơi tự suy rồi lệch.

---

# 9 — Đưa MQTT lên production

## `bootstrap.sh` làm gì

Nó **chỉ tạo một tài khoản duy nhất**: `backend-bridge` — tài khoản để BatteryService nối vào broker. **Không** liên quan tới credential của thiết bị (cái đó do `MqttPasswordFileSyncService` ghi tự động).

```bash
openssl rand -base64 32           → mật khẩu ngẫu nhiên
docker run eclipse-mosquitto mosquitto_passwd -b /c/passwd backend-bridge <pass>
chmod 0644 passwd
echo "Password: $PASSWORD"        → in ra màn hình
```

## Ba lý do không dùng thẳng ở production

| # | Vấn đề | Bằng chứng |
|---|---|---|
| 1 | `chmod 0644` — **cả máy đọc được** | [bootstrap.sh](../iot/infra/mqtt/mosquitto/bootstrap.sh) tự thừa nhận: *"Production: dùng named volume + init container hoặc proper uid mapping → set 0600"* |
| 2 | In mật khẩu ra **stdout** | Vào shell history, log CI, scrollback terminal |
| 3 | Cần `docker run` trên máy đích | Server prod có thể không cho chạy container ad-hoc |

## 🔴 Vấn đề lớn hơn: hạ tầng broker production **chưa tồn tại**

[docker-compose.prod.yml:416-434](docker-compose.prod.yml#L416-L434) ghi rõ quyết định kiến trúc:

> *"MQTT broker — **KHÔNG nằm trong backend repo**. Per architectural decision: broker thuộc `capstone/iot/` monorepo (cả dev + prod). […] `cd /opt/solar/iot && docker compose -f infra/docker-compose.prod.yml up -d mosquitto`"*

Nhưng (kiểm lại 2026-08-07, sau khi pull):

```bash
$ ls capstone/iot/infra/
README.md  db  docker-compose.dev.yml  env.example.txt  mqtt  setup.sh
```

**`docker-compose.prod.yml` VẪN KHÔNG TỒN TẠI.** Tài liệu prod trỏ tới một file chưa ai viết.

Helm cũng vậy — [values.yaml:439-450](deploy/helm/solar-battery/values.yaml) có đủ 8 biến `Mqtt__*` nhưng `Mqtt__Enabled: "false"`, và **không có chart nào deploy Mosquitto**.

⇒ **Hiện không có đường nào đưa MQTT lên production**, dù dev đã chạy được.

## ✅ Giải pháp

### Bước 1 — Sinh credential **ngoài** server (máy dev hoặc CI)

```bash
# Sinh mật khẩu, KHÔNG in ra màn hình
MQTT_PW=$(openssl rand -base64 32 | tr -d '/+=' | head -c 32)

# Sinh hash
HASH_LINE=$(docker run --rm -i eclipse-mosquitto:2.0 \
  sh -c "mosquitto_passwd -c -b /tmp/p backend-bridge '$MQTT_PW' >/dev/null && cat /tmp/p")

# Ghi thẳng vào kho bí mật, không qua file trung gian
echo "Mqtt__Password=$MQTT_PW" >> /opt/solar/.env.prod
chmod 600 /opt/solar/.env.prod
unset MQTT_PW
```

### Bước 2 — Nơi lưu mật khẩu

| Môi trường | Nơi lưu |
|---|---|
| Docker Compose VPS | `/opt/solar/.env.prod`, `chmod 600`, chủ sở hữu = user chạy compose |
| Kubernetes | `Secret solar-secrets` (xem `deploy/README.md`) |

### Bước 3 — ⭐ Viết `iot/infra/docker-compose.prod.yml` (**file đang thiếu**)

Khác bản dev ở 6 điểm:

```yaml
# capstone/iot/infra/docker-compose.prod.yml
services:
  mosquitto:
    image: eclipse-mosquitto:2.0
    container_name: solar-mosquitto
    restart: unless-stopped

    # (1) UID khớp với container backend để đọc được file passwd backend ghi ra.
    #     Xem Bước 4 — đây là bẫy chí mạng.
    user: "1883:1883"

    ports:
      - "8883:8883"          # (2) CHỈ TLS. KHÔNG publish 1883 ra ngoài.

    volumes:
      - ./mqtt/mosquitto/config/mosquitto.conf:/mosquitto/config/mosquitto.conf:ro
      - ./mqtt/mosquitto/config/acl.conf:/mosquitto/config/acl.conf:ro
      - ./mqtt/mosquitto/config/conf.d:/mosquitto/config/conf.d:ro    # (3) tls.conf do gen-certs.sh sinh
      - ./mqtt/mosquitto/passwd:/mosquitto/config/passwd              # (4) KHÔNG :ro — backend phải ghi
      - ./mqtt/mosquitto/certs:/mosquitto/certs:ro
      - mosquitto-data:/mosquitto/data
      - mosquitto-log:/mosquitto/log

    # (5) Tự nạp lại khi backend ghi passwd — copy từ backend/docker-compose.yml:165-180
    entrypoint:
      - /bin/sh
      - -c
      - |
        (
          last=""
          while :; do
            cur=$$(stat -c %Y /mosquitto/config/passwd 2>/dev/null || echo none)
            if [ -n "$$last" ] && [ "$$cur" != "$$last" ]; then
              echo "[passwd-watch] file doi -> SIGHUP mosquitto"
              kill -HUP 1 2>/dev/null || true
            fi
            last="$$cur"
            sleep 5
          done
        ) &
        exec /usr/sbin/mosquitto -c /mosquitto/config/mosquitto.conf

    environment:
      MQTT_HEALTH_USER: ${Mqtt__Username:-backend-bridge}
      MQTT_HEALTH_PASS: ${Mqtt__Password:?bat buoc}
    healthcheck:
      test: ["CMD-SHELL", "mosquitto_pub -h 127.0.0.1 -p 8883 --cafile /mosquitto/certs/ca.crt -u \"$$MQTT_HEALTH_USER\" -P \"$$MQTT_HEALTH_PASS\" -t solar/healthcheck -m ok -q 1"]
      interval: 10s
      timeout: 5s
      retries: 5

    networks: [solar-net]

# (6) Dùng CHUNG mạng với backend — backend compose up TRƯỚC để mạng tồn tại
networks:
  solar-net:
    external: true
    name: backend_solar-net

volumes:
  mosquitto-data:
  mosquitto-log:
```

Trước lần chạy đầu, bật TLS:
```bash
cd capstone/iot
./infra/mqtt/scripts/gen-certs.sh <broker-fqdn>   # sinh cert + conf.d/tls.conf
```

Backend env tương ứng:
```
Mqtt__Enabled=true
Mqtt__Host=mosquitto
Mqtt__Port=8883
Mqtt__UseTls=true
Mqtt__AllowUntrustedCertificates=false
Mqtt__PasswordFilePath=/mosquitto-config/passwd
```

### Bước 4 — ⚠️ Quyền file: bẫy chí mạng

Mosquitto 2.0 **từ chối nạp** file `passwd` mà người khác đọc được. Nhưng backend ghi file đó ở chế độ **0600 với UID của container backend**, còn Mosquitto chạy **UID 1883**.

⇒ Nếu hai UID khác nhau, Mosquitto sẽ **không đọc nổi** file backend vừa ghi → mọi thiết bị bị từ chối.

Hai cách xử lý:

| Cách | Làm gì |
|---|---|
| **A (đơn giản)** | Đặt `user: "1883:1883"` cho **container batteryservice** trong `docker-compose.prod.yml` của backend |
| **B (linh hoạt)** | Tạo group chung, `group_add: ["1883"]` ở backend + sửa `WriteAtomicallyAsync` ghi mode `0640` |

**Bắt buộc kiểm chứng sau khi deploy:**
```bash
docker exec solar-mosquitto cat /mosquitto/config/passwd      # phải đọc được
docker exec solar-mosquitto ls -l /mosquitto/config/passwd    # xem UID/mode
docker logs solar-mosquitto 2>&1 | grep -i "world readable\|denied\|Error"   # phải rỗng
```

### Bước 5 — Xoay mật khẩu `backend-bridge` định kỳ

```bash
NEW_PW=$(openssl rand -base64 32 | tr -d '/+=' | head -c 32)
docker exec -i solar-mosquitto mosquitto_passwd -b /mosquitto/config/passwd backend-bridge "$NEW_PW"
sed -i "s|^Mqtt__Password=.*|Mqtt__Password=$NEW_PW|" /opt/solar/.env.prod
docker compose -f /opt/solar/backend/docker-compose.prod.yml up -d batteryservice
unset NEW_PW
```

> Dòng `backend-bridge` nằm ngoài vùng `# >>> BatteryService managed devices` nên `MosquittoPasswordFile.Compose()` giữ nguyên từng ký tự — an toàn.

### Bẫy mới phát hiện: `gen-certs.sh` không cập nhật CA nhúng

> ⚠️ **Bẫy im lặng** — không có cảnh báo nào khi lệch

Script đã được cải tiến (2026-07-31): giờ nó tự sinh `conf.d/tls.conf` cùng lúc với cert, bỏ được bước uncomment tay trong `mosquitto.conf`.

**Nhưng** nó vẫn chỉ hướng dẫn flow cũ:
```
cp $CA_CRT firmware-esp32/data/ca_cert.pem
pio run -t uploadfs
```

Nó **không** sinh lại `src/net/ca_cert_embedded.h` — file mà firmware giờ ưu tiên dùng cho MQTT.

⇒ **Chạy lại `gen-certs.sh` là broker có CA mới, firmware vẫn nhúng CA cũ** → MQTT bắt tay hỏng với lỗi `-9984 X509 Certificate verification failed`. Không có cảnh báo nào.

**Sửa (~15 phút):** thêm vào cuối `gen-certs.sh`:
```bash
awk 'BEGIN{print "static const char kMqttCaCert[] = R\"CERT("} {print} END{print ")CERT\";"}' \
  "$CA_CRT" > ../../firmware-esp32/src/net/ca_cert_embedded_generated.h
```
(hoặc in cảnh báo to nhắc regen tay)

### Việc cần thêm vào plan

| Task | Việc | Công |
|---|---|---|
| **T0.3** | Viết `iot/infra/docker-compose.prod.yml` | ~2h |
| **T0.4** | Xử lý UID/quyền file `passwd` + test đọc được | ~1h |
| **T0.5** ⭐mới | `gen-certs.sh` regen luôn `ca_cert_embedded.h` | ~15ph |

---

# 10 — Hiển thị QR trên UI thay vì in giấy

## Trả lời ngắn: **được, ~5h** — nhưng có 2 thứ phải sửa

### ✅ Thư viện QR **đã cài sẵn**

```json
// frontend/package.json dòng 38
"qrcode.react": "^4.2.0"
```

### ✅ Backend **đã sinh chuỗi QR**

[IotDeviceMapper.cs:95](services/BatteryService/src/BatteryService.Application/Mapping/IotDeviceMapper.cs#L95):
```csharp
ProvisioningQrCode = $"iot://provision?dc={Uri.EscapeDataString(e.DeviceCode)}&key={Uri.EscapeDataString(rawApiKey)}"
```

### ❌ Vấn đề 1: đang render QR dạng **chữ**, không phải hình

[DeviceKeyRevealDialog.tsx](../frontend/src/features/admin/components/iot/DeviceKeyRevealDialog.tsx):
```tsx
<CopyRow label="Provisioning QR" value={device.provisioningQrCode} />
```

Tức là một ô input chứa chuỗi `iot://provision?dc=...&key=...`. **Không quét được.**

### ❌ Vấn đề 2: dialog cảnh báo sai — và chỉ đúng một nửa

Dialog ghi *"Các giá trị này chỉ hiển thị MỘT LẦN… không thể xem lại."* Đối chiếu backend:

| Trường | Xem lại được? | Bằng chứng |
|---|---|---|
| `apiKey` | ✅ **CÓ** | `IotDeviceDetailDto.ApiKey`; comment GH-724: *"KHÔNG phải 'chỉ 1 lần': key vẫn đọc lại được qua GET /api/admin/iot-devices/{id}"* |
| `provisioningQrCode` | ❌ Không | Detail DTO không có trường này |
| `mqttUsername` | ❌ Không | Detail DTO không có |
| `mqttPassword` | ❌ Không (và **không thể** — chỉ lưu hash) | Cần T1.1 |
| `mqttBrokerHost/Port/UseTls/TopicPrefix` | ❌ Không | Detail DTO không có |

⇒ Cảnh báo **sai với `apiKey`** (gây hoang mang thừa) và **đúng với 5 trường còn lại** — nhưng đó mới là thứ cần sửa.

## ✅ Giải pháp

### Backend — 3 việc (~1,5h)

**(a)** Mở rộng `IotDeviceDetailDto`:
```csharp
public class IotDeviceDetailDto : IotDeviceDto
{
    public string? ApiKey { get; set; }                 // đã có

    // ⭐ mới — để Admin xem lại và in nhãn bất cứ lúc nào
    public string? ProvisioningQrCode { get; set; }
    public string? MqttUsername { get; set; }
    public string? MqttPassword { get; set; }           // cần cột plaintext (T1.1)
    public string? MqttBrokerHost { get; set; }
    public int?    MqttBrokerPort { get; set; }
    public bool?   MqttUseTls { get; set; }
    public string? MqttTopicPrefix { get; set; }
}
```

**(b)** `GetIotDeviceByIdQueryHandler` — inject `IMqttBrokerEndpointProvider`, dựng lại QR từ `ApiKeyPlaintext`:
```csharp
if (!string.IsNullOrEmpty(entity.ApiKeyPlaintext))
    dto.ProvisioningQrCode =
        $"iot://provision?dc={Uri.EscapeDataString(entity.DeviceCode)}" +
        $"&key={Uri.EscapeDataString(entity.ApiKeyPlaintext)}";

var broker = _brokerEndpoint.Resolve(entity.DeviceCode);
dto.MqttUsername    = entity.MqttUsername;
dto.MqttPassword    = entity.MqttPasswordPlaintext;    // T1.1
dto.MqttBrokerHost  = broker.Host;
dto.MqttBrokerPort  = broker.Port;
dto.MqttUseTls      = broker.Host is null ? null : broker.UseTls;
dto.MqttTopicPrefix = broker.TopicPrefix;
```

**(c)** Phụ thuộc **T1.1** — `MqttPassword` cần cột `MqttPasswordPlaintext`. Hash `$7$` là một chiều, không đọc ngược được.

> ~~**Phương án thay thế:** nút "Xoay khoá MQTT" thay vì lưu plaintext.~~
> ❌ **ĐÃ LOẠI (Q8)** — chốt **lưu `MqttPasswordPlaintext`**, cùng khuôn `ApiKeyPlaintext` đã quyết 2026-07-16. Không mở ra loại phơi nhiễm mới (cùng bảng, cùng endpoint, cùng lớp quyền Admin). Nút xoay khoá vẫn làm nhưng là việc riêng — xem Q6.

### Frontend — 4 việc (~3,5h)

**(d)** Render QR thành hình:
```tsx
import { QRCodeSVG } from "qrcode.react";

<div className="flex justify-center p-4 bg-white rounded-lg">
  <QRCodeSVG
    value={device.provisioningQrCode}
    size={200}
    level="M"              // sửa lỗi ~15% — đủ cho nhãn dán có thể bị xước
    includeMargin
  />
</div>
```

**(e)** Nút **"Xem lại thông tin"** ở `IoTDeviceTable` → gọi `GET /api/admin/iot-devices/{id}` → mở dialog.

**(f)** Sửa cảnh báo, tách hai nhóm rõ ràng:
```
✅ Xem lại được bất cứ lúc nào:  API Key · QR · MQTT username/broker
⚠️ Chỉ hiện lúc tạo/xoay khoá:   MQTT password
```

**(g)** Nút **In nhãn**:
```tsx
<div className="print-label hidden print:block">
  <QRCodeSVG value={device.provisioningQrCode} size={120} />
  <div className="font-mono font-bold">{device.deviceCode}</div>
  <div className="text-xs">Setup: SolarGW-XXXX / solar2026</div>
</div>
```
```css
@media print {
  body > *:not(.print-label) { display: none; }
  @page { size: 50mm 30mm; margin: 2mm; }
}
```

## ⚠️ Vẫn nên giữ nhãn giấy

Lúc khách đổi WiFi, kỹ thuật viên đứng trước tủ pin cần mật khẩu AP setup — mà lúc đó **có thể không có mạng** để mở web admin. Nhãn dán là bản sao lưu không phụ thuộc mạng.

⇒ **UI QR là để in ra nhãn, không phải để thay thế nhãn.**

---

# 11 — Trang cấu hình thiết bị — đã có chưa

## Trả lời ngắn: **CHƯA CÓ. Không có một dòng nào.** (kiểm lại sau khi pull — vẫn vậy)

```bash
grep -rn "WebServer|DNSServer|softAP|WIFI_AP|WiFiManager|captive" \
  --include="*.cpp" --include="*.h" --include="*.ini" firmware-esp32/
→ 0 kết quả
```

Cụ thể **không có**:

| Thứ | Trạng thái |
|---|---|
| Thư viện `WiFiManager` trong `platformio.ini` | ❌ `lib_deps` chỉ có PubSubClient, ArduinoJson, ModbusMaster, OneWire, DallasTemperature, INA219, INA226, SHT31, BusIO |
| `WiFi.softAP()` — phát sóng | ❌ |
| `WebServer` / `AsyncWebServer` | ❌ |
| `DNSServer` — DNS wildcard để trang tự mở | ❌ |
| File HTML trang cấu hình | ❌ |
| Chế độ `WIFI_AP` / `WIFI_AP_STA` | ❌ — [wifi_manager.cpp:55](../iot/firmware-esp32/src/net/wifi_manager.cpp#L55) chỉ có `WiFi.mode(WIFI_STA)` |

**Toàn bộ `wifi_manager.cpp` vẫn đúng 100 dòng**, làm một việc: nối WiFi với SSID/PASS từ `config.h`.

## ✅ Giải pháp (task T3.3, ~6h)

### Chọn thư viện

**Dùng `tzapu/WiFiManager`** thay vì tự viết. Nó xử lý sẵn phần khó nhất: DNS wildcard + phát hiện captive portal cho iOS/Android (mỗi hệ một URL kiểm tra khác nhau, dễ sót). Tốn ~50KB flash — không đáng lo với ESP32-S3 16MB.

```ini
; platformio.ini — thêm vào lib_deps
tzapu/WiFiManager @ ^2.0.17
```

### Khung code

```cpp
#include <WiFiManager.h>

bool runSetupPortal() {
  WiFiManager wm;

  // Ô nhập thêm ngoài WiFi — đây là thứ ta cần
  WiFiManagerParameter pDevCode("devcode", "Device Code", identity::deviceCode(), 64);
  WiFiManagerParameter pApiKey ("apikey",  "API Key",     "",                    128);
  wm.addParameter(&pDevCode);
  wm.addParameter(&pApiKey);

  // AP CÓ MẬT KHẨU — không để mở, kẻo hàng xóm cấu hình hộ
  char apName[32];
  uint8_t mac[6]; WiFi.macAddress(mac);
  snprintf(apName, sizeof(apName), "SolarGW-%02X%02X", mac[4], mac[5]);

  // Chế độ phục hồi: 10 phút rồi tắt AP, quay lại thử WiFi cũ.
  // Chế độ setup lần đầu: chờ vô hạn (0) vì chắc chắn phải có người cấu hình.
  wm.setConfigPortalTimeout(wificfg::isConfigured() ? 600 : 0);

  wm.setSaveParamsCallback([&]() {
    identity::setDeviceCode(pDevCode.getValue());
    identity::setApiKey    (pApiKey.getValue());
  });

  bool ok = wm.startConfigPortal(apName, AP_SETUP_PASSWORD);
  if (ok) {
    wificfg::save(wm.getWiFiSSID().c_str(), wm.getWiFiPass().c_str());
    delay(2000);
    ESP.restart();
  }
  return ok;
}
```

### Sáu việc con

| # | Việc | Công |
|---|---|---|
| a | Thêm lib + khung `runSetupPortal()` | 1h |
| b | Máy trạng thái WiFi 3 chế độ (T3.2 — sơ đồ trong file kế hoạch) | 3h |
| c | Tuỳ biến HTML: hiển thị **RSSI** khi chọn mạng; < −75 dBm thì cảnh báo | 1h |
| d | Nút quét QR — dùng `BarcodeDetector` API của trình duyệt điện thoại | 1h |
| e | Validate trước khi lưu: `deviceCode` ≤ 64 ký tự, `apiKey` bắt đầu `iotk_` | (trong a) |
| f | Lọc + cảnh báo khi chỉ dò được mạng 5 GHz | (trong c) |

### Bẫy cần biết

| Bẫy | Xử lý |
|---|---|
| Có điện thoại không tự mở trang | In sẵn `http://192.168.4.1` trên nhãn để gõ tay |
| AP không mật khẩu = ai cũng cấu hình được | Đặt `AP_SETUP_PASSWORD` (Tầng 1, in trên nhãn) |
| Chế độ phục hồi phát sóng mãi mãi | `setConfigPortalTimeout(600)` — 10 phút |
| WiFi khách là **WPA2-Enterprise** | `WiFi.begin(ssid,pass)` **không hỗ trợ**. Xem rủi ro ở Phần 16 |

---

# 12 — Staff theo dõi thiết bị IoT

## Hiện trạng — nhiều hơn bạn nghĩ

| Nền tảng | Đã có | File |
|---|---|---|
| **Web Staff** | ✅ Trang tra cứu theo mã + quản lý calibration | [IoTCalibrationsPage.tsx](../frontend/src/features/staff/pages/IoTCalibrationsPage.tsx) |
| Web Staff | ✅ Mục "Calibration thiết bị" trong sidebar | `staffNav.ts:50-51` → `/staff/iot-calibrations` |
| Web Staff | ✅ Badge trạng thái + bảng calibration dùng chung | `shared/components/iot/` |
| **Mobile Staff** | ✅ Màn hình tra cứu + calibration | `app/(staff)/tools/calibration/{index,create}.tsx` |
| Mobile Staff | ✅ Service + hooks + types đầy đủ | `src/features/iot-devices/` |

## Quyền hiện tại

| Endpoint | Quyền | Staff? |
|---|---|---|
| `GET /api/iot-devices/by-code/{code}` | `Admin,Manager,Staff` | ✅ |
| `GET /api/iot-devices/{id}/calibrations` | `Admin,Manager,Staff` | ✅ |
| `POST`/`DELETE` calibrations | `Admin,Staff` | ✅ |
| `GET /api/iot-devices/calibrations-expiring` | `Admin,Manager` | ❌ |
| **Toàn bộ `/api/admin/iot-devices/*`** | **`Admin`** ([dòng 32](services/BatteryService/src/BatteryService.Api/Controllers/Admin/AdminIotDevicesController.cs#L32)) | ❌ |

## ⭐ Phát hiện: Staff **đã xem được trạng thái** rồi

`by-code` trả về **`IotDeviceDto` đầy đủ** — không phải subset:

```
status, lastSeenAt, lastProvisionedAt, lastOfflineAt,
currentFirmwareVersion, targetFirmwareVersion,
heartbeatIntervalSeconds, lastClockSkewSeconds, apiKeyLastFour, siteName, notes …
```

Nhưng mobile chỉ khai **6 trường** trong type:
```ts
// mobile/src/features/iot-devices/types/iot-device.types.ts
interface IotDeviceDto { id, deviceCode, displayName, status, siteId, siteName }
```

⇒ **Dữ liệu đã về máy rồi, chỉ là không hiển thị.** Mở rộng type + UI là xong, **không cần đụng backend**.

## Cái thực sự còn thiếu

| # | Thiếu gì | Ghi chú |
|---|---|---|
| 1 | **Danh sách thiết bị cho Staff** | `/api/admin/iot-devices` là `Admin` only. Staff phải **biết trước mã** — không duyệt được |
| 2 | **Lịch sử heartbeat** | ⚠️ Endpoint `GET /api/admin/iot-devices/{id}/heartbeats` **KHÔNG TỒN TẠI**. XML doc [dòng 114](services/BatteryService/src/BatteryService.Api/Controllers/Admin/AdminIotDevicesController.cs#L114) nói *"dùng `GET .../{id}/heartbeats` (Sprint IoT-2)"* — grep cả controller, **chưa ai viết**. Tài liệu nói dối |
| 3 | Calibration sắp hết hạn cho Staff | `Admin,Manager` only |
| 4 | Quét QR trên mobile | `package.json` chỉ có `react-native-qrcode-svg` (**hiển thị**), **không có** `expo-camera`/`expo-barcode-scanner` (**quét**) |

## ✅ Giải pháp

### 🥇 Làm trước — rẻ nhất, hiệu quả cao nhất (~1h, **không đụng backend**)

```ts
// mobile/src/features/iot-devices/types/iot-device.types.ts
export interface IotDeviceDto {
  id: string;
  deviceCode: string;
  displayName: string;
  status: IotDeviceStatusEnum;
  siteId: string;
  siteName: string | null;

  // ⭐ API ĐÃ TRẢ SẴN — chỉ cần khai để dùng
  lastSeenAt: string | null;
  lastProvisionedAt: string | null;
  lastOfflineAt: string | null;
  currentFirmwareVersion: string | null;
  targetFirmwareVersion: string | null;
  heartbeatIntervalSeconds: number;
  lastClockSkewSeconds: number | null;
  apiKeyLastFour: string;
  hardwareRevision: string | null;
}
```

Rồi bổ sung thẻ trạng thái vào màn hình calibration:
```
┌──────────────────────────────────┐
│ GW-ESP32-001        ● Active     │
│ Nhà anh Nam - tủ 1               │
│ Thấy lần cuối:  2 phút trước     │
│ Firmware:       0.1.0-sprint4    │
│ Lệch đồng hồ:   +1,2 giây        │
└──────────────────────────────────┘
```

**80% giá trị với 6% công sức.**

### Backend (~6h)

**(a)** Endpoint danh sách cho Staff — tái dùng `GetIotDevicesQuery` sẵn có:
```csharp
[HttpGet]
[Authorize(Roles = "Admin,Manager,Staff")]
public async Task<IActionResult> List([FromQuery] GetIotDevicesQuery q, CancellationToken ct)
    => StatusCode((await _mediator.Send(q, ct)).StatusCode, ...);
```
> Trả `IotDeviceDto` thường — **không** `apiKey`/`mqttPassword`.
>
> ⚠️ Cẩn thận trùng route: `IotDeviceCalibrationsController` đã `[Route("api/iot-devices")]`. Đặt action vào **chính controller đó**, đừng tạo controller mới cùng route.

**(b)** Viết endpoint heartbeat **đang thiếu** — cursor pagination theo `time`, **không dùng offset** (hypertable, be.md §13):
```csharp
var q = _unitOfWork.IotDeviceHeartbeats.GetAllAsync()
    .Where(h => h.IotDeviceId == deviceId);
if (cursor.HasValue) q = q.Where(h => h.Time < cursor.Value);   // cuộn ngược thời gian
var items = await q.OrderByDescending(h => h.Time)
                   .Take(Math.Min(limit, 1000) + 1)             // +1 để biết còn trang sau
                   .ToListAsync(ct);
bool hasMore = items.Count > limit;
if (hasMore) items.RemoveAt(items.Count - 1);
// nextCursor = items.Last().Time ; totalCount = null (time-series không đếm full)
```

**(c)** Mở `calibrations-expiring` cho `Staff` — sửa 1 dòng attribute.

**(d)** ✅ **Đã chốt (Q7): Staff xem MỌI thiết bị — KHÔNG lọc theo site.**
> Nhất quán tiền lệ spec §34.10.6 *"Staff xem được mọi asset là CỐ Ý"*. Đừng thêm bộ lọc tenant vào endpoint này.

### Web Staff (~6h)

| # | Việc |
|---|---|
| e | Trang `/staff/iot-devices` — bảng danh sách, badge trạng thái, lọc, tìm kiếm |
| f | Trang chi tiết: thông tin + biểu đồ heartbeat (RSSI, uptime, lệch đồng hồ, số bản ghi xếp hàng) |
| g | Thêm mục vào `staffNav.ts` |
| h | Tái dùng `shared/components/iot/IoTDeviceStatusBadge` đã có |

### Mobile Staff (~6h)

| # | Việc |
|---|---|
| i | ⭐ Mở rộng type (mục 🥇) — **~1h, làm trước** |
| j | Màn hình `app/(staff)/tools/devices/index.tsx` — danh sách |
| k | Màn hình `[id].tsx` — chi tiết + heartbeat |
| l | Thêm vào `tools/index.tsx` — **nhớ mở rộng union type `href`** (nó liệt kê tường minh từng route) |
| m | Quét QR: cần cài thêm `expo-camera` (SDK 51+ có `CameraView` + `barcodeScannerSettings`) |

### Ước lượng

| Phần | Công |
|---|---|
| 🥇 Mở rộng type mobile | **~1h** |
| Backend (a–d) | ~6h |
| Web Staff (e–h) | ~6h |
| Mobile Staff (j–m) | ~6h |
| **Tổng** | **~19h ≈ 2,5 ngày** |

---

# 13 — Bốn lỗi cấu hình phần cứng (phải sửa trước khi cắm BMS thật)

> **Bối cảnh phần cứng (đã xác nhận từ nhãn BMS + `battery_mapping.h`):**
> **JK-BD6A24S10P** — 100 A liên tục · 200 A đỉnh · 8–24S · cân bằng chủ động 0,6 A · SN `512261K3E001783`
> Pack đang dùng: **8S LiFePO₄ 24 V 30 Ah** (`BAT-2026-REAL-001`) · cảm biến phụ: **INA226** + **DS18B20**

## ✅ Tin tốt trước: hai vấn đề của bản 1 đã được giải quyết

### Đã sửa — dòng điện tràn số ở 32,7 A

Commit `39a4b2f` *"feat(bms): Implement JK-BMS Modbus V1.1 support with dedicated decoding functions"* viết lại **toàn bộ** đường JK:

| | Bản trước | Bản hiện tại trên `dev` |
|---|---|---|
| Cách đọc | 1 block liên tục 16 thanh ghi từ `0x1200` | **Sparse blocks** — 9 địa chỉ rời rạc |
| Dòng điện | `raw[2]` → ép `int16` → **±32,767 A** | `decodeJkPackCurrent(hi, lo)` → **int32** → ±2.147.483 A |
| Điện áp | `raw[0]` × 0,001 | `decodeJkPackVoltage(hi, lo)` → UINT32 mV |
| `kJkBmsMap` | offsets thật | **toàn `kRegMissing`** — dùng decoder riêng |

Địa chỉ mới ([bms_register_map.h:81-89](../iot/firmware-esp32/src/bms/bms_register_map.h#L81-L89)):
```
0x128A MOS temp        0x1290 pack voltage (U32)   0x1298 pack current (I32)
0x129C battery temps   0x12A0 alarm (U32)          0x12A6 SOC (byte thấp)
0x12B0 cycle (U32)     0x12B8 SOH (byte cao)       0x12C0 switch status
```

Thứ tự word: **most-significant word first** — `(hi << 16) | lo`.

### Đã kiểm chứng — bản đồ thanh ghi JK

Có test đối chiếu **dữ liệu capture từ phần cứng thật**:
```cpp
test_jk_realtime_values_match_hardware_capture()
  decodeJkTemperature(0x013B) → 31,5 °C
  decodeJkSoc(0x002A)         → 42 %
  decodeJkSoh(0x6400)         → 100 %
test_jk_signed_current_decode()
test_jk_charging_state_uses_switches_and_current_direction()
test_jk_uses_dedicated_sparse_layout()
```

### Đã chốt — điện áp hệ thống

`battery_mapping.h` ghi rõ: *"pack THẬT (JK-BMS 8S 24V 30Ah)"*, asset `BAT-2026-REAL-001`, ngưỡng 20–29,5 V.

⇒ Tối đa ~29,2 V < giới hạn bus **36 V** của INA226. **An toàn** — nhưng chỉ tới **9S**; xem ràng buộc ở cuối LỖI 2.

---

## LỖI 1 — `BMS_MODEL` đang chọn nhầm loại BMS ⇒ code JK là **code chết**

> 🔴 **Mức độ: CAO**

[config.example.h:170](../iot/firmware-esp32/include/config.example.h#L170):
```c
#define BMS_MODEL  2          // JBD default
```

Đường JK được gate bằng `#if BMS_MODEL == 3` ([modbus_bms.cpp:214](../iot/firmware-esp32/src/bms/modbus_bms.cpp#L214)). Với `BMS_MODEL = 2`, **toàn bộ code JK vừa viết không được biên dịch vào firmware.**

Người viết code JK có `BMS_MODEL=3` trong `config.h` local của họ — nhưng `config.h` **nằm trong `.gitignore`**, nên ai copy `config.example.h` → `config.h` sẽ nhận **JBD**.

Hậu quả khi chạy JBD trên phần cứng JK:

| | JBD (đang chọn) | JK (bạn có) |
|---|---|---|
| Địa chỉ | `0x0000` | `0x128A`–`0x12C0` |
| Thang điện áp | ×0,01 | ×0,001 (mV) |
| Thang dòng | ×0,01, 16 bit | ×0,001, **32 bit** |
| Thang nhiệt | Kelvin ×0,1, bias −273,15 | °C ×0,1, bias 0 |

JK không có thanh ghi ở `0x0000` → **timeout mọi lần đọc, không có số liệu nào**.

### ✅ Sửa

```c
// include/config.example.h  VÀ  include/config.h
#define BMS_MODEL  3          // JK-BMS
```

Hoặc bền hơn — thêm vào `platformio.ini` để không phụ thuộc file local:
```ini
[env:esp32-s3-real]
build_flags =
    ${env:esp32-s3-devkitc-1.build_flags}
    -UBMS_MODEL
    -DBMS_MODEL=3
    -UUSE_MOCK_BMS
    -DUSE_MOCK_BMS=0
```

**1 phút.**

---

## LỖI 2 — Cấu hình INA226 **sai về mặt vật lý**, chip không khởi tạo được

> 🔴 **Mức độ: CAO** — và có **ba ràng buộc phụ của thư viện** mà giá trị "đúng về lý thuyết" vẫn vướng.

### Thông số BMS thật (đọc từ nhãn, 2026-08-07)

| Trường trên nhãn | Giá trị |
|---|---|
| 型号 (model) | **JK-BD6A24S10P** |
| 均衡方式 (kiểu cân bằng) | 主动均衡 — **cân bằng chủ động** |
| 均衡电流 (dòng cân bằng) | 0,6 A |
| 持续电流 (dòng liên tục) | **100 A** |
| 瞬时电流 (dòng đỉnh) | **200 A** |
| 电池串数 (số cell nối tiếp) | 8–24S |
| SN | 512261K3E001783 |
| MAC | 28:D4:1E:6A:F6:78 |

⇒ Phải đo được tới **200 A**, không phải 20 A.

### Vì sao cấu hình hiện tại chắc chắn hỏng

[config.example.h:186-187](../iot/firmware-esp32/include/config.example.h#L186-L187):
```c
#define INA226_SHUNT_OHM     0.1f      // 100 mΩ
#define INA226_MAX_CURRENT_A 20.0f
```

[ina226.cpp:49](../iot/firmware-esp32/src/sensor/ina226.cpp#L49) gọi `setMaxCurrentShunt(20.0, 0.1)`. Thư viện `robtillaart/INA226 v0.6.6` kiểm ba điều kiện ([INA226.cpp:220-223](../iot/firmware-esp32/.pio/libdeps/esp32-s3-devkitc-1/INA226/INA226.cpp)):

```cpp
float shuntVoltage = maxCurrent * shunt;
if (shuntVoltage > 0.08190)           return INA226_ERR_SHUNTVOLTAGE_HIGH;   // 81,90 mV
if (maxCurrent < 0.001)               return INA226_ERR_MAXCURRENT_LOW;
if (shunt < INA226_MINIMAL_SHUNT_OHM) return INA226_ERR_SHUNT_LOW;           // mặc định 1 mΩ
```

```
20 A × 0,1 Ω = 2,0 V  >  0,08190 V     →  ERR_SHUNTVOLTAGE_HIGH (0x8000)
```

⇒ `ina226Begin()` trả false, `s_inited` không bao giờ được đặt. **Nguồn `redundant` chưa từng có dữ liệu.**

Với 100 A thì còn tệ hơn về vật lý: `P = I²R = 100² × 0,1 = **1 000 W**` — shunt bốc cháy trong vài giây.

### ✅ Sửa — bốn bước, thiếu bước nào cũng vẫn hỏng

#### Bước 1 — Phần cứng: shunt **200 A / 75 mV = 0,375 mΩ**

Chọn theo **dòng đỉnh 200 A**, không theo dòng liên tục 100 A:

| Phương án | Dải đo tối đa | Tản nhiệt @100 A | Đánh giá |
|---|---|---|---|
| Shunt 100A/75mV (**0,75 mΩ**) | 81,90 mV ÷ 0,75 mΩ = **109 A** | 7,5 W | ❌ Chỉ dư 9 % trên dòng liên tục, **mù hoàn toàn** ở đỉnh 200 A |
| **Shunt 200A/75mV (0,375 mΩ)** | 81,90 mV ÷ 0,375 mΩ = **218 A** | **3,75 W** | ✅ Phủ cả đỉnh, **toả nhiệt bằng nửa** |

Ở 200 A đỉnh: `200 × 0,000375 = 75 mV` ✓ (dưới ngưỡng 81,90 mV) · `P = 15 W` (chỉ thoáng qua).

Phải là **shunt bắt bu-lông** (khối kim loại, 4 chân — 2 chân dòng lớn, 2 chân đo). **Không phải** điện trở gốm nhỏ.

#### Bước 2 — Config

```c
// include/config.example.h  VÀ  include/config.h
#define INA226_SHUNT_OHM     0.000375f   // shunt 200A/75mV = 0,375 mΩ
#define INA226_MAX_CURRENT_A 200.0f      // dòng đỉnh của JK-BD6A24S10P
```

#### Bước 3 — ⚠️ Gỡ chặn `INA226_MINIMAL_SHUNT_OHM` của thư viện

Thư viện chặn mọi shunt **nhỏ hơn 1 mΩ** ([INA226.h:44-45](../iot/firmware-esp32/.pio/libdeps/esp32-s3-devkitc-1/INA226/INA226.h)):

```cpp
#ifndef INA226_MINIMAL_SHUNT_OHM
#define INA226_MINIMAL_SHUNT_OHM          0.001
#endif
```

`0,000375 < 0,001` ⇒ trả `INA226_ERR_SHUNT_LOW (0x8002)`. **Đúng shunt vật lý nhưng vẫn không init được.**

May là hằng có `#ifndef` nên ghi đè được bằng build flag — **không cần fork thư viện**:

```ini
; platformio.ini — thêm vào [env] để áp cho mọi env
build_flags =
    ...
    -DINA226_MINIMAL_SHUNT_OHM=0.0001    ; cho phép shunt sub-mΩ (200A/75mV = 0,375 mΩ)
```

#### Bước 4 — ⚠️ Tắt `normalize` (nếu không sẽ chặn ở 163 A)

`setMaxCurrentShunt(maxCurrent, shunt, normalize = true)` — tham số thứ ba **mặc định `true`**, và firmware đang gọi bằng 2 tham số nên nhận mặc định đó.

Khối normalize làm tròn `current_LSB` về dạng 1/2/5 × 10ⁿ µA, nhưng vòng lặp chỉ chạy `i < 4` ⇒ **giá trị lớn nhất nó tạo được là 5 000 µA**. Truy ngược:

```
current_LSB = maxCurrent / 32768
cần: current_LSB × 1e6 + 1 ≤ 5000   ⇒   maxCurrent ≤ 163,8 A
```

Với `maxCurrent = 200`: `LSB = 6 103,5 µA` → vượt 5 000 → vòng lặp thoát với `result = false` → **`INA226_ERR_NORMALIZE_FAILED (0x8003)`**.

⇒ Phải truyền tường minh `false` ([ina226.cpp:49](../iot/firmware-esp32/src/sensor/ina226.cpp#L49)):

```cpp
// TRƯỚC
int err = s_ina226.setMaxCurrentShunt(INA226_MAX_CURRENT_A, INA226_SHUNT_OHM);

// SAU — normalize=false vì maxCurrent 200 A vượt trần 163,8 A của khối làm tròn
int err = s_ina226.setMaxCurrentShunt(INA226_MAX_CURRENT_A, INA226_SHUNT_OHM, false);
```

Kiểm lại toàn bộ phép tính với `normalize = false`:

| Đại lượng | Giá trị |
|---|---|
| `current_LSB` = 200 ÷ 32768 | **6,104 mA** (độ phân giải dòng) |
| `calib` = round(0,00512 ÷ (6,104e-3 × 3,75e-4)) | **2 237** ✓ (≤ 32 767, không phải tự chia đôi) |
| `_maxCurrent` = 6,104e-3 × 32768 | **200,0 A** ✓ |
| Điện áp shunt ở 200 A | 75 mV ✓ (< 81,90 mV) |

Không có bước nào trả lỗi. ✓

#### Bước 5 (nhỏ) — Sửa log in sai số 0

[ina226.cpp:51,57](../iot/firmware-esp32/src/sensor/ina226.cpp#L51) dùng `%.3f` cho shunt. Với `0,000375` nó in ra **`0.000`** — nhìn như chưa cấu hình gì. Đổi sang `%.6f`, hoặc in bằng mΩ (`shunt * 1000.0f` với `%.3f mΩ`).

### Xác nhận sau khi sửa

Log boot **phải** có:
```
[ina226] init OK addr=0x40 shunt=0.000375Ω max=200.0A
```
và **không** có `setMaxCurrentShunt FAIL`.

### ⚠️ Ràng buộc kèm theo: INA226 giới hạn pack ở **9S LiFePO₄**

Bus voltage của INA226 tối đa **36 V**. BMS của bạn hỗ trợ **8–24S**, nên cần biết trần:

| Số cell | Điện áp đầy (3,65 V/cell) | INA226 đo được? |
|---|---|---|
| 8S (hiện tại) | 29,2 V | ✅ |
| 9S | 32,9 V | ✅ |
| **10S** | **36,5 V** | ❌ **vượt 36 V** |
| 16S (48 V) | 58,4 V | ❌ |
| 24S | 87,6 V | ❌ |

⇒ Pack 8S hiện tại **an toàn**. Nếu sau này nâng lên ≥ 10S thì phải đổi sang **INA228/INA238** (bus 85 V), hoặc bỏ đo áp bằng INA226 và chỉ dùng nó đo dòng qua shunt.

---

## LỖI 3 — `BMS_UNIT_ID_COUNT` lệch với `battery_mapping.h` (**MỚI**)

> 🔴 **Mức độ: CAO** — nguyên nhân chính khiến chu kỳ đội lên 13×

[battery_mapping.h](../iot/firmware-esp32/src/config/battery_mapping.h) vừa đổi, comment mới ghi:

> *"Slot 1 = pack **THẬT** (JK-BMS 8S 24V 30Ah), unitId 1, **khớp `BMS_UNIT_ID_COUNT=1`**. Các slot dưới chỉ dùng ở chế độ mock."*

Nhưng `config.example.h` (và `config.h`) vẫn là:
```c
#define BMS_UNIT_ID_COUNT    4
```

**Hệ quả với phần cứng thật:** `modbusReadMultiDrop()` lặp unitId 1→4, nhưng chỉ có **một** BMS ở unitId 1. Ba lần còn lại đều timeout.

Kết hợp với **LỖI 4** bên dưới, đây là nguyên nhân chính khiến chu kỳ đội lên **~13 giây**. Chi tiết tính toán: [Phần 14](#14--chu-kỳ-gửi-số-liệu--có-giảm-dưới-10-giây-được-không).

### ✅ Sửa

```c
#define BMS_UNIT_ID_COUNT    1
```

**1 phút — và đây là thay đổi có hiệu quả lớn nhất trong toàn bộ tài liệu này** (chu kỳ 13,2 s → ~1,0 s).

> Khi lắp thêm pin thật thì tăng lại cho khớp số BMS **thực sự có mặt trên bus**, không phải số slot trong `battery_mapping.h`.

---

## LỖI 4 — `BMS_POLL_TIMEOUT_MS` là **hằng chết**, timeout thật là 2000 ms (**MỚI**)

> 🟡 **Mức độ: TRUNG BÌNH** — gần như vô hại sau khi sửa LỖI 3

[config.example.h:176](../iot/firmware-esp32/include/config.example.h#L176):
```c
#define BMS_POLL_TIMEOUT_MS  500UL      // Modbus request timeout per battery
```

Nhưng:
```bash
$ grep -rn "BMS_POLL_TIMEOUT_MS" firmware-esp32/src/
→ 0 kết quả
```

**Hằng này không được dùng ở bất cứ đâu trong mã nguồn.**

Timeout thật đến từ thư viện `ModbusMaster` ([ModbusMaster.h:252](../iot/firmware-esp32/.pio/libdeps/esp32-s3-devkitc-1/ModbusMaster/src/ModbusMaster.h)):
```cpp
static const uint16_t ku16MBResponseTimeout = 2000;  ///< Modbus timeout [milliseconds]
```

Dùng ở `ModbusMaster.cpp:800`. Là `static const` trong header ⇒ **không đổi được** nếu không sửa thư viện.

⇒ Mỗi giao dịch Modbus thất bại tốn **2 giây**, không phải 0,5 giây như hằng số gợi ý. Với `BMS_POLL_RETRY = 1` thì mỗi lần đọc hỏng tốn **2 + 0,02 + 2 = 4,02 giây**.

### ✅ Sửa — ba lựa chọn

| Cách | Làm gì | Đánh đổi |
|---|---|---|
| **A (khuyến nghị)** | **Xoá** `BMS_POLL_TIMEOUT_MS` khỏi config + ghi rõ trong comment rằng timeout là 2000 ms do thư viện | Trung thực, 5 phút, nhưng không giảm được timeout |
| **B** | Fork/patch `ModbusMaster.h` thành 500 ms, pin version trong `lib_deps` | Giảm 4× thời gian chờ, nhưng phải duy trì bản fork |
| **C** | Đặt `BMS_POLL_RETRY = 0` cho bus đã ổn định | Bỏ retry — chấp nhận rớt mẫu khi nhiễu |

Với `BMS_UNIT_ID_COUNT = 1` (LỖI 3 đã sửa) thì LỖI 4 gần như không còn ảnh hưởng — chỉ tốn 4 giây khi BMS thật sự không trả lời. Nên **ưu tiên sửa LỖI 3 trước**, LỖI 4 xử lý theo cách A cho khỏi hiểu nhầm.

---

## Giờ mới đến câu hỏi gốc: có cần hiệu chuẩn không?

| Nguồn | Cần hiệu chuẩn? | Lý do |
|---|---|---|
| **JK-BMS** (`primary`) | ❌ Không | Đã hiệu chuẩn tại nhà máy, có shunt tích hợp. Bản đồ thanh ghi **đã kiểm chứng bằng test capture thật**. Chỉ cần sửa **LỖI 1** để code được biên dịch vào |
| **INA226** (`redundant`) | ✅ **Có** | Sai số shunt ±0,5–1 % + sai số điện trở tiếp xúc bu-lông. **Sau khi sửa LỖI 2**. Đo 2 điểm bằng ampe kìm ở ~20 A và ~80 A |
| **DS18B20** (`external-temp`) | ⚠️ Tuỳ | Datasheet ±0,5 °C. Chỉ để cảnh báo quá nhiệt (ngưỡng 60 °C) → **không cần**. Để so sánh chéo với nhiệt độ BMS → **nên**, vì chênh 0,5 °C có thể kích hoạt cảnh báo lệch nguồn giả |
| **SHT31** (môi trường) | ❌ Không | ±0,3 °C / ±2 %RH, quá đủ |
| **MQ-2, rò nước** | ❌ Không | Nhị phân có/không |

## Thứ tự làm

| # | Việc | Công | Mức |
|---|---|---|---|
| 1 | `BMS_UNIT_ID_COUNT` 4 → **1** | 1 phút | 🔴 hiệu quả lớn nhất |
| 2 | `BMS_MODEL` 2 → **3** (config + `platformio.ini`) | 1 phút | 🔴 |
| 3 | Mua shunt **200A/75mV** + sửa 2 macro + build flag + `normalize=false` + sửa log | phần cứng + 10 phút | 🔴 |
| 4 | Xoá `BMS_POLL_TIMEOUT_MS` (hoặc patch thư viện) | 5 phút | 🟡 |
| 5 | Hiệu chuẩn INA226 2 điểm | ~1h | 🟡 sau (3) |
| 6 | Hiệu chuẩn DS18B20 nếu cần | ~30ph | 🟢 |

---

# 14 — Chu kỳ gửi số liệu — có giảm dưới 10 giây được không

## ⚠️ Trước hết: chu kỳ **thực tế hiện nay không phải 10 giây, mà ~13 giây**

Bản 1 của tài liệu này ước tính sàn ~1,03 giây. Con số đó **sai theo hướng lạc quan** vì chưa tính hai điều:
- Đường JK cần **9 giao dịch Modbus** mỗi pin (không phải 1 như JBD)
- Timeout thật là **2000 ms**, không phải 500 ms (LỖI 4)
- `BMS_UNIT_ID_COUNT = 4` nhưng chỉ có **1** BMS (LỖI 3)

## Tính lại từ đầu

### Thời gian truyền Modbus RTU @ 9600 baud, 8N1 (10 bit/byte)

| | Số byte | Thời gian |
|---|---|---|
| Khung yêu cầu | 8 | 8,3 ms |
| Khung đáp, đọc 1 thanh ghi | 7 | 7,3 ms |
| Khung đáp, đọc 2 thanh ghi | 9 | 9,4 ms |
| **Một giao dịch** (chưa tính BMS xử lý) | | **~16–18 ms** |
| Cộng thời gian BMS quay đầu (~10 ms) | | **~26–28 ms** |

### Đường JK cần 9 giao dịch mỗi pin

[`readJkRealtime()`](../iot/firmware-esp32/src/bms/modbus_bms.cpp#L114) đọc **5 khối bắt buộc + 4 khối tuỳ chọn**:

```
mosTemp(1) · voltage(2) · current(2) · batteryTemps(2) · soc(1)    ← bắt buộc
cycleCount(2) · soh(1) · switches(1) · alarm(2)                    ← tuỳ chọn
```

⇒ 4 giao dịch đọc 1 thanh ghi + 5 giao dịch đọc 2 thanh ghi ≈ **240 ms cho một pin có thật**.

### Pin KHÔNG có thật (unitId 2, 3, 4)

`readJkRealtime` thoát ngay ở khối bắt buộc đầu tiên thất bại:
```
readJkRegisters(mosTemp) → timeout 2000 ms
                         → retry: delay(20) + timeout 2000 ms
                         → return false
```
= **4 020 ms mỗi pin vắng mặt**, cộng `delay(20)` giữa các pin trong `modbusReadMultiDrop`.

### Bảng tổng hợp

| Cấu hình | Modbus | + DS18B20 750 ms + INA226 5 ms | **Chu kỳ** |
|---|---|---|---|
| **Hiện tại** (`COUNT=4`, 1 BMS thật, `MODEL=3`) | 240 + 20 + 3×(4 020+20) = **12 380 ms** | | **~13,2 giây** |
| **Hiện tại + `MODEL=2`** (mặc định) — JK không trả lời JBD | 4×(4 020+20) = **16 160 ms** | | **~16,9 giây, KHÔNG có số liệu nào** |
| **Sau khi sửa LỖI 3** (`COUNT=1`) | 240 + 20 = **260 ms** | | **~1,02 giây** |
| Sửa thêm DS18B20 xuống 10-bit | 260 ms | + 187 ms | **~0,45 giây** |

> ⇒ **Sửa một dòng `BMS_UNIT_ID_COUNT` giảm chu kỳ 13 lần** — nhiều hơn mọi tối ưu khác cộng lại.

## Sáu giới hạn, xếp theo mức độ chặn

### 🔴 Giới hạn 1 — Cấu hình sai (LỖI 3 + LỖI 4)

Xem bảng trên. **Sửa được bằng 2 dòng config, không cần viết code.**

### 🔴 Giới hạn 2 — Vật lý firmware sau khi đã sửa cấu hình

| Bước | Thời gian | Bằng chứng |
|---|---|---|
| Modbus JK 1 pin (9 giao dịch) | ~240 ms | `readJkRealtime()` |
| INA226 | ~5 ms | I²C nhanh |
| **DS18B20** | **750 ms — CHẶN LUỒNG** | [ds18b20.cpp:42-43](../iot/firmware-esp32/src/sensor/ds18b20.cpp#L42): `setWaitForConversion(true)` |

**Sàn sau khi sửa LỖI 3: ~1,02 giây.**

Cách hạ tiếp:

| Sửa | Được gì | Mất gì |
|---|---|---|
| `DS18B20_RESOLUTION` 12 → **10** | 750 → **187 ms** | Độ phân giải 0,0625 → 0,25 °C (thừa cho nhiệt độ pin) |
| `setWaitForConversion(false)` + đọc ở chu kỳ sau | 750 → **~0 ms** | Nhiệt độ trễ 1 chu kỳ; phải sửa code |
| `BMS_RS485_BAUD` 9600 → **19200** | Modbus 240 → ~130 ms | JK-BMS phải hỗ trợ (thường có) |
| Bỏ 4 khối tuỳ chọn của JK (cycle/soh/switches/alarm) | 240 → ~135 ms | Mất SOH, chu kỳ, trạng thái sạc, mã lỗi |

### 🔴 Giới hạn 3 — Timestamp chỉ có độ phân giải **giây**

[time_sync.cpp:96](../iot/firmware-esp32/src/net/time_sync.cpp#L96): `strftime(..., "%Y-%m-%dT%H:%M:%SZ", ...)` — **không có mili-giây**.

Trường ms được vá bằng **chỉ số item trong batch** ([payload.cpp:17-28](../iot/firmware-esp32/src/core/payload.cpp#L17-L28)):
```
item 0 → …T08:15:42.000Z
item 1 → …T08:15:42.001Z
item 2 → …T08:15:42.002Z
```

Lý do: khoá chính `sensor_readings` là `(time, battery_asset_id)` — **không gồm** `sensor_source_code`. Ba nguồn cùng một pin phải có `Time` khác nhau.

> ✅ Cơ chế này **đang hoạt động đúng**.

**Nhưng:** nếu chu kỳ < 1 giây, hai batch liên tiếp có thể rơi vào **cùng một giây** → cùng base ISO → item chỉ số 0 của batch sau trùng hệt batch trước → backend đếm là `duplicate_reading` và **bỏ im lặng**.

⇒ **Chu kỳ < 1 giây làm mất dữ liệu mà không báo lỗi.**

### 🟢 Giới hạn 4 — Hạn mức request: **không phải nút thắt**

- Đã đăng nhập: **500 request / 30 giây** mỗi thiết bị ([StandardRateLimitOptions.cs](shared/src/SharedInfrastructure/RateLimiting/StandardRateLimitOptions.cs))
- Thiết bị IoT tính là "đã đăng nhập" (claim `iot:device_id`)
- **MQTT hoàn toàn không đi qua bộ giới hạn HTTP**

### 🟡 Giới hạn 5 — Backend **hardcode 10 giây**

[DeviceLifecycleHandlers.cs:111](services/BatteryService/src/BatteryService.Application/CQRS/Handler/IotDevice/DeviceLifecycleHandlers.cs#L111):
```csharp
PollingIntervalSeconds = 10,     // ← số cứng, KHÔNG đọc từ DB
```

`IotDevice` **không có cột** `PollingIntervalSeconds`. Admin **không đổi được** qua web.

### 🟡 Giới hạn 6 — Dung lượng lưu trữ

`sensor_readings` là hypertable **không có retention, không có nén**.

Số bản ghi mỗi chu kỳ = số pin × số nguồn. Với **1 pin × 3 nguồn = 3 bản ghi**:

| Chu kỳ | Bản ghi/ngày | /năm | Dung lượng/năm (~200 B/dòng) |
|---|---|---|---|
| 10 s | 25 920 | 9,5 triệu | ~1,9 GB |
| **5 s** | 51 840 | 18,9 triệu | **~3,8 GB** |
| 2 s | 129 600 | 47,3 triệu | ~9,5 GB |
| 1 s | 259 200 | 94,6 triệu | ~19 GB |

> Con số này thấp hơn bản 1 vì bản 1 giả định 4 pin. Với **4 pin thật** thì nhân 4: chu kỳ 5 s → ~15 GB/năm/thiết bị.

### 🟡 Giới hạn 7 — Gửi nhanh hơn **không làm cảnh báo nhanh hơn**

`ThresholdCheckBackgroundService` quét mỗi **30 giây** (`AnomalyEngineOptions.ScanIntervalSeconds = 30`).

⇒ Nếu mục tiêu là *phát hiện sự cố nhanh hơn*, hạ `ScanIntervalSeconds` mới có tác dụng — và nó **miễn phí** về lưu trữ.

---

## ✅ Giải pháp

### Bước 0 — Sửa cấu hình trước (**bắt buộc, 2 phút**)

```c
#define BMS_UNIT_ID_COUNT    1     // khớp số BMS THỰC SỰ trên bus
#define BMS_MODEL            3     // JK-BMS
```

Chỉ riêng bước này đã đưa chu kỳ từ ~13,2 s xuống ~1,02 s. **Làm trước khi bàn tới bất kỳ tối ưu nào khác.**

### Bước 1 — Chọn con số

| Mục tiêu | Nên đặt | Ghi chú |
|---|---|---|
| **An toàn, không sửa gì thêm** | **5 giây** | Trên sàn 1,02 s rất nhiều — có biên an toàn khi thêm pin |
| **Biểu đồ mượt** | **2 giây** | Vẫn trên sàn. Cần nén + retention |
| **Nhanh nhất an toàn** | **1,5 giây** | Sát sàn 1,02 s — chỉ nên khi đã đo thực tế |
| **Dưới 1 giây** | ❌ Không | Giới hạn 3 (timestamp) làm mất dữ liệu im lặng |

✅ **ĐÃ CHỐT (Q9): 5 giây.** Hạ tiếp chỉ sau khi đo thực tế bằng `[stats]` log — ba dòng còn lại trong bảng giữ lại làm tham chiếu, không phải lựa chọn đang mở.

### Bước 2 — Thêm cột `PollingIntervalSeconds` (~1,5h, gộp vào T1.1 cùng migration)

```csharp
// IotDevice.cs
public int PollingIntervalSeconds { get; set; } = 10;

// IotDeviceConfiguration.cs
builder.Property(d => d.PollingIntervalSeconds)
       .HasColumnName("polling_interval_seconds")
       .HasDefaultValue(10);

// CreateIotDeviceCommand.cs + UpdateIotDeviceCommand.cs — ValidateAsync()
// Biên [1, 600] để KHỚP clamp của firmware (provision.cpp:133-134).
// Biên rộng hơn là firmware âm thầm clamp lại, admin tưởng đã đổi mà không đổi.
if (PollingIntervalSeconds is < 1 or > 600)
    AddError(response, nameof(PollingIntervalSeconds),
             "Polling interval phải nằm trong [1, 600] giây.");

// DeviceLifecycleHandlers.cs — BỎ số cứng
PollingIntervalSeconds = device.PollingIntervalSeconds,
```

⚠️ Migration thêm cột `NOT NULL` vào bảng đã có dữ liệu → **phải có `defaultValue: 10`** (be.md §14).

### Bước 3 — Nén + retention (~1h, làm cùng lúc nếu hạ xuống ≤ 5 giây)

```sql
ALTER TABLE sensor_readings SET (
  timescaledb.compress,
  timescaledb.compress_segmentby = 'battery_asset_id',
  timescaledb.compress_orderby   = 'time DESC'
);

SELECT add_compression_policy('sensor_readings', INTERVAL '7 days', if_not_exists => TRUE);
SELECT add_retention_policy  ('sensor_readings', INTERVAL '180 days', if_not_exists => TRUE);
```

**An toàn với continuous aggregate:** policy của `sensor_readings_agg_1h` chỉ materialize `[now−3h, now−5m]` ([migration 20260716040506](services/BatteryService/src/BatteryService.Infrastructure/Migrations/20260716040506_AddSensorReadingsContinuousAggregate1h.cs)). Nén chunk cũ hơn **7 ngày** không chồng lấn ✓

**Hai bẫy:**

| Bẫy | Xử lý |
|---|---|
| Chunk đã nén **không insert được** trên TimescaleDB < 2.11 | Kiểm: `SELECT extversion FROM pg_extension WHERE extname='timescaledb';` |
| `Down()` của migration | `remove_compression_policy` + `remove_retention_policy` + `SET (timescaledb.compress = false)`. **Phải test rollback** |

### Nếu mục tiêu là *phát hiện sự cố nhanh hơn*

Đừng hạ chu kỳ. Hạ cái này — **miễn phí**:
```
AnomalyEngine__ScanIntervalSeconds=10     # từ 30 → 10
```

---

# 15 — Rủi ro: đường HTTPS có thể đang chết hoàn toàn

> 🔴 **Mức độ: CAO.** Đây là rủi ro nghiêm trọng nhất phát hiện trong lần rà soát này.

> **Mức độ tin cậy:** suy luận từ code, **chưa chạy được trên phần cứng**. Có cách kiểm chứng 1 phút ở cuối mục.

## Bối cảnh: CA cert nhúng mới được thêm — nhưng chỉ cho MQTT

Commit `39a4b2f` thêm file `src/net/ca_cert_embedded.h`. Comment trong file nói rõ lý do:

> *"`local_queue.cpp` và `mqtt_client.cpp` đều gọi `LittleFS.begin(true)` tức format-nếu-mount-lỗi. **Ảnh do `mklittlefs` tạo không mount được** với thư viện LittleFS trong firmware, nên **phân vùng bị xoá sạch mỗi lần boot** và cuốn theo `ca_cert.pem`. Nhúng vào flash chương trình thì hết phụ thuộc."*

Nhưng bản vá chỉ áp cho **một** trong hai đường TLS:

| Đường | Nguồn CA | Trạng thái |
|---|---|---|
| **MQTT** ([mqtt_client.cpp:71-89](../iot/firmware-esp32/src/net/mqtt_client.cpp#L71-L89)) | **Ưu tiên CA nhúng**, fallback LittleFS | ✅ đã sửa |
| **HTTPS** ([http_client.cpp:259-307](../iot/firmware-esp32/src/net/http_client.cpp#L259-L307)) | **CHỈ LittleFS** (`LittleFS.begin(false)`) | ❌ chưa đụng |

## Chuỗi hệ quả

```
httpConfigureTls()
  → loadCaPemOnce()
      → LittleFS.begin(false)     ← KHÔNG format (cố ý, để không xoá hàng đợi)
          → nếu mount fail → FilesystemUnavailable
  → TLS_ALLOW_INSECURE = 0
      → FAIL CLOSED → trả false
  → s_tlsConfigured = false
      → postJsonInternal() return NGAY, không gửi request nào
```

Củng cố thêm: thư mục `data/` **chỉ có `ca_cert.pem.placeholder`**, không có cert thật, và không được git track.

## Nếu đúng thì hệ quả là gì

| Chức năng | Đường | Trạng thái |
|---|---|---|
| Telemetry | MQTT | ✅ vẫn chạy (CA nhúng) |
| `/provision` | HTTPS | ❌ **chết** — thiết bị không bao giờ lấy được config |
| `/heartbeat` | HTTPS | ❌ **chết** — backend không biết thiết bị còn sống |
| OTA | HTTPS | ❌ **chết** |
| Đẩy bù hàng đợi offline | HTTPS | ❌ **chết** |
| Sự cố môi trường (MQ-2, rò nước) | HTTPS | ❌ **chết** |
| Ambient SHT31 | HTTPS | ❌ **chết** |

Đây là trạng thái **nửa vời nguy hiểm nhất**: nhìn dashboard thấy có số liệu nên tưởng chạy tốt, nhưng thiết bị **chưa từng provision**, không heartbeat, không cập nhật được firmware, và mọi dữ liệu tích trong lúc mất mạng **không bao giờ đẩy được**.

## ✅ Cách kiểm chứng — 1 phút

Cắm USB, mở serial monitor lúc boot:

```
Nếu thấy:  [http] TLS FAIL: LittleFS không mount được. Từ chối kết nối.
       →   ĐÚNG là đang chết.

Nếu thấy:  [http] TLS configured (verify CA)
       →   Không sao, LittleFS vẫn mount được trên máy bạn.
```

Kiểm chéo: `[provision] provisioned, polling=...` có xuất hiện không. Không có dòng đó = chưa provision lần nào.

## ✅ Sửa (~30 phút)

Cho `loadCaPemOnce()` ưu tiên CA nhúng giống `mqtt_client.cpp`:

```cpp
#include "net/ca_cert_embedded.h"

tls::CaLoadStatus loadCaPemOnce() {
  if (s_caLoaded) return tls::CaLoadStatus::Ok;
  if (s_caAttempted && !s_caLoaded) return tls::CaLoadStatus::FileMissing;
  s_caAttempted = true;

  // ⭐ MỚI — ưu tiên CA nhúng, cùng khuôn với mqtt_client.cpp::loadCaCert().
  //    LittleFS không đáng tin (xem ca_cert_embedded.h), nên đường HTTPS
  //    cũng phải có nguồn CA không phụ thuộc filesystem.
  if (kMqttCaCert[0] != '\0' &&
      tls::isLikelyPemCertificate(kMqttCaCert, strlen(kMqttCaCert))) {
    s_caPem = String(kMqttCaCert);
    s_caLoaded = true;
    return tls::CaLoadStatus::Ok;
  }

  // Fallback LittleFS — giữ nguyên phần cũ (cho phép thay CA tại hiện trường).
  if (!LittleFS.begin(false)) return tls::CaLoadStatus::FilesystemUnavailable;
  ...
}
```

> Đặt CA nhúng **trước** LittleFS ở HTTPS, ngược với thứ tự tôi đề xuất trong file kế hoạch (T2.4b). Lý do: MQTT đã chọn thứ tự này và nó đang chạy được — giữ hai đường **cùng một logic** quan trọng hơn là tối ưu khả năng thay CA tại hiện trường, vì hai logic khác nhau chính là thứ đã tạo ra lỗ hổng này.

Đồng thời cập nhật comment sai ở [tls_ca.h:5](../iot/firmware-esp32/src/net/tls_ca.h#L5) — nó nói phần phần cứng nằm ở `tls_ca_device.cpp`, mà file đó **không tồn tại**.

---

# 16 — Tổng hợp việc phát sinh

## 🔴 Ưu tiên cao nhất — làm trước khi cắm phần cứng thật

| # | Việc | Công | Phần |
|---|---|---|---|
| 1 | **`BMS_UNIT_ID_COUNT` 4 → 1** | **1 phút** | [13](#lỗi-3--bms_unit_id_count-lệch-với-battery_mappingh-mới) — chu kỳ 13,2 s → 1,0 s |
| 2 | **`BMS_MODEL` 2 → 3** (config + `platformio.ini`) | **1 phút** | [13](#lỗi-1--bms_model-đang-chọn-nhầm-loại-bms--code-jk-là-code-chết) — kích hoạt code JK |
| 3 | **Đọc serial tìm dòng `[http] TLS`** | **1 phút** | [15](#15--rủi-ro-đường-https-có-thể-đang-chết-hoàn-toàn) — biết HTTPS sống hay chết |
| 4 | Cho `http_client` dùng CA nhúng (nếu bước 3 xác nhận chết) | ~30ph | [15](#15--rủi-ro-đường-https-có-thể-đang-chết-hoàn-toàn) |
| 5 | Mua **shunt 200A/75mV (0,375 mΩ)** | phần cứng | [13](#lỗi-2--cấu-hình-ina226-sai-về-mặt-vật-lý-chip-không-khởi-tạo-được) |
| 5b | Sửa 2 macro INA226 → `0.000375f` / `200.0f` | 1 phút | [13](#lỗi-2--cấu-hình-ina226-sai-về-mặt-vật-lý-chip-không-khởi-tạo-được) |
| 5c | `platformio.ini`: `-DINA226_MINIMAL_SHUNT_OHM=0.0001` | 2 phút | [13](#lỗi-2--cấu-hình-ina226-sai-về-mặt-vật-lý-chip-không-khởi-tạo-được) |
| 5d | `ina226.cpp:49`: thêm tham số thứ ba `false` (tắt normalize) | 2 phút | [13](#lỗi-2--cấu-hình-ina226-sai-về-mặt-vật-lý-chip-không-khởi-tạo-được) |
| 5e | `ina226.cpp:51,57`: `%.3f` → `%.6f` cho shunt (đang in ra `0.000`) | 2 phút | [13](#lỗi-2--cấu-hình-ina226-sai-về-mặt-vật-lý-chip-không-khởi-tạo-được) |
| 6 | Xoá `BMS_POLL_TIMEOUT_MS` (hằng chết) | 5 phút | [13](#lỗi-4--bms_poll_timeout_ms-là-hằng-chết-timeout-thật-là-2000-ms-mới) |
| 7 | **T0.1** — chuẩn hoá case `DeviceCode` + **xoá khối `user esp-2`** | ~2h | [8](#8--topic-mqtt-do-ai-tạo-ở-đâu) |
| 8 | **T0.2** — nối đường credential DB → broker | ~1h | [3](#3--chuỗi-chìa-khoá--cơ-chế-cốt-lõi) |

## 🟡 Trước khi deploy production

| # | Việc | Công | Phần |
|---|---|---|---|
| 9 | **T0.3** — viết `iot/infra/docker-compose.prod.yml` (**vẫn thiếu**) | ~2h | [9](#9--đưa-mqtt-lên-production) |
| 10 | **T0.4** — UID/quyền file `passwd` giữa 2 container | ~1h | [9](#9--đưa-mqtt-lên-production) |
| 11 | **T0.5** — `gen-certs.sh` regen luôn `ca_cert_embedded.h` | ~15ph | [9](#bẫy-mới-phát-hiện-gen-certssh-không-cập-nhật-ca-nhúng) |
| 12 | Quy trình sinh + lưu mật khẩu `backend-bridge` an toàn | ~1h | [9](#9--đưa-mqtt-lên-production) |
| 13 | Hạ `CORE_DEBUG_LEVEL` từ **5** về 1–3 trước khi ship | 1 phút | — |

## 🟢 Cải thiện

| # | Việc | Công | Phần |
|---|---|---|---|
| 14 | ⭐ **Mở rộng type mobile** — hiện trạng thái thiết bị (không đụng backend) | **~1h** | [12](#12--staff-theo-dõi-thiết-bị-iot) |
| 15 | Cột `PollingIntervalSeconds` + bỏ số cứng 10 | ~1,5h | [14](#14--chu-kỳ-gửi-số-liệu--có-giảm-dưới-10-giây-được-không) |
| 16 | Nén + retention cho `sensor_readings` | ~1h | [14](#14--chu-kỳ-gửi-số-liệu--có-giảm-dưới-10-giây-được-không) |
| 17 | QR thành hình + xem lại + in nhãn | ~5h | [10](#10--hiển-thị-qr-trên-ui-thay-vì-in-giấy) |
| 18 | Endpoint `/heartbeats` (**tài liệu nói có nhưng chưa tồn tại**) | ~2h | [12](#12--staff-theo-dõi-thiết-bị-iot) |
| 19 | Danh sách + chi tiết thiết bị cho Staff (web + mobile) | ~17h | [12](#12--staff-theo-dõi-thiết-bị-iot) |
| 20 | **T3.3** — captive portal | ~6h | [11](#11--trang-cấu-hình-thiết-bị--đã-có-chưa) |
| 21 | **T1.7** — sửa `Channel` dùng nhầm làm `SensorSourceCode` | ~30ph | file kế hoạch |
| 22 | **Q6** — tách endpoint `rotate-mqtt` khỏi `rotate-key` (BE) + nút riêng trên UI (FE) | ~2h | [Quyết định Q6](#quyết-định-đã-chốt-2026-08-07) |
| 23 | **Q5** — `batteryMappings` runtime (**T4.1**, đã chuyển từ tuỳ chọn sang trong phạm vi) | ~3h | [Quyết định Q5](#quyết-định-đã-chốt-2026-08-07) |

## 🎯 Nếu chỉ làm được ba việc hôm nay

1. **`BMS_UNIT_ID_COUNT = 1`** — 1 phút, chu kỳ nhanh gấp **13 lần**
2. **`BMS_MODEL = 3`** — 1 phút, kích hoạt code JK đã viết + đã test
3. **Đọc serial tìm `[http] TLS`** — 1 phút, biết HTTPS còn sống không

Ba phút, và bạn biết chắc hệ thống đang ở đâu.

## Rủi ro còn treo

| Rủi ro | Mức | Xử lý |
|---|---|---|
| **Đường HTTPS chết** (CA nhúng chỉ áp cho MQTT) | **Cao** | [Phần 15](#15--rủi-ro-đường-https-có-thể-đang-chết-hoàn-toàn) — kiểm 1 phút bằng serial |
| WiFi khách là **WPA2-Enterprise** | **Cao** | `WiFi.begin(ssid,pass)` không hỗ trợ. **Kiểm tra trước khi demo** — triệu chứng duy nhất là `[wifi] reconnecting...` lặp vô hạn |
| ACL vá tay cho `ESP-2` chồng với fix T0.1 | Trung bình | Xoá khối `user esp-2` **cùng lúc** với T0.1, đừng để hai luật song song |
| `gen-certs.sh` không regen CA nhúng | Trung bình | Chạy lại script = broker CA mới, firmware CA cũ → `-9984` |
| WiFi 5 GHz-only hoặc mesh chung SSID | Trung bình | ESP32-S3 chỉ bắt 2,4 GHz. Trang setup phải lọc + cảnh báo |
| Backend chết trong lúc device publish MQTT | Trung bình | QoS 0 + không retain ⇒ telemetry lúc đó **mất thật** |
| Hàng đợi LittleFS đầy khi offline dài ngày | Thấp | Đo dung lượng thực; nới chu kỳ khi offline |

---

# 17 — Bốn nợ kỹ thuật lộ ra khi chạy thật (08/08/2026)

Phần này ghi lại **bốn lỗi tìm thấy trong buổi kiểm thử end-to-end đầu tiên** — chạy hoàn toàn bằng
dòng lệnh, không cần phần cứng.

**Cả bốn KHÔNG do Sprint IoT-3 tạo ra.** `git log -S` trên chính dòng code lỗi cho thấy chúng ra đời
ở `e7b2fb7a` (01/08), `7e350fe1` (05/08), `8e71dfb7` (#654 — Sprint Bonus, 16/07) và `82b56569`
(16/07). Sprint IoT-3 chỉ **làm chúng lộ ra**, vì lần đầu tiên hệ thống có: thiết bị tạo lúc chạy
(IOT3-25/26/42), uplink MQTT đi tới nơi (IOT3-14/39), và trang để nhìn thấy dữ liệu (IOT3-57/58/67).

Điểm chung khiến chúng sống sót qua **657 unit test**:

| Nợ | Chỉ lộ ra khi |
|---|---|
| #1 | đi qua **bind mount thật** — test dùng file trên đĩa |
| #2 | payload **sai tên trường** — test luôn dựng payload đúng |
| #3 | có **hai lượt quét liên tiếp** trên cùng dữ liệu |
| #4 | **DB sạch** — có dữ liệu cũ là lỗi bị che hoàn toàn |

Bản đầy đủ (kèm số liệu đo và câu SQL tái hiện): `docs/non-obvious-decisions.md` §"Bốn nợ kỹ thuật".
Kịch bản chạy lại: `iot-quy-trinh-test-khong-can-phan-cung.md` · `./iot-test-lai.sh --reset`.
Task sửa: **IOT3-106** (Phase M của Sprint IoT-3, `overall.md` §17).

---

## 17.1 — `passwd` không tự nạp lại ⇒ thiết bị mới không đăng nhập được

**Triệu chứng.** Tạo thiết bị trên UI → backend log `đã ghi N bản ghi` → file `passwd` đúng, định
dạng `$7$` chuẩn → container mosquitto `grep` cũng thấy dòng đó. Nhưng thiết bị nối vào nhận
`Connection Refused: not authorised`. **Không có dòng lỗi nào ở bất kỳ đâu.**

**Đo được.**

| | Giá trị |
|---|---|
| mtime host | `1786187127` |
| mtime container thấy | `1786186407` — **chậm 720 giây** |
| Số lần vòng `passwd-watch` in ra | **0** |
| Sau `docker exec solar-mosquitto kill -HUP 1` | đăng nhập được **ngay** |

**Nguyên nhân.** `MqttPasswordFileSyncService.WriteAtomicallyAsync` ghi file tạm rồi
`File.Move(temp, path, overwrite: true)` — **đổi tên**, tạo inode mới. Đó là lựa chọn ĐÚNG: broker
không bao giờ đọc phải file ghi dở. Nhưng compose mount **một file lẻ**:

```yaml
- ./infra/mqtt/mosquitto/passwd:/mosquitto/config/passwd:ro
```

Nội dung theo kịp, **mtime thì không**. Vòng `passwd-watch` so `stat -c %Y` → thấy không đổi → không
bao giờ gửi SIGHUP → mosquitto giữ nguyên bảng mật khẩu nạp lúc khởi động.

**Cách sửa.** Mount **thư mục** thay vì file lẻ, ở **cả ba** file compose: `backend/docker-compose.yml`,
`backend/docker-compose.prod.yml`, `iot/infra/docker-compose.prod.yml`.

> ⚠️ **Trên Linux nghi TỆ HƠN — chưa đo được.** Bind mount file lẻ gắn theo **inode**; sau `File.Move`
> inode đổi nên container có thể thấy **cả nội dung lẫn mtime đều cũ**. Đây là suy luận từ hành vi đã
> biết của Docker, **chưa phải số liệu** — phải kiểm trên VPS trước khi ship.
>
> ❌ **Đừng** đổi sang ghi in-place để "sửa nhanh": làm vậy là bỏ mất tính nguyên tử vốn đang bảo vệ
> đúng chỗ — broker có thể đọc phải file ghi dở và **từ chối cả file**, mất quyền của mọi thiết bị.

**Chữa cháy:** `docker exec solar-mosquitto kill -HUP 1` sau mỗi lần tạo/xoay thiết bị.

---

## 17.2 — Telemetry rơi im lặng khi payload sai tên mảng

**Triệu chứng.** Publish OK, broker chuyển tin OK, cầu nối chạy OK, DB trống, **không log gì**.

**Nguyên nhân.** `MqttBridgeBackgroundService.DispatchTelemetryAsync` deserialize payload thành
`BatchIngestSensorReadingsCommand` rồi duyệt `cmd.Items`. Mảng **phải tên `items`**. Dùng `readings`
thì `System.Text.Json` không khớp tên nào, deserialize **thành công** với `Items` rỗng — không ngoại
lệ, không cảnh báo, không bản ghi.

Payload đúng:

```json
{"items":[{"time":"…Z","batteryAssetSerial":"BAT-2026-001","voltage":12.6,
           "current":1.5,"temperature":25.3,"socPercent":80,"sensorSourceCode":"primary"}]}
```

**Cách sửa.** Một dòng sau khi deserialize:

```csharp
if (cmd.Items.Count == 0)
{
    _logger.LogWarning(
        "MQTT telemetry từ {DeviceCode} không có mục nào — payload sai tên trường? "
        + "Mảng phải tên `items`. Payload: {Payload}", device.DeviceCode, payload);
    return;
}
```

Rà lại cả `DispatchHeartbeatAsync` vì cùng khuôn.

---

## 17.3 — `PromotedToAlertId` không bao giờ được gán

**Đo được.** `SELECT count(*) FILTER (WHERE promoted_to_alert_id IS NOT NULL) FROM noise_breach_events`
→ **0/11** trên toàn bảng, dù đã sinh alert từ đúng chuỗi breach đó.

**Nguyên nhân.** `AnomalyDetectionService.cs:133` gác lời gọi bằng `if (recordedBreach is not null)`.
Nhưng `ShouldSuppressByNoiseAsync` trả `recorded = null` khi `alreadyRecorded == true`, tức ở **lượt
quét LẠI** — mà alert của đường chống nhiễu **chỉ nổ ở lượt quét lại**: lượt đầu
`effectiveCount = breachCount + 1` chưa đạt `NoiseSuppressionCount` nên luôn bị chặn.

Hai điều kiện **loại trừ nhau**: lượt nào có `recordedBreach` thì không nổ alert; lượt nào nổ alert
thì nó đã null.

**Hậu quả.** Mất dấu vết kiểm toán *"alert này nổ từ chuỗi vi phạm nào"*. Nặng hơn: XML doc ghi
*"retention sẽ giữ các row đã promote"* — không row nào được đánh dấu ⇒ retention sẽ **xoá sạch chuỗi
breach làm bằng chứng cho alert**.

**Cách sửa.** Bỏ gác theo `recordedBreach`, đổi `pendingBreach` thành nullable — hàm đã tự truy vấn
cả chuỗi từ DB:

```csharp
if (threshold.NoiseSuppressionEnabled && threshold.NoiseSuppressionCount > 1)
{
    await PromoteBreachChainAsync(
        reading.BatteryAssetId, anomaly.Type, threshold, alert.Id,
        recordedBreach, cancellationToken);   // nhận null
}
```

---

## 17.4 — Dedup alert mù với alert do CHÍNH lượt quét đó tạo

**Đo được (DB sạch).** 6 reading quá áp gửi cách nhau 2 giây → **5 alert `status=1` (Open)**, cùng
`battery_asset_id`, cùng `anomaly_type`, trong 9 giây, `merged_into_alert_id` đều NULL.

**Nguyên nhân.** `FindActiveAlertToMergeAsync` truy vấn **DB** bằng `.FirstOrDefaultAsync()`. Các
alert vừa `AddAsync` trong cùng lượt quét còn **pending trong change tracker**, chưa `SaveChanges`,
nên truy vấn không thấy. Mỗi reading vì thế tự tạo một alert Open mới.

Trớ trêu: `ShouldSuppressByNoiseAsync` **đã lường trước đúng cơ chế này** cho `noise_breach_events`,
có ghi chú ngay trong file — *"row pending không được DB đếm"*. `FindActiveAlertToMergeAsync` thì không.

**Vì sao lâu nay không lộ.** Cần có sẵn một alert cùng loại **đã persisted** và còn trong
`DedupWindowEndUtc` (30 phút) thì dedup mới chạy đúng.

- **DB còn alert cũ** → tìm thấy cha ngay từ reading đầu → mọi alert sau đều `Merged`. Lần đo lúc
  12:08 cho ra **12 alert, tất cả Merged** vào một cha từ 11:44 — nhìn như dedup hoàn hảo.
- **DB sạch** → reading đầu tạo alert mới nhưng còn pending → reading thứ hai không thấy → tạo alert
  Open thứ hai. Và cứ thế.

**Nghịch lý: DB càng sạch, lỗi càng lộ.** Đó là lý do nó sống sót qua cả 657 unit test.

**Hậu quả.** Chống nhiễu chặn được *báo động giả* (vi phạm thoáng qua do nhiễu đo); nó **KHÔNG** chặn
*báo động trùng* (một sự cố kéo dài). Hai việc khác nhau. Một pin lỗi thật — điện áp vọt rồi giữ
nguyên — sẽ đẩy 5–10 alert Open giống hệt nhau vào hàng đợi trực, và cảnh báo thật sẽ chìm trong đó.

**Cách sửa.** Cho `FindActiveAlertToMergeAsync` nhìn cả phần chưa lưu: giữ một
`Dictionary<(Guid assetId, AnomalyTypeEnum type), Alert>` cục bộ trong phạm vi một lượt quét, tra nó
**trước** khi hỏi DB.

> ❌ **Đừng** dùng `SaveChangesAsync` sau mỗi alert: sửa được triệu chứng nhưng đánh đổi N round-trip
> mỗi lượt quét và làm mất tính nguyên tử của cả lượt.

**Cách đo lại:** xoá sạch `alerts` + `noise_breach_events` của loại đang thử, gửi ≥ 6 reading vi phạm
cách nhau 2 giây, đếm `status=1`. Nhiều hơn **1** là lỗi còn nguyên. Chi tiết: §8.4 của
`iot-quy-trinh-test-khong-can-phan-cung.md`.

## 17.5 — Ô "Gửi command" liệt kê 5 loại lệnh firmware không hiểu loại nào

**Triệu chứng.** Admin chọn `reboot` (hoặc `ota`, `sample-now`, `calibrate`, `set-config`) → backend
trả **202**, toast xanh → thiết bị nhận đúng topic → trả `status: "unknown"` → **thiết bị không làm
gì**. Ack chỉ vào log backend ở mức `Information` nên chìm nghỉm.

**Gốc.** `classifyType` (`iot/firmware-esp32/src/cmd/cmd_logic.cpp:33-39`) chỉ nhận **ba** tên:
`set_interval` · `trigger_ota` · `request_heartbeat`. Danh sách 5 tên kia chép từ XML doc của
`IotDeviceCommandPayloadDto` — tài liệu đó chưa bao giờ khớp firmware, rồi frontend và
`docs/api-battery.md` chép lại. **Ba nơi cùng sai, không nơi nào là nguồn sự thật.**

**Đã sửa.** Ba tên đúng ở cả ba nơi, đều trỏ về `classifyType`; ack `failed`/`rejected`/`unknown`
nâng lên `LogWarning`; và (09/08) bỏ hẳn ô JSON khỏi đường đi thường ngày — chọn lệnh bằng thẻ có
mô tả, nhịp lấy mẫu bằng nút nhanh + ô số chặn dải [1, 3600], JSON dời vào "Tuỳ chọn nâng cao".

**Hai điều dễ hiểu nhầm, nay hiện thẳng trên form:**

- `set_interval` **chỉ đổi RAM** — reboot là mất (`main.cpp:672` không gọi `nvsPutInt32`).
- Thiết bị **offline thì lệnh mất luôn**, không nằm chờ: `PubSubClient.cpp:220` bật cờ Clean Session
  vô điều kiện nên broker không giữ lệnh hộ. Vẫn 202, vẫn toast xanh.

**Còn thiếu.** Ack **chưa hiển thị lên UI** — muốn biết lệnh có chạy hay không vẫn phải
`docker logs solar-batteryservice | grep cmd/ack`.

---

# Ba câu tóm gọn nhất

**1. Hai kênh, hai chìa khoá.**
HTTPS dùng `apiKey` — lo việc cần chắc chắn (provision, heartbeat, OTA, đẩy bù). MQTT dùng username/password riêng — lo luồng số liệu liên tục và lệnh điều khiển. Backend là bên duy nhất nối được cả hai.

**2. Nạp tay một cặp chìa, cặp còn lại backend đưa.**
Kỹ thuật viên chỉ nạp `deviceCode` + `apiKey` — qua **cổng cấu hình WiFi** của thiết bị, hoặc `set devcode` / `set apikey` qua cáp. Dùng cặp đó gọi `/provision`, backend trả về mọi thứ còn lại, kể cả **username/mật khẩu MQTT**. ✅ Đã xong ở IOT3-42 — **không ai gõ tay khoá MQTT**.

**3. Người chỉ chạm vào thiết bị đúng hai lúc:** khi lắp đặt, và khi khách đổi WiFi.
Mọi thay đổi khác — chu kỳ đo, thêm pin, đổi broker, xoay khoá, cập nhật firmware — đều làm trên web.

---

# Phần nào đã có, phần nào chưa

*(cập nhật sau khi pull `dev` @ `bea80a9`)*

| Cơ chế | Trạng thái |
|---|---|
| REST API provision/heartbeat/OTA/calibration | ✅ Đã có, chạy được |
| Backend áp hệ số hiệu chuẩn khi ingest | ✅ Đã có |
| MQTT bridge nghe 4 wildcard | ✅ Đã có |
| LWT → đánh dấu offline + tạo alert | ✅ Đã có |
| Lệnh downlink + ack | ✅ Đã có |
| Hàng đợi offline + đẩy bù chống trùng | ✅ Đã có |
| Vá timestamp ms để 3 nguồn không đụng khoá chính | ✅ Đã có |
| OTA + rollback | ✅ Đã có |
| Staff tra cứu thiết bị theo mã + calibration (web + mobile) | ✅ Đã có |
| Thư viện QR ở frontend (`qrcode.react`) | ✅ Đã cài, **chưa dùng để render hình** |
| **Giải mã JK-BMS Modbus V1.1 (32 bit, sparse block)** | ✅ **MỚI** — có test capture phần cứng thật |
| **CA nhúng cho MQTT** | ✅ **MỚI** — `ca_cert_embedded.h` |
| Vòng lặp chính chạy trong task FreeRTOS riêng | ✅ **MỚI** |
| 7 env chẩn đoán phần cứng + 4 example (`jk-probe`, `modbus-scan`…) | ✅ **MỚI** |
| 10 bộ test mới (`test_tls_ca`, `test_retry_gate`, `test_mqtt_qos_contract`…) | ✅ **MỚI** |
| Tham số `qos` giả trong `publishWithStats` | ✅ **ĐÃ BỎ** (GH-746) + có test chặn |
| **CA nhúng cho HTTPS** | ❌ **CHƯA** — rủi ro nghiêm trọng, xem Phần 15 |
| `BMS_MODEL` trỏ đúng JK | ❌ **Vẫn là 2 (JBD)** — code JK bị gate chết |
| `BMS_UNIT_ID_COUNT` khớp số BMS thật | ❌ **Vẫn là 4** — chu kỳ đội lên 13× |
| `BMS_POLL_TIMEOUT_MS` có tác dụng | ❌ **Hằng chết** — timeout thật 2000 ms |
| Cấu hình INA226 đúng vật lý | ❌ **Vẫn sai** — `ERR_SHUNTVOLTAGE_HIGH`, chip không khởi tạo được |
| Gỡ chặn `INA226_MINIMAL_SHUNT_OHM` (shunt sub-mΩ) | ❌ Chưa — thiếu là `ERR_SHUNT_LOW` dù shunt đúng |
| `normalize=false` cho `setMaxCurrentShunt` | ❌ Chưa — thiếu là `ERR_NORMALIZE_FAILED` ở mọi `maxCurrent` > 163,8 A |
| Chuẩn hoá case DeviceCode ở ranh giới MQTT | ❌ **Bug đang sống** + đã có workaround vá tay `user esp-2` |
| `MqttPasswordFileSyncService` | ⚠️ Code có, **chưa từng chạy** (thiếu `Mqtt__PasswordFilePath`) |
| `iot/infra/docker-compose.prod.yml` | ❌ **Vẫn không tồn tại** dù prod compose trỏ tới |
| `gen-certs.sh` regen CA nhúng | ❌ Chưa — bẫy im lặng |
| Endpoint `GET .../{id}/heartbeats` | ❌ **Không tồn tại** dù XML doc nói có |
| Cột `PollingIntervalSeconds` | ❌ Chưa có — backend hardcode 10 |
| Nén + retention cho `sensor_readings` | ❌ Chưa có |
| Provision trả cấu hình MQTT | ❌ Chưa có — **T1.2/T1.3** |
| Firmware đọc cấu hình MQTT từ NVS | ❌ Chưa có — **T2.3/T2.5** |
| WiFi cấu hình được tại chỗ | ❌ Chưa có — **T3.1/T3.2** |
| Trang setup qua điện thoại (captive portal) | ❌ **Vẫn 0 dòng** — **T3.3** |
| Firmware đọc `batteryMappings` từ provision | ❌ Chưa có — **T4.1** (Q5 chốt: **làm**, không còn tuỳ chọn) |
| Danh sách thiết bị cho Staff | ❌ Chưa có |
| Quét QR trên mobile (`expo-camera`) | ❌ Chưa cài |
| `tls_ca_device.cpp` (mà `tls_ca.h` nhắc tới) | ❌ **Không tồn tại** — comment sai |

Chi tiết từng task, ước lượng công, thứ tự làm và rủi ro: xem [iot-zero-touch-wifi-khach.md](iot-zero-touch-wifi-khach.md).

---

# Phụ lục — Calibration

Calibration **không** nằm trong Phương án A vì nó đã hoạt động đầy đủ và **hoàn toàn ở backend** — firmware không có một dòng calibration nào (đúng thiết kế).

**Công thức:** `giá_trị_thật = giá_trị_đo × Scale + Offset`

**Luồng:** đo đối chứng bằng đồng hồ chuẩn tại 2 điểm → tính Scale/Offset → `POST /api/iot-devices/{deviceId}/calibrations` → backend áp lúc nhận số liệu ([BatchIngestSensorReadingsCommandHandler.cs:282-284](services/BatteryService/src/BatteryService.Application/CQRS/Handler/SensorReading/BatchIngestSensorReadingsCommandHandler.cs#L282-L284)).

**Ví dụ tính:**

| Điểm | Đo được (raw) | Chuẩn (thật) |
|---|---|---|
| A | 12,45 V | 12,60 V |
| B | 11,20 V | 11,34 V |

```
Scale  = (12,60 − 11,34) / (12,45 − 11,20) = 1,008
Offset = 12,60 − (12,45 × 1,008)           = 0,0504
Kiểm B: 11,20 × 1,008 + 0,0504 = 11,3400 ✓
```

**Quy tắc tra cứu** (2 tầng, ưu tiên cụ thể hơn):
1. `(channel, batteryAssetId)` — riêng cho pin này
2. `(channel, null)` — chung cho cả thiết bị
3. Không có → giữ nguyên số thô

Cache Redis **TTL 5 phút** — sửa trên web thì tối đa 5 phút sau mới có hiệu lực.

**Khi nào cần calibrate:** xem bảng đầy đủ ở [Phần 13](#giờ-mới-đến-câu-hỏi-gốc-có-cần-hiệu-chuẩn-không).

Với `USE_MOCK_BMS=1` thì calibration **vô nghĩa** — số giả không có gì để đối chứng.
