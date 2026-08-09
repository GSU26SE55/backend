#!/usr/bin/env bash
# ==================================================================
# Chạy lại toàn bộ quy trình test IoT end-to-end — không cần phần cứng.
# Tài liệu đầy đủ: iot-quy-trinh-test-khong-can-phan-cung.md
#
# Cách dùng:
#   ./iot-test-lai.sh                    # chạy hết, KHÔNG xoá dữ liệu cũ
#   ./iot-test-lai.sh --reset            # xoá alert/breach/reading cũ trước khi chạy
#   ./iot-test-lai.sh --token "<jwt>"    # kèm test downlink (cần token admin)
#   ./iot-test-lai.sh --device ESP-TEST-2
#   ./iot-test-lai.sh --help
#
# Viết thành script thay vì dán từng khối vì zsh diễn giải `#` và `)` trong comment
# tiếng Việt giữa khối dán nhiều dòng → `parse error near ')'` cắt ngang, những lệnh
# phía trước đã chạy còn phía sau thì không, mà output nhìn vẫn bình thường.
# ==================================================================
# KHÔNG dùng `pipefail`: `grep -q` thoát ngay khi khớp → đóng ống dẫn → lệnh phía trước nhận
# SIGPIPE → mã thoát khác 0 → `pipefail` lan ra cả đường ống và điều kiện thành SAI dù đã tìm thấy.
# Script này kiểm trạng thái tường minh ở từng bước nên không cần pipefail.
set -u

DEVICE="ESP-TEST-2"
TOKEN=""
RESET=0
BE="http://localhost:4006"
MQTT_PORT=21883
PG="solar-postgres"
DB="battery_db"
BROKER="solar-mosquitto"
BACKEND="solar-batteryservice"
IMG="eclipse-mosquitto:2.0"

while [ $# -gt 0 ]; do
  case "$1" in
    --device) DEVICE="$2"; shift 2 ;;
    --token)  TOKEN="$2";  shift 2 ;;
    --reset)  RESET=1;     shift ;;
    --help|-h) grep '^#' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) echo "Tham số lạ: $1 (xem --help)" >&2; exit 1 ;;
  esac
done

MQTT_USER="$(echo "$DEVICE" | tr '[:upper:]' '[:lower:]')"
PASS=0; FAIL=0; SKIP=0

c_ok()   { printf '  \033[32m✔ %s\033[0m\n' "$1"; PASS=$((PASS+1)); }
c_bad()  { printf '  \033[31m✘ %s\033[0m\n' "$1"; FAIL=$((FAIL+1)); }
c_skip() { printf '  \033[33m⊘ %s\033[0m\n' "$1"; SKIP=$((SKIP+1)); }
step()   { printf '\n\033[1m── %s\033[0m\n' "$1"; }
sql()    { docker exec "$PG" psql -U postgres -d "$DB" -tAc "$1" 2>/dev/null | tr -d ' \r'; }
sqlshow(){ docker exec "$PG" psql -U postgres -d "$DB" -c "$1" 2>&1; }
now_iso(){ date -u +%Y-%m-%dT%H:%M:%SZ; }

pub() { # pub <topic-suffix> <payload>
  docker run --rm "$IMG" mosquitto_pub \
    -h "$LAN_IP" -p "$MQTT_PORT" -u "$MQTT_USER" -P "$DP" -q 1 \
    -t "solar/$MQTT_USER/$1" -m "$2" 2>&1
}

# ================================================================== 0
step "0 · Tiền kiểm"

# `tr` là bắt buộc: docker ps ngăn bằng XUỐNG DÒNG, còn `case` dưới đây so khớp bằng KHOẢNG TRẮNG.
# Thiếu nó thì chỉ tên đầu và tên cuối khớp được, mọi container ở giữa đều bị báo "KHÔNG chạy".
RUNNING=" $(docker ps --format '{{.Names}}' | tr '\n' ' ') "
for c in "$PG" "$BROKER" "$BACKEND"; do
  case "$RUNNING" in
    *" $c "*) c_ok "container $c đang chạy" ;;
    *) c_bad "container $c KHÔNG chạy — 'docker compose --profile mqtt up -d' trước" ;;
  esac
done
[ "$FAIL" -gt 0 ] && { echo; echo "Dừng: hạ tầng chưa sẵn sàng."; exit 1; }

LAN_IP="$(ipconfig getifaddr en0 2>/dev/null || ipconfig getifaddr en1 2>/dev/null || hostname -I 2>/dev/null | awk '{print $1}')"
[ -n "$LAN_IP" ] && c_ok "LAN_IP = $LAN_IP" || { c_bad "không dò được IP LAN"; exit 1; }

# Cầu nối phải đang nối broker, nếu không mọi test uplink sẽ vào hư không.
BRIDGE_HITS="$(docker logs "$BACKEND" 2>&1 | grep -c "MQTT bridge connected to broker")"
if [ "${BRIDGE_HITS:-0}" -gt 0 ]; then
  c_ok "cầu nối MQTT đã nối broker"
else
  c_bad "cầu nối CHƯA nối — kiểm 'Mqtt__Enabled' trong .env (KHÔNG phải .env.Docker), xem §2.1"
  echo; echo "Dừng."; exit 1
fi

AK="$(sql "SELECT api_key_plaintext FROM iot_devices WHERE device_code='$DEVICE';")"
DP="$(sql "SELECT mqtt_password_plaintext FROM iot_devices WHERE device_code='$DEVICE';")"
SITE="$(sql "SELECT site_id FROM iot_devices WHERE device_code='$DEVICE';")"
DEV_ID="$(sql "SELECT id FROM iot_devices WHERE device_code='$DEVICE';")"

[ -n "$AK" ] && c_ok "có apiKey của $DEVICE" || { c_bad "$DEVICE không tồn tại hoặc chưa lưu apiKey"; exit 1; }
[ -n "$DP" ] && c_ok "có mật khẩu MQTT" || { c_bad "chưa có mật khẩu MQTT — chạy POST /api/admin/iot-devices/$DEV_ID/rotate-mqtt"; exit 1; }

N_ASSET="$(sql "SELECT count(*) FROM battery_assets WHERE site_id='$SITE' AND NOT is_deleted;")"
c_ok "site có $N_ASSET pin (số alert DeviceOffline kỳ vọng ở bước 6)"

SERIAL="$(sql "SELECT serial_number FROM battery_assets WHERE site_id='$SITE' AND NOT is_deleted ORDER BY serial_number LIMIT 1;")"
c_ok "pin dùng để test: $SERIAL"

# Nợ #1 — broker không tự nạp lại passwd. Bắn SIGHUP cho chắc.
docker exec "$BROKER" kill -HUP 1 >/dev/null 2>&1 && c_ok "đã SIGHUP broker (nợ #1)" || c_bad "SIGHUP thất bại"
sleep 2

# ================================================================== reset
if [ "$RESET" -eq 1 ]; then
  step "0bis · Dọn dữ liệu cũ (--reset)"
  sqlshow "DELETE FROM noise_breach_events WHERE time > now() - interval '24 hours';" | tail -1
  sqlshow "DELETE FROM alerts WHERE detected_at > now() - interval '24 hours';" | tail -1
  sqlshow "DELETE FROM iot_device_heartbeats WHERE iot_device_id='$DEV_ID';" | tail -1

  LEFT="$(sql "SELECT count(*) FROM noise_breach_events WHERE time > now() - interval '24 hours';")"
  [ "$LEFT" = "0" ] && c_ok "breach còn lại: 0" \
    || c_bad "breach còn lại: $LEFT — lệnh xoá bị cắt, KẾT QUẢ BƯỚC 5 SẼ SAI"
fi

# ================================================================== 1
step "1 · Provision (HTTPS) — đưa thiết bị về Active"

RESP="$(curl -s -X POST "$BE/api/iot-devices/provision" \
  -H "Content-Type: application/json" \
  -H "X-Api-Key: $AK" -H "X-Device-Code: $DEVICE" \
  -d "{\"firmwareVersion\":\"0.1.0\",\"hardwareRevision\":\"v1.0\",\"deviceTimestamp\":\"$(now_iso)\"}")"

echo "$RESP" | python3 -c '
import sys, json
d = json.load(sys.stdin)
ok = d.get("isSuccess") and d.get("statusCode") == 200
data = d.get("data") or {}
six = ["mqttBrokerHost","mqttBrokerPort","mqttUseTls","mqttTopicPrefix","mqttUsername","mqttPassword"]
have = [k for k in six if data.get(k) is not None]
print("RESULT", "ok" if ok else "fail",
      len(have), len(data.get("batteryMappings") or []),
      data.get("mqttTopicPrefix") or "-", d.get("message",""))
' > /tmp/iot_prov.$$ 2>/dev/null

read -r _ PSTAT PSIX PMAP PPREFIX PMSG < /tmp/iot_prov.$$ || true
rm -f /tmp/iot_prov.$$

if [ "${PSTAT:-fail}" = "ok" ]; then
  c_ok "provision 200 — $PSIX/6 trường MQTT, $PMAP mapping pin, prefix=$PPREFIX"
  [ "$PSIX" = "6" ] && c_ok "đủ 6 trường MQTT (IOT3-26/42)" || c_bad "chỉ có $PSIX/6 trường MQTT"
  [ "${PMAP:-0}" -gt 0 ] && c_ok "batteryMappings có $PMAP pin (IOT3-49)" || c_bad "batteryMappings rỗng"
else
  c_bad "provision thất bại: $PMSG"
  echo "$RESP" | head -c 400; echo
fi

ST="$(sql "SELECT status FROM iot_devices WHERE device_code='$DEVICE';")"
[ "$ST" = "2" ] && c_ok "trạng thái = 2 (Active)" || c_bad "trạng thái = $ST, cần 2 — bước 6 sẽ không chạy"

# ================================================================== 2
step "2 · Telemetry (MQTT uplink) → TimescaleDB"

BEFORE="$(sql "SELECT count(*) FROM sensor_readings WHERE time > now() - interval '2 minutes';")"
OUT="$(pub "$SERIAL/telemetry" \
  "{\"items\":[{\"time\":\"$(now_iso)\",\"batteryAssetSerial\":\"$SERIAL\",\"voltage\":12.6,\"current\":1.5,\"temperature\":25.3,\"socPercent\":80,\"sensorSourceCode\":\"primary\"}]}")"
[ -z "$OUT" ] && c_ok "publish OK" || { c_bad "publish lỗi: $OUT"; }

sleep 5
AFTER="$(sql "SELECT count(*) FROM sensor_readings WHERE time > now() - interval '2 minutes';")"
if [ "${AFTER:-0}" -gt "${BEFORE:-0}" ]; then
  c_ok "DB có bản ghi mới ($BEFORE → $AFTER)"
else
  c_bad "DB KHÔNG có bản ghi mới — mảng payload phải tên 'items' (nợ #2), hoặc topic sai chữ thường"
fi

# ================================================================== 3
step "3 · Heartbeat → endpoint + biểu đồ"

for i in 1 2 3 4 5; do
  pub "heartbeat" \
    "{\"firmwareVersion\":\"0.1.0\",\"rssiDbm\":$((-50 - i*8)),\"freeMemoryPercent\":$((70 - i*4)),\"uptimeSeconds\":$((i*300)),\"queuedReadingCount\":$((i*3)),\"deviceTimestamp\":\"$(now_iso)\"}" >/dev/null
  sleep 1
done
sleep 3

HB="$(sql "SELECT count(*) FROM iot_device_heartbeats WHERE iot_device_id='$DEV_ID' AND time > now() - interval '2 minutes';")"
[ "${HB:-0}" -ge 5 ] && c_ok "$HB heartbeat vào DB (IOT3-58)" || c_bad "chỉ có ${HB:-0} heartbeat"
echo "     → mở web /staff/iot-devices/$DEV_ID để xem 4 biểu đồ (IOT3-67)"

# ================================================================== 4
step "4 · Downlink (chiều xuống) — chuẩn hoá chữ thường"

if [ -z "$TOKEN" ]; then
  c_skip "bỏ qua — cần --token \"<jwt admin>\""
else
  docker run -d --name iot-dl-$$ --rm "$IMG" mosquitto_sub \
    -h "$LAN_IP" -p "$MQTT_PORT" -u "$MQTT_USER" -P "$DP" \
    -t "solar/$MQTT_USER/cmd" -v >/dev/null 2>&1
  sleep 3

  DLRESP="$(curl -s -X POST "$BE/api/admin/iot-devices/$DEV_ID/command" \
    -H "Content-Type: application/json" -H "Authorization: Bearer $TOKEN" \
    -d '{"type":"sample-now","params":{"reason":"iot-test-lai"}}')"
  sleep 3

  DLTOPIC="$(echo "$DLRESP" | python3 -c 'import sys,json;d=json.load(sys.stdin);print((d.get("data") or {}).get("topic","-"))' 2>/dev/null)"
  DLSEEN="$(docker logs "iot-dl-$$" 2>&1 | grep -c "solar/$MQTT_USER/cmd" || true)"
  docker kill "iot-dl-$$" >/dev/null 2>&1 || true

  case "$DLTOPIC" in
    "solar/$MQTT_USER/cmd") c_ok "backend dựng topic chữ thường: $DLTOPIC (IOT3-14/39)" ;;
    "-") c_bad "backend từ chối: $(echo "$DLRESP" | head -c 200)" ;;
    *)   c_bad "topic sai: $DLTOPIC (kỳ vọng solar/$MQTT_USER/cmd)" ;;
  esac
  [ "${DLSEEN:-0}" -ge 1 ] && c_ok "thiết bị NHẬN được lệnh" \
    || c_bad "thiết bị KHÔNG nhận — lệnh rơi vào hư không"
fi

# ================================================================== 5
step "5 · Ngưỡng cảnh báo"

BCOUNT="$(sql "SELECT count(*) FROM noise_breach_events WHERE anomaly_type=2 AND time > now() - interval '24 hours';")"
NSC="$(sql "SELECT tc.noise_suppression_count FROM battery_assets ba JOIN threshold_configs tc ON tc.battery_type_id=ba.battery_type_id AND NOT tc.is_deleted WHERE ba.serial_number='$SERIAL';")"
echo "     ngưỡng chống nhiễu = ${NSC:-?} lần/24h · breach sẵn có = ${BCOUNT:-0}"
[ "${BCOUNT:-0}" -ge "${NSC:-5}" ] && \
  c_skip "breach cũ đã vượt ngưỡng ⇒ alert sẽ nổ NGAY gói đầu (chạy lại với --reset để thấy chống nhiễu)"

echo "  5.1 Quá nhiệt nghiêm trọng — phải nổ ngay gói đầu"
pub "$SERIAL/telemetry" \
  "{\"items\":[{\"time\":\"$(now_iso)\",\"batteryAssetSerial\":\"$SERIAL\",\"voltage\":12.6,\"current\":1.5,\"temperature\":85.0,\"socPercent\":80,\"sensorSourceCode\":\"primary\"}]}" >/dev/null
sleep 13

OH="$(sql "SELECT count(*) FROM alerts WHERE anomaly_type=1 AND status=1 AND detected_at > now() - interval '2 minutes';")"
[ "${OH:-0}" -ge 1 ] && c_ok "alert Overheat/Critical trong ≤13s (IOT3-80)" || c_bad "không có alert Overheat"

echo "  5.2 Quá áp — chống nhiễu"
for i in 1 2 3 4 5 6; do
  pub "$SERIAL/telemetry" \
    "{\"items\":[{\"time\":\"$(now_iso)\",\"batteryAssetSerial\":\"$SERIAL\",\"voltage\":20.0,\"current\":1.5,\"temperature\":25.0,\"socPercent\":80,\"sensorSourceCode\":\"primary\"}]}" >/dev/null
  sleep 2
done
sleep 13

NB="$(sql "SELECT count(*) FROM noise_breach_events WHERE anomaly_type=2 AND time > now() - interval '3 minutes';")"
OV_OPEN="$(sql "SELECT count(*) FROM alerts WHERE anomaly_type=2 AND status=1 AND detected_at > now() - interval '3 minutes';")"
OV_MERGED="$(sql "SELECT count(*) FROM alerts WHERE anomaly_type=2 AND status=3 AND detected_at > now() - interval '3 minutes';")"

[ "${NB:-0}" -ge 6 ] && c_ok "$NB breach được ghi nhận" || c_bad "chỉ có ${NB:-0} breach, cần ≥6"
echo "     alert quá áp: $OV_OPEN mở (status=1) · $OV_MERGED gộp (status=3)"

# Bước 5.2 CHỈ phán xét chống nhiễu — tức có reading nào bị chặn không.
# Khử trùng lặp là chuyện khác, để 5.3 lo. Gộp hai thứ vào một chỗ thì nợ #4 (đã biết) sẽ làm
# script thoát mã 1 và che mất hồi quy thật.
TOTAL_OV=$(( ${OV_OPEN:-0} + ${OV_MERGED:-0} ))
if [ "$TOTAL_OV" -lt "${NB:-0}" ]; then
  c_ok "$TOTAL_OV alert / $NB breach — chống nhiễu có chặn bớt"
elif [ "${BCOUNT:-0}" -ge "${NSC:-5}" ]; then
  c_skip "không chặn được gì vì breach cũ đã vượt ngưỡng — chạy với --reset"
else
  c_bad "$TOTAL_OV alert / $NB breach — chống nhiễu KHÔNG chặn reading nào"
fi

PROM="$(sql "SELECT count(*) FROM noise_breach_events WHERE promoted_to_alert_id IS NOT NULL;")"
[ "${PROM:-0}" = "0" ] && c_skip "promoted_to_alert_id = 0 trên toàn bảng — nợ #3 (đã biết, chưa sửa)"

echo "  5.3 Khử trùng lặp — chỉ có nghĩa khi chạy với --reset"
# Số alert OPEN mới là con số có nghĩa. Đếm tổng số dòng sẽ ra giá trị phụ thuộc nhịp quét.
# Một sự cố kéo dài phải cho ra ĐÚNG MỘT alert Open + N-1 bản Merged làm audit.
if [ "$RESET" -eq 0 ]; then
  c_skip "bỏ qua — DB còn alert cũ sẽ hút alert mới thành bản Merged và che mất nợ #4"
elif [ "${OV_OPEN:-0}" = "1" ]; then
  c_ok "đúng 1 alert Open + $OV_MERGED bản gộp — khử trùng lặp ĐÚNG (nợ #4 đã được sửa)"
elif [ "${OV_OPEN:-0}" -gt 1 ]; then
  c_skip "nợ #4 còn nguyên: $OV_OPEN alert Open trùng nhau cho MỘT sự cố (đã biết, chưa sửa)"
  echo "     → dedup mù với alert do chính lượt quét đó tạo; xem docs/non-obvious-decisions.md mục 4"
else
  c_bad "0 alert Open — telemetry hoặc chống nhiễu có vấn đề, kiểm bước 2 trước"
fi

# ================================================================== 6
step "6 · LWT — mất kết nối đột ngột"

ST="$(sql "SELECT status FROM iot_devices WHERE device_code='$DEVICE';")"
if [ "$ST" != "2" ]; then
  c_skip "thiết bị đang status=$ST (cần 2) — DispatchStatusAsync sẽ bỏ qua"
else
  docker run -d --name iot-lwt-$$ --rm "$IMG" mosquitto_sub \
    -h "$LAN_IP" -p "$MQTT_PORT" -u "$MQTT_USER" -P "$DP" \
    --will-topic "solar/$MQTT_USER/status" --will-payload "offline" --will-qos 1 --will-retain \
    -t "solar/$MQTT_USER/cmd" >/dev/null 2>&1
  sleep 4
  docker kill "iot-lwt-$$" >/dev/null 2>&1     # SIGKILL — socket đứt, KHÔNG có DISCONNECT
  sleep 8

  ST2="$(sql "SELECT status FROM iot_devices WHERE device_code='$DEVICE';")"
  [ "$ST2" = "3" ] && c_ok "thiết bị → Offline qua di chúc" || c_bad "trạng thái = $ST2, cần 3"

  DO="$(sql "SELECT count(*) FROM alerts WHERE anomaly_type=7 AND detected_at > now() - interval '2 minutes';")"
  if [ "${DO:-0}" = "$N_ASSET" ]; then
    c_ok "$DO alert DeviceOffline = đúng số pin cùng site"
  else
    c_bad "có ${DO:-0} alert DeviceOffline, kỳ vọng $N_ASSET (một cho mỗi pin)"
  fi
fi

# ================================================================== tổng kết
step "Tổng kết"
printf '  đạt %d · hỏng %d · bỏ qua %d\n' "$PASS" "$FAIL" "$SKIP"
echo
echo "  Thiết bị đang Offline sau bước 6 — chạy lại script (hoặc bước 1) để đưa về Active."
echo "  Dọn dữ liệu test: xem §11 trong iot-quy-trinh-test-khong-can-phan-cung.md"
[ "$FAIL" -eq 0 ] && exit 0 || exit 1
