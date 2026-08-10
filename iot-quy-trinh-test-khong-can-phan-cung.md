# Quy trình test IoT end-to-end — không cần phần cứng

> Ghi lại nguyên buổi kiểm thử ngày **2026-08-08**, chạy được từ đầu tới cuối bằng dòng lệnh.
> Giả lập một ESP32 bằng `mosquitto_pub`/`mosquitto_sub` + `curl`, đi trọn đường
> **HTTPS provision → MQTT → cầu nối → TimescaleDB → cảnh báo → LWT**.
>
> Dùng để: hồi quy sau mỗi lần sửa MQTT · dựng lại môi trường demo · huấn luyện người mới ·
> khoanh vùng khi phần cứng thật không gửi được số liệu (chạy quy trình này trước, nếu nó xanh thì
> lỗi nằm ở firmware chứ không phải backend).
>
> Tài liệu liên quan: `iot-co-che-hoat-dong.md` (cơ chế) · `docs/non-obvious-decisions.md`
> (§"Ba nợ kỹ thuật") · `docs/runbooks/11-mqtt-bridge-password.md`

---

## 0. Bảng tra — mọi con số cần trong tài liệu này

| Enum | Giá trị |
|---|---|
| `IotDeviceStatusEnum` | 1 Pending · **2 Active** · **3 Offline** · 4 Disabled · 5 Decommissioned |
| `AlertStatusEnum` | **1 Open** · 2 Acknowledged · **3 Merged** · 4 Resolved |
| `AlertSeverityEnum` | 1 Info · **2 Warning** · **3 Critical** |
| `AnomalyTypeEnum` | **1 Overheat** · **2 Overvoltage** · 3 Undervoltage · 4 LowSoc · **7 DeviceOffline** · 14 EnvironmentalIncident |

| Cổng (dev, máy này) | Giá trị |
|---|---|
| BatteryService | `localhost:4006` |
| Mosquitto plain | `21883` → 1883 trong container |
| Mosquitto TLS | `28883` → 8883 |
| Trong network Docker | `mosquitto:1883` |

> ⚠️ **`mqttBrokerHost` mà provision trả về là `mosquitto`** — hostname nội bộ Docker. Thiết bị thật
> ngoài LAN **không phân giải được**, phải dùng IP LAN của máy chủ. `MqttBrokerEndpointProvider.Resolve()`
> đang trả thẳng `Mqtt:Host`, tức host mà *backend* dùng để nối broker, không phải host mà *thiết bị*
> cần. Chưa có `Mqtt__AdvertisedHost` — đây là lỗ hổng đã biết, không nằm trong Sprint IoT-3.

---

## 1. Biến dùng chung — đặt một lần cho cả phiên

```bash
cd /Users/alex/Documents/capstone/backend

DEVICE=ESP-TEST-2                 # deviceCode, DB lưu CHỮ HOA
MQTT_USER=esp-test-2              # = deviceCode chữ thường; ACL dùng làm %u
LAN_IP=$(ipconfig getifaddr en0 || ipconfig getifaddr en1)   # macOS
BE=http://localhost:4006

echo "LAN_IP=$LAN_IP"
```

Lấy credential thẳng từ DB — khỏi copy từ dialog trên UI (chuỗi bị cắt trong ô input):

```bash
eval $(docker exec solar-postgres psql -U postgres -d battery_db -tAc \
  "SELECT 'AK='||api_key_plaintext||' DP='||mqtt_password_plaintext \
   FROM iot_devices WHERE device_code='$DEVICE';")

[ -n "$AK" ] && [ -n "$DP" ] && echo "✔ có apiKey + mật khẩu MQTT" || echo "✘ thiếu — xem §2"
```

`DP` rỗng ⇒ thiết bị tạo trước IOT3-25 (chưa lưu mật khẩu dạng đọc lại được). Chạy
`POST /api/admin/iot-devices/{id}/rotate-mqtt` để cấp mới — thiết bị tự lành qua `/provision`,
**không cần ra hiện trường**. Đừng dùng `rotate-key`: cái đó đổi cả apiKey và bắt buộc nạp lại tại chỗ.

---

## 2. Giai đoạn 0 — hạ tầng (một lần cho cả hệ thống)

### 2.1 Bật MQTT — sửa `.env`, KHÔNG phải `.env.Docker`

Đây là chỗ mất thời gian nhất, và không có gì báo lỗi khi làm sai.

`docker-compose.yml` khai batteryservice với **cả hai**: `env_file: .env.Docker` **và**
`environment: Mqtt__Host: ${Mqtt__Host:-mosquitto}`. Theo đặc tả Compose, `environment:` **thắng**
`env_file:`; còn `${Mqtt__Host}` thì Compose nội suy từ **`.env`** (file mặc định). Kết quả: sửa
`.env.Docker` cho nhóm `Mqtt__*` là **không có tác dụng gì**.

```bash
# Mật khẩu bridge — nếu chưa có, xem §2.2
PW=$(grep "^Mqtt__Password=" .env.Docker | cut -d= -f2-)

sed -i '' "s|^Mqtt__Enabled=.*|Mqtt__Enabled=true|"   .env
sed -i '' "s|^Mqtt__Host=.*|Mqtt__Host=mosquitto|"    .env
sed -i '' "s|^Mqtt__Password=.*|Mqtt__Password=$PW|"  .env
```

Kiểm bằng chính Compose thay vì đoán. **Không dùng `grep -A20`** — khối `environment:` sắp theo bảng
chữ cái nên `Mqtt__` nằm sau hàng chục dòng `ASPNETCORE_`/`ConnectionStrings__`/`Jwt__`:

```bash
docker compose config \
  | awk '/^  batteryservice:$/{f=1} f&&/^  [a-z0-9_-]+:$/&&!/batteryservice/{f=0} f&&/Mqtt__/'
```

Phải thấy `Mqtt__Enabled: "true"` và `Mqtt__Host: mosquitto`.

### 2.2 Mật khẩu tài khoản cầu nối `backend-bridge`

⚠️ **`bootstrap.sh` KHÔNG dùng được nữa** sau khi BatteryService đã đồng bộ lần đầu. File `passwd`
lúc đó có cặp mốc dạng comment (`# >>> BatteryService managed devices …`), mà `mosquitto_passwd`
phân tích **mọi dòng** thành `user:hash` nên báo:

```
Error: Corrupt password file at line 2.
```

**Broker thì chấp nhận comment bình thường** — đã đo trực tiếp: cả user trước mốc lẫn sau mốc đều
đăng nhập được, sai mật khẩu vẫn bị từ chối đúng. Chỉ có *tiện ích* là không chịu.

Cách đúng — băm trong file **cô lập** rồi ghép vào, giữ nguyên mốc và các dòng thiết bị:

```bash
BRIDGE_PASS="$(LC_ALL=C tr -dc 'A-Za-z0-9' </dev/urandom | head -c 32)"
TMP="$(mktemp -d)"

docker run --rm -v "$TMP:/c" eclipse-mosquitto:2.0 sh -c \
  "touch /c/pw && mosquitto_passwd -b /c/pw backend-bridge '$BRIDGE_PASS'"

python3 - infra/mqtt/mosquitto/passwd "$TMP/pw" <<'PY'
import sys, pathlib
target, src = map(pathlib.Path, sys.argv[1:3])
newline = next(l for l in src.read_text().splitlines() if l.startswith("backend-bridge:"))
lines = target.read_text().splitlines()
hit = [i for i, l in enumerate(lines) if l.startswith("backend-bridge:")]
assert len(hit) == 1, f"tìm thấy {len(hit)} dòng backend-bridge — dừng"
lines[hit[0]] = newline
target.write_text("\n".join(lines) + "\n")
print(f"✔ đã thay dòng {hit[0]+1}, giữ nguyên mốc + các thiết bị")
PY

rm -rf "$TMP"
echo "Mqtt__Password=$BRIDGE_PASS"     # dán vào .env (§2.1)
```

Kiểm mật khẩu có khớp hash không — đừng đợi tới lúc bridge báo lỗi:

```bash
docker exec solar-mosquitto mosquitto_pub -h 127.0.0.1 -p 1883 \
  -u backend-bridge -P "$BRIDGE_PASS" -t probe/check -m ok -q 1 \
  && echo "✔ khớp" || echo "✘ không khớp"
```

### 2.3 Khởi động

```bash
docker compose --profile mqtt up -d --force-recreate mosquitto batteryservice
```

`--force-recreate` là **bắt buộc**: biến môi trường được cố định lúc **tạo** container, `up -d` trên
container đã có sẽ giữ nguyên giá trị cũ.

### 2.4 Xác nhận Giai đoạn 0

```bash
sleep 12
docker logs solar-batteryservice 2>&1 | grep -o '"Message":"MQTT bridge[^"]*"' | tail -1
docker logs solar-batteryservice 2>&1 | grep -o '"Message":"MqttPasswordFileSync[^"]*"' | tail -1 | cut -c1-120
```

Phải thấy:

```
"MQTT bridge connected to broker, 4 subscriptions (mosquitto:1883)"
"MqttPasswordFileSync: đã ghi N bản ghi thiết bị vào /mosquitto-config/passwd."
```

Thấy `MQTT bridge disabled (Mqtt:Enabled=false)` ⇒ quay lại §2.1, gần như chắc chắn sửa nhầm
`.env.Docker`.

### 2.5 ⚠️ Nợ #1 — `passwd` không tự nạp lại

**Sau MỌI lần tạo/xoay thiết bị, phải chạy:**

```bash
docker exec solar-mosquitto kill -HUP 1
```

Không làm thì thiết bị mới nhận `Connection Refused: not authorised` dù file `passwd` hoàn toàn đúng
và container cũng đọc đúng nội dung. Nguyên nhân đầy đủ ở `docs/non-obvious-decisions.md` §"Ba nợ
kỹ thuật" mục 1 (tóm tắt: `File.Move` đổi inode + mount **file lẻ** ⇒ mtime trong container không
đổi ⇒ vòng `passwd-watch` không bao giờ bắn SIGHUP).

---

## 3. Giai đoạn 1 — tạo hồ sơ thiết bị

Trên web admin: **IoT Devices → Thêm**, điền `deviceCode` (in hoa), tên, chọn Site. Dialog hiện QR
thành ảnh + API key + 6 trường MQTT, có nút **In nhãn 50×30 mm**.

Hoặc bằng API:

```bash
TK="<access token admin>"      # exp ~1 giờ; hết hạn thì đăng nhập lại

curl -s -X POST "$BE/api/admin/iot-devices" \
  -H "Content-Type: application/json" -H "Authorization: Bearer $TK" \
  -d "{\"deviceCode\":\"$DEVICE\",\"displayName\":\"Thiết bị test\",\"siteId\":\"<site-guid>\"}" \
  | python3 -m json.tool
```

Rồi **§2.5** (SIGHUP), rồi quay lại **§1** lấy `AK`/`DP`.

> Thông tin này **xem lại được** bất cứ lúc nào qua nút *Xem lại thông tin* — trừ mật khẩu MQTT của
> thiết bị tạo trước IOT3-25. Đừng xoay khoá chỉ vì lỡ đóng tab.

---

## 4. Test 1 — Provision (HTTPS)

```bash
curl -s -X POST "$BE/api/iot-devices/provision" \
  -H "Content-Type: application/json" \
  -H "X-Api-Key: $AK" -H "X-Device-Code: $DEVICE" \
  -d "{\"firmwareVersion\":\"0.1.0\",\"hardwareRevision\":\"v1.0\",\"deviceTimestamp\":\"$(date -u +%Y-%m-%dT%H:%M:%SZ)\"}" \
  | python3 -m json.tool
```

**Đạt khi:** `isSuccess: true`, `statusCode: 200`, và có **đủ sáu** trường
`mqttBrokerHost/Port/UseTls/TopicPrefix/Username/Password` + mảng `batteryMappings`.

Trạng thái thiết bị `Pending → Active`:

```bash
docker exec solar-postgres psql -U postgres -d battery_db -tAc \
  "SELECT 'status=' || status FROM iot_devices WHERE device_code='$DEVICE';"   # phải = 2
```

| Mã lỗi | Nghĩa |
|---|---|
| 401 | apiKey sai — hoặc quên thay `<token>` trong lệnh |
| 403 | `X-Device-Code` không khớp với key |
| 409 | thiết bị Disabled/Decommissioned |
| 422 | đồng hồ lệch > 5 phút — đồng bộ NTP rồi thử lại |

---

## 5. Test 2 — Telemetry (MQTT uplink)

⚠️ **Mảng phải tên `items`.** Dùng `readings` thì `System.Text.Json` không khớp tên nào, deserialize
**thành công** với `Items` rỗng — không lỗi, không log, không bản ghi. Đây là **nợ #2**.

⚠️ Topic phải **chữ thường**, khớp `mqttTopicPrefix` provision trả về. Gõ chữ hoa là ACL chặn và
broker **im lặng**.

⚠️ **Không cần** trường `deviceCode` trong payload — cầu nối ghi đè bằng giá trị chuẩn từ DB (IOT3-14).

```bash
TS=$(date -u +%Y-%m-%dT%H:%M:%SZ)

docker run --rm eclipse-mosquitto:2.0 mosquitto_pub \
  -h "$LAN_IP" -p 21883 -u "$MQTT_USER" -P "$DP" -q 1 \
  -t "solar/$MQTT_USER/BAT-2026-001/telemetry" \
  -m "{\"items\":[{\"time\":\"$TS\",\"batteryAssetSerial\":\"BAT-2026-001\",\"voltage\":12.6,\"current\":1.5,\"temperature\":25.3,\"socPercent\":80,\"sensorSourceCode\":\"primary\"}]}"

sleep 4
docker exec solar-postgres psql -U postgres -d battery_db -c \
"SELECT time, voltage, current, temperature, sensor_source_code
 FROM sensor_readings WHERE time > now() - interval '5 minutes' ORDER BY time DESC LIMIT 5;"
```

**Đạt khi:** có dòng mới, `sensor_source_code = primary`.

**Khoanh vùng khi trống:**

```bash
docker logs solar-batteryservice --since 2m 2>&1 \
  | grep -oE '"Message":"[^"]*(unknown device|Failed handling)[^"]*"' | tail -5
```

- `unknown device` → sai phân đoạn topic
- **rỗng hoàn toàn** → payload sai tên trường (nợ #2), hoặc gói không qua được ACL

Tách bạch broker với backend — nghe bằng quyền `backend-bridge` (ACL `readwrite solar/#`):

```bash
docker exec -d solar-mosquitto sh -c \
  "mosquitto_sub -h 127.0.0.1 -p 1883 -u backend-bridge -P '$PW' -t 'solar/+/+/telemetry' -C 1 -W 12 > /tmp/caught.txt"
# publish lại, rồi:
docker exec solar-mosquitto cat /tmp/caught.txt
```

Bắt được ⇒ gói qua broker, lỗi ở backend. Không bắt được ⇒ ACL chặn.

---

## 6. Test 3 — Heartbeat + trang biểu đồ

Gửi 5 nhịp có xu hướng xấu dần, để biểu đồ có hình đọc được:

```bash
for i in 1 2 3 4 5; do
  docker run --rm eclipse-mosquitto:2.0 mosquitto_pub \
    -h "$LAN_IP" -p 21883 -u "$MQTT_USER" -P "$DP" -q 1 \
    -t "solar/$MQTT_USER/heartbeat" \
    -m "{\"firmwareVersion\":\"0.1.0\",\"rssiDbm\":$((-50 - i*8)),\"freeMemoryPercent\":$((70 - i*4)),\"uptimeSeconds\":$((i*300)),\"queuedReadingCount\":$((i*3)),\"deviceTimestamp\":\"$(date -u +%Y-%m-%dT%H:%M:%SZ)\"}"
  sleep 1
done

docker exec solar-postgres psql -U postgres -d battery_db -c \
"SELECT time, rssi_dbm, free_memory_percent, uptime_seconds, queued_reading_count
 FROM iot_device_heartbeats ORDER BY time DESC LIMIT 5;"
```

Rồi mở web **`/staff/iot-devices`** → bấm thiết bị → phải thấy **4 biểu đồ**: sóng WiFi · RAM còn
trống · bản ghi đang xếp hàng · chạy liên tục. Sóng tụt xuống dưới −75 dBm thì ô "Sóng WiFi" chuyển
đỏ kèm cảnh báo.

Test này xác nhận cùng lúc endpoint `GET /api/iot-devices/{id}/heartbeats` (IOT3-58), trang biểu đồ
(IOT3-67) và ngưỡng RSSI (IOT3-60).

---

## 7. Test 4 — Downlink (chiều xuống)

Đây là chỗ lỗi hoa/thường IOT3-14 từng giết chết: backend cũ ghép topic từ `DeviceCode` **chữ HOA**
trong khi thiết bị nghe ở chữ thường. Broker không báo gì — lệnh chỉ đơn giản không tới nơi.

**Cửa sổ 1** — nghe bằng quyền thiết bị (ACL `pattern read solar/%u/cmd`), để nguyên:

```bash
docker run --rm eclipse-mosquitto:2.0 mosquitto_sub \
  -h "$LAN_IP" -p 21883 -u "$MQTT_USER" -P "$DP" \
  -t "solar/$MQTT_USER/cmd" -v
```

**Cửa sổ 2** — gửi lệnh. ⚠️ Trường là **`type`** và **`params`**, không phải `commandType`/`payload`:

```bash
DEV_ID=$(docker exec solar-postgres psql -U postgres -d battery_db -tAc \
  "SELECT id FROM iot_devices WHERE device_code='$DEVICE';" | tr -d ' \r\n')

curl -s -X POST "$BE/api/admin/iot-devices/$DEV_ID/command" \
  -H "Content-Type: application/json" -H "Authorization: Bearer $TK" \
  -d '{"type":"sample-now","params":{"reason":"regression-test"}}' | python3 -m json.tool
```

**Đạt khi:** trả `202` kèm `"topic": "solar/esp-test-2/cmd"` (**chữ thường**, dù `deviceCode` chữ
HOA), và cửa sổ 1 hiện ngay gói `{"cmdId":"…","type":"sample-now",…}`.

| Quan sát | Kết luận |
|---|---|
| 202 **và** cửa sổ nghe hiện tin | ✅ chuẩn hoá chữ thường đúng cả hai đầu |
| 202 nhưng cửa sổ nghe **im** | ❌ backend ghép topic chữ hoa — lỗi IOT3-14 quay lại |
| 503 | cầu nối rớt kết nối — xem log batteryservice |
| 400 `Type là bắt buộc` | sai tên trường trong body |

---

## 8. Test 5 — Ngưỡng cảnh báo

### 8.1 Đọc ngưỡng THẬT trước, đừng đoán

```bash
docker exec solar-postgres psql -U postgres -d battery_db -c "
SELECT bt.name, tc.voltage_min, tc.voltage_max, tc.temperature_min, tc.temperature_max,
       tc.soc_warning_threshold, tc.noise_suppression_enabled,
       tc.noise_suppression_count, tc.noise_suppression_window_hours
FROM battery_assets ba
JOIN battery_types bt ON bt.id = ba.battery_type_id
JOIN threshold_configs tc ON tc.battery_type_id = bt.id AND NOT tc.is_deleted
WHERE ba.serial_number = 'BAT-2026-001' AND NOT ba.is_deleted;"
```

Ví dụ đo được (LiFePO4 12V 100Ah): điện áp **10.50–14.60 V**, nhiệt độ −10…60 °C, SOC cảnh báo 20 %,
**chống nhiễu bật, 5 lần / 24 giờ**.

> **Chống nhiễu quyết định kết quả bài test.** `count = 5` nghĩa là phải có **5 lần vi phạm trong
> 24 giờ** mới sinh alert — một gói xấu đơn lẻ **không tạo gì cả**, và đó là hành vi ĐÚNG.
> Hai đường đi tắt nổ ngay lần đầu: `EnvironmentalIncident`, và `Overheat` mức `Critical`.

### 8.2 Quá nhiệt nghiêm trọng — phải nổ NGAY gói đầu

```bash
docker run --rm eclipse-mosquitto:2.0 mosquitto_pub \
  -h "$LAN_IP" -p 21883 -u "$MQTT_USER" -P "$DP" -q 1 \
  -t "solar/$MQTT_USER/BAT-2026-001/telemetry" \
  -m "{\"items\":[{\"time\":\"$(date -u +%Y-%m-%dT%H:%M:%SZ)\",\"batteryAssetSerial\":\"BAT-2026-001\",\"voltage\":12.6,\"current\":1.5,\"temperature\":85.0,\"socPercent\":80,\"sensorSourceCode\":\"primary\"}]}"

sleep 12      # ≤10 giây là chu kỳ quét sau IOT3-80 (trước đây 30 giây)

docker exec solar-postgres psql -U postgres -d battery_db -c \
"SELECT detected_at, anomaly_type, severity, actual_value, threshold_value, unit, status,
        merged_into_alert_id IS NOT NULL AS la_ban_gop
 FROM alerts WHERE detected_at > now() - interval '5 minutes' ORDER BY detected_at DESC;"
```

**Đạt khi:** có alert `anomaly_type=1` (Overheat), `severity=3` (Critical), `actual 85 / threshold 60`.

> **Hai dòng cùng `detected_at` KHÔNG phải lỗi.** Dòng thứ hai có `merged_into_alert_id` trỏ về dòng
> đầu và `status=3` (Merged) — hai lượt quét chồng cửa sổ cùng thấy một bất thường, hệ thống gộp
> thay vì tạo cảnh báo mới. Khử trùng lặp chạy đúng.

### 8.3 Quá áp — phải IM LẶNG mấy gói đầu

```bash
for i in 1 2 3 4 5 6; do
  docker run --rm eclipse-mosquitto:2.0 mosquitto_pub \
    -h "$LAN_IP" -p 21883 -u "$MQTT_USER" -P "$DP" -q 1 \
    -t "solar/$MQTT_USER/BAT-2026-001/telemetry" \
    -m "{\"items\":[{\"time\":\"$(date -u +%Y-%m-%dT%H:%M:%SZ)\",\"batteryAssetSerial\":\"BAT-2026-001\",\"voltage\":20.0,\"current\":1.5,\"temperature\":25.0,\"socPercent\":80,\"sensorSourceCode\":\"primary\"}]}"
  echo "  gói $i"; sleep 2
done

sleep 12

# ⚠️ Bảng này KHÔNG có cột `created_at` — khoá thời gian là `time`
docker exec solar-postgres psql -U postgres -d battery_db -c \
"SELECT time, actual_value, promoted_to_alert_id IS NOT NULL AS da_len_alert
 FROM noise_breach_events WHERE anomaly_type=2 AND time > now() - interval '15 minutes' ORDER BY time;"

docker exec solar-postgres psql -U postgres -d battery_db -c \
"SELECT detected_at, status, merged_into_alert_id IS NOT NULL AS la_ban_gop
 FROM alerts WHERE anomaly_type=2 AND detected_at > now() - interval '15 minutes' ORDER BY detected_at;"
```

**Đạt khi:** đủ **6 breach** được ghi, và **mấy gói đầu KHÔNG sinh alert nào**.

> **Số alert cuối cùng KHÔNG phải hằng số** — nó phụ thuộc nhịp quét. Lần đo 2026-08-08 cho ra
> 6 breach + **3 alert** (không phải 2), vì lượt quét sau đánh giá lại những reading còn nằm trong
> tầm lookback. Đây là hành vi ĐÚNG, đừng "sửa" theo con số kỳ vọng.
>
> ⚠️ **Chạy lại bài này trong cùng 24 giờ sẽ nổ NGAY gói đầu** — 6 breach cũ vẫn còn trong cửa sổ.
> Muốn thử lại từ đầu thì xoá breach cũ (§11) hoặc chờ qua 24 giờ.
>
> **Và số alert sẽ nhiều hơn số gói** (đo được: 6 gói → 12 alert). Kiểm `status` trước khi hoảng:
>
> ```bash
> docker exec solar-postgres psql -U postgres -d battery_db -c \
> "SELECT status, merged_into_alert_id IS NOT NULL AS la_ban_gop, count(*)
>  FROM alerts WHERE anomaly_type=2 AND detected_at > now() - interval '10 minutes'
>  GROUP BY status, la_ban_gop;"
> ```
>
> Tất cả `status=3` (Merged) cùng trỏ về **một** alert cha ⇒ ĐÚNG: người trực chỉ thấy một cảnh
> báo, 12 dòng kia là audit chứng minh sự cố kéo dài. Chỉ khi chúng là `status=1` (Open) mới là lỗi
> — lúc đó bảng cảnh báo sẽ ngập rác.

> ⚠️ **Nợ #3:** cột `promoted_to_alert_id` sẽ **luôn rỗng**. Đường promote không bao giờ chạy vì
> `AnomalyDetectionService.cs:133` gác `if (recordedBreach is not null)`, mà alert của đường chống
> nhiễu **chỉ nổ ở lượt quét lại** — lượt đó `recordedBreach` đã null. Chi tiết ở
> `docs/non-obvious-decisions.md`.

---

### 8.4 Khử trùng lặp alert — bài test lộ ra **nợ #4**

Đây là bài **duy nhất** trong cả tài liệu bắt buộc phải chạy trên **DB sạch**. Chạy chồng lên dữ
liệu cũ thì kết quả nhìn như hoàn hảo, và lỗi vẫn nằm nguyên đó.

#### Câu hỏi bài test trả lời

Một viên pin lỗi thật — điện áp vọt lên rồi **giữ nguyên** — sẽ đẩy bao nhiêu cảnh báo vào hàng đợi
trực? Đúng ra là **một** (kèm N−1 bản gộp làm audit). Nếu ra N cảnh báo `Open` giống hệt nhau thì
người trực phải tự lọc bằng mắt, và cảnh báo thật sẽ chìm trong đó.

Chống nhiễu (§8.3) chặn **báo động giả** — vi phạm thoáng qua do nhiễu đo.
Khử trùng lặp chặn **báo động trùng** — cùng một sự cố kéo dài. **Hai việc khác nhau**, đừng lẫn.

#### Bước 1 — dọn sạch, và KIỂM lệnh dọn có chạy thật không

Dán **từng khối một**. Đây là chỗ `zsh: parse error` từng cắt ngang và làm sai cả bài test (§10bis).

```bash
docker exec solar-postgres psql -U postgres -d battery_db -c "DELETE FROM noise_breach_events WHERE anomaly_type=2;"
```

```bash
docker exec solar-postgres psql -U postgres -d battery_db -c "DELETE FROM alerts WHERE anomaly_type=2;"
```

```bash
docker exec solar-postgres psql -U postgres -d battery_db -tAc "SELECT 'breach=' || (SELECT count(*) FROM noise_breach_events WHERE anomaly_type=2) || ' alert=' || (SELECT count(*) FROM alerts WHERE anomaly_type=2);"
```

**Bắt buộc ra `breach=0 alert=0`.** Khác 0 là lệnh xoá bị cắt — dừng lại, đừng chạy tiếp, kết quả
sẽ vô nghĩa.

#### Bước 2 — đưa thiết bị về Active

Sau bài LWT (§9) thiết bị đang Offline, mà Offline thì telemetry vẫn vào nhưng bối cảnh đã khác.

```bash
eval $(docker exec solar-postgres psql -U postgres -d battery_db -tAc "SELECT 'AK='||api_key_plaintext||' DP='||mqtt_password_plaintext FROM iot_devices WHERE device_code='ESP-TEST-2';")
```

```bash
curl -s -X POST http://localhost:4006/api/iot-devices/provision -H "Content-Type: application/json" -H "X-Api-Key: $AK" -H "X-Device-Code: ESP-TEST-2" -d "{\"firmwareVersion\":\"0.1.0\",\"hardwareRevision\":\"v1.0\",\"deviceTimestamp\":\"$(date -u +%Y-%m-%dT%H:%M:%SZ)\"}" | python3 -m json.tool | grep -E '"isSuccess"|"statusCode"'
```

#### Bước 3 — gửi một chùm vi phạm LIÊN TỤC

Cách nhau 2 giây để cả chùm rơi vào **cùng một lượt quét** (chu kỳ 10 giây sau IOT3-80). Đó chính
là điều kiện làm lộ lỗi.

```bash
LAN_IP=$(ipconfig getifaddr en0 || ipconfig getifaddr en1)
```

```bash
for i in 1 2 3 4 5 6; do docker run --rm eclipse-mosquitto:2.0 mosquitto_pub -h "$LAN_IP" -p 21883 -u esp-test-2 -P "$DP" -q 1 -t "solar/esp-test-2/BAT-2026-001/telemetry" -m "{\"items\":[{\"time\":\"$(date -u +%Y-%m-%dT%H:%M:%SZ)\",\"batteryAssetSerial\":\"BAT-2026-001\",\"voltage\":20.0,\"current\":1.5,\"temperature\":25.0,\"socPercent\":80,\"sensorSourceCode\":\"primary\"}]}"; echo "  goi $i"; sleep 2; done
```

#### Bước 4 — đếm theo `status`, KHÔNG đếm tổng số dòng

Đây là điểm quyết định. Đếm tổng số alert sẽ ra một con số vô nghĩa (nó phụ thuộc nhịp quét).
Con số có nghĩa là **số alert `Open`**.

```bash
sleep 15
```

```bash
docker exec solar-postgres psql -U postgres -d battery_db -c "SELECT CASE status WHEN 1 THEN 'Open (nguoi truc THAY)' WHEN 2 THEN 'Acknowledged' WHEN 3 THEN 'Merged (audit)' WHEN 4 THEN 'Resolved' END AS y_nghia, count(*) FROM alerts WHERE anomaly_type=2 AND detected_at > now() - interval '5 minutes' GROUP BY status ORDER BY status;"
```

Xem chi tiết từng dòng, gồm cả liên kết cha–con:

```bash
docker exec solar-postgres psql -U postgres -d battery_db -c "SELECT detected_at, status, merged_into_alert_id AS alert_cha, actual_value FROM alerts WHERE anomaly_type=2 AND detected_at > now() - interval '5 minutes' ORDER BY detected_at;"
```

#### Cách đọc

| Số alert `Open` (status=1) | Kết luận |
|---|---|
| **1** — kèm vài bản `Merged` cùng trỏ về nó | ✅ khử trùng lặp ĐÚNG. Nợ #4 đã được sửa |
| **> 1**, `merged_into_alert_id` đều NULL | ❌ **nợ #4 còn nguyên** — dedup mù với alert cùng lượt quét |
| **0** | ⚠️ chống nhiễu chưa đủ ngưỡng, hoặc telemetry không vào — kiểm §5 trước |

Kết quả đo ngày 2026-08-08 trên DB sạch: **5 alert `Open`, `merged_into_alert_id` toàn NULL**, cùng
`battery_asset_id`, cùng `anomaly_type`, trong 9 giây.

#### Vì sao DB bẩn che mất lỗi này

`FindActiveAlertToMergeAsync` tìm alert đang mở **trong DB** để gộp vào. Nó chỉ tìm thấy khi có một
alert cùng loại **đã `SaveChanges`** và còn trong `DedupWindowEndUtc` (30 phút).

- **DB còn alert cũ** → tìm thấy cha ngay từ reading đầu → mọi alert sau đều `Merged`. Nhìn hoàn hảo.
  Lần đo 12:08 cho ra **12 alert, tất cả Merged** vào một cha từ 11:44.
- **DB sạch** → reading đầu tạo alert mới, nhưng nó còn **pending trong change tracker**, chưa vào
  DB. Reading thứ hai truy vấn → không thấy → tạo alert Open thứ hai. Và cứ thế.

Nghịch lý: **DB càng sạch, lỗi càng lộ.** Đó là lý do nó sống sót qua 657 unit test — test dựng
sẵn dữ liệu, hoặc chỉ chạy một reading một lần.

Chi tiết nguyên nhân + hai hướng sửa: `docs/non-obvious-decisions.md` §"Bốn nợ kỹ thuật" mục 4.

#### Biến thể — chứng minh dedup CÓ chạy qua các lượt quét

Để thấy rõ lỗi chỉ nằm ở phạm vi **một lượt quét**, gửi hai reading cách nhau **≥ 15 giây** (dài hơn
chu kỳ quét 10 giây), sau khi đã dọn sạch:

```bash
docker run --rm eclipse-mosquitto:2.0 mosquitto_pub -h "$LAN_IP" -p 21883 -u esp-test-2 -P "$DP" -q 1 -t "solar/esp-test-2/BAT-2026-001/telemetry" -m "{\"items\":[{\"time\":\"$(date -u +%Y-%m-%dT%H:%M:%SZ)\",\"batteryAssetSerial\":\"BAT-2026-001\",\"voltage\":20.0,\"current\":1.5,\"temperature\":25.0,\"socPercent\":80,\"sensorSourceCode\":\"primary\"}]}"; sleep 20
```

Chạy khối trên **năm lần liên tiếp** (mỗi lần cách nhau 20 giây), rồi đếm lại như Bước 4.

Kỳ vọng: **1 Open + 4 Merged**. Ra đúng vậy nghĩa là dedup hoạt động bình thường giữa các lượt quét,
và lỗi chỉ xảy ra **trong** một lượt — khoanh vùng chính xác cho người đi sửa.

---

## 9. Test 6 — LWT (di chúc khi mất kết nối đột ngột)

### 9.1 Điều kiện tiên quyết — thiết bị PHẢI đang Active

`DispatchStatusAsync` có chốt `if (device.Status != Active) return;`. Thiết bị im lặng quá 5 phút
sẽ bị `IotDeviceOfflineDetectionService` đánh dấu Offline, và khi đó test này **không làm gì cả**
mà cũng không log gì.

```bash
docker exec solar-postgres psql -U postgres -d battery_db -tAc \
  "SELECT 'status=' || status FROM iot_devices WHERE device_code='$DEVICE';"
```

Khác 2 thì chạy lại **§4** (provision) để đưa về Active.

### 9.2 Nối có di chúc, rồi chết đột ngột

```bash
docker run -d --name lwt-test --rm eclipse-mosquitto:2.0 mosquitto_sub \
  -h "$LAN_IP" -p 21883 -u "$MQTT_USER" -P "$DP" \
  --will-topic "solar/$MQTT_USER/status" --will-payload "offline" --will-qos 1 --will-retain \
  -t "solar/$MQTT_USER/cmd"

sleep 3
docker kill lwt-test
```

> `docker kill` gửi SIGKILL — socket đứt mà **không có gói DISCONNECT**, nên broker phát di chúc.
> Đúng bằng việc rút phích điện ngoài hiện trường. Thoát tử tế (`Ctrl-C`) thì broker **không** phát
> di chúc — đó là quy định MQTT, không phải lỗi.

### 9.3 Kết quả

```bash
sleep 8
docker exec solar-postgres psql -U postgres -d battery_db -c \
"SELECT device_code, status, last_offline_at FROM iot_devices WHERE device_code='$DEVICE';"

docker exec solar-postgres psql -U postgres -d battery_db -c \
"SELECT anomaly_type, severity, count(*), min(detected_at)
 FROM alerts WHERE detected_at > now() - interval '3 minutes'
 GROUP BY anomaly_type, severity;"
```

**Đạt khi:**

| Chỗ kiểm | Giá trị |
|---|---|
| `iot_devices.status` | **3** (Offline), `last_offline_at` vài giây trước |
| Alert | `anomaly_type=7` (DeviceOffline), `severity=2` (Warning) |
| **Số lượng** | = **số pin cùng site**, không phải 1 |

> Alert gắn theo **pin**, không theo thiết bị — người trực quan tâm "pin nào mất giám sát" chứ không
> phải "hộp nào chết". Site có 5 pin thì ra 5 alert, và `detected_at` của chúng trùng
> `last_offline_at` tới mili-giây (cùng một transaction).

---

## 10. Bảng tổng kết — chạy hết thì đã chứng minh được gì

| Mắt xích | Test | Task sprint liên quan |
|---|---|---|
| Tạo thiết bị → QR ảnh + 6 trường MQTT | §3 | IOT3-70/71/72 |
| Đồng bộ `passwd` → broker biết thiết bị | §2.4 + §2.5 | GH-784 |
| Provision HTTPS → 6 trường + `batteryMappings` | §4 | IOT3-25/26/42/49 |
| Telemetry MQTT → TimescaleDB | §5 | IOT3-14/39 |
| Heartbeat + biểu đồ | §6 | IOT3-58/60/67 |
| Downlink, chuẩn hoá chữ thường | §7 | IOT3-14/32/39 |
| Cảnh báo tức thì + khử trùng lặp | §8.2 | IOT3-80 |
| Chống nhiễu 5 lần / 24 h | §8.3 | NS-10 |
| Khử trùng lặp alert (⚠️ cần DB sạch) | §8.4 | — · lộ ra **nợ #4** |
| Điểm danh offline bằng polling | §9.1 | IoT-1 |
| LWT → alert theo từng pin | §9 | S4-FW-02 |

**Chưa phủ:** firmware trên phần cứng thật (chặn ở việc mua shunt 200 A/75 mV — IOT3-11),
trang cấu hình tại chỗ, OTA.

---

## 10bis. ⚠️ Dán lệnh vào zsh — hai bẫy cú pháp

**1. Comment có dấu ngoặc.** Dòng `# 2) Đưa thiết bị về Active` giữa một khối dán nhiều dòng làm zsh
báo `parse error near ')'` và **cắt ngang phần còn lại** — những lệnh phía trước có thể đã chạy,
phía sau thì không. Rất khó nhận ra vì output trông như bình thường.

**2. Comment cuối dòng lệnh.** `docker logs … | tail -2   # phải thấy client` cho ra
`tail: #: No such file or directory`.

Cách an toàn: **dán từng khối một**, không kèm comment. Hoặc bọc cả khối trong `bash <<'EOF' … EOF`
để zsh không diễn giải gì cả:

```bash
bash <<'EOF'
# comment trong này an toàn — kể cả 1) 2) 3)
echo "khối chạy trong bash, zsh không đụng vào"
EOF
```

**Luôn kiểm lệnh xoá có chạy thật không** trước khi kết luận kết quả test:

```bash
docker exec solar-postgres psql -U postgres -d battery_db -tAc \
  "SELECT 'breach con lai trong 24h: ' || count(*) FROM noise_breach_events
   WHERE anomaly_type=2 AND time > now() - interval '24 hours';"
```

Ra khác 0 mà anh vừa xoá ⇒ lệnh xoá đã bị cắt ngang.

---

## 11. Dọn dẹp sau khi test

Các lệnh dưới đây **xoá dữ liệu**. Chỉ chạy trên DB dev.

```bash
# Alert + breach sinh ra trong 1 giờ qua
docker exec solar-postgres psql -U postgres -d battery_db -c \
"DELETE FROM alerts WHERE detected_at > now() - interval '1 hour';"

docker exec solar-postgres psql -U postgres -d battery_db -c \
"DELETE FROM noise_breach_events WHERE time > now() - interval '1 hour';"

# Số đo giả
docker exec solar-postgres psql -U postgres -d battery_db -c \
"DELETE FROM sensor_readings WHERE time > now() - interval '1 hour' AND voltage IN (12.6, 20.0);"

# Heartbeat giả
docker exec solar-postgres psql -U postgres -d battery_db -c \
"DELETE FROM iot_device_heartbeats WHERE time > now() - interval '1 hour';"

# Đưa thiết bị test về Active
# → chạy lại §4
```

Xoá breach là **bắt buộc** nếu muốn chạy lại §8.3 trong cùng 24 giờ — không thì chống nhiễu đã đủ
ngưỡng và alert nổ ngay gói đầu.

Xoá **alert** là bắt buộc với §8.4 — và vì lý do ngược lại: alert cũ còn trong `DedupWindowEndUtc`
sẽ hút hết alert mới vào làm bản `Merged`, khiến bài test **báo đạt trong khi nợ #4 còn nguyên**.

---

## 12. Bốn cái bẫy tốn thời gian nhất — đọc trước khi truy sự cố

| Triệu chứng | Nguyên nhân thật |
|---|---|
| `MQTT bridge disabled (Mqtt:Enabled=false)` dù đã sửa `.env.Docker` | `environment:` thắng `env_file:`; phải sửa **`.env`** (§2.1) |
| `Connection Refused: not authorised` dù `passwd` đúng | broker chưa nạp lại — `kill -HUP 1` (§2.5, **nợ #1**) |
| Publish OK, DB trống, **không log gì** | mảng payload sai tên — phải là **`items`** (§5, **nợ #2**) |
| Test khử trùng lặp "luôn đạt" | DB còn alert cũ đang che lỗi — phải dọn sạch (§8.4, **nợ #4**) |

Ba cái đầu có chung một tính chất: **mọi tầng đều báo thành công**, không ngoại lệ, không dòng log
lỗi — chỉ có dữ liệu không tồn tại. Khi truy sự cố MQTT, kiểm chúng trước mọi giả thuyết khác.

Cái thứ tư ngược lại: nó làm **bài test báo đạt trong khi lỗi còn nguyên**. Nguy hiểm theo kiểu
khác — ba cái đầu làm mất thời gian, cái này làm mất niềm tin vào chính bộ test.

### Bảng tra nhanh bốn nợ

| # | Ở đâu | Triệu chứng | Chữa cháy |
|---|---|---|---|
| 1 | `passwd` + bind mount file lẻ | thiết bị mới không đăng nhập được | `docker exec solar-mosquitto kill -HUP 1` |
| 2 | `DispatchTelemetryAsync` | payload sai tên mảng → mất dữ liệu im lặng | dùng đúng `items` |
| 3 | `PromotedToAlertId` | chuỗi breach mất dấu vết, retention sẽ xoá | (không có, cần sửa code) |
| 4 | `FindActiveAlertToMergeAsync` | N alert `Open` trùng nhau cho một sự cố | (không có, cần sửa code) |

Chi tiết đầy đủ: `docs/non-obvious-decisions.md` §"Bốn nợ kỹ thuật phát hiện khi chạy thật".
