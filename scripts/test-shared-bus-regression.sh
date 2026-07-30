#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# Regression runtime cho thay đổi ở SharedInfrastructure/Bus/MassTransitExtensions.
#
# Rủi ro R-41: file này được **8 service** dùng chung. Sửa nó mà chỉ kiểm service
# đang làm là bỏ sót — một service khác có thể chết lúc khởi động mà không ai thấy,
# vì nó không nằm trong luồng đang test.
#
# Script kiểm ĐỦ 8:
#   · image có mới không (chạy đúng code vừa sửa)
#   · container còn sống không
#   · MassTransit bus có khởi động không
#   · log có exception khi khởi động không
# ─────────────────────────────────────────────────────────────────────────────
set -uo pipefail

EV="${EV:?Cần đặt biến EV = thư mục evidence}"
export MQ="${MQ:-http://localhost:15673}"   # solar-rabbitmq (15672 là iot-rabbitmq của dự án khác)
OUT="$EV/10-shared-bus-regression"
mkdir -p "$OUT"

PASS=0; FAIL=0
SUM="$OUT/_summary.md"; : > "$SUM"
ok()  { PASS=$((PASS+1)); printf '  ✅ %-34s %s\n' "$1" "${2:-}"; printf '| ✅ PASS | %s | %s |\n' "$1" "${2:-}" >> "$SUM"; }
bad() { FAIL=$((FAIL+1)); printf '  ❌ %-34s %s\n' "$1" "${2:-}"; printf '| ❌ FAIL | %s | %s |\n' "$1" "${2:-}" >> "$SUM"; }

# 8 service gọi AddMessageBus (xem R-41). Cột 2 = tên container.
SERVICES=(
  "AuthService|solar-authservice"
  "BatteryService|solar-batteryservice"
  "TicketService|solar-ticketservice"
  "NotificationService|solar-notificationservice"
  "EmailService|solar-emailservice"
  "SmsService|solar-smsservice"
  "FileStorageService|solar-filestorageservice"
  "AuditAggregatorService|solar-auditaggregatorservice"
)

{
  echo "# Regression runtime — SharedInfrastructure/Bus (R-41)"
  echo ""
  echo "- Thời điểm: $(date '+%Y-%m-%d %H:%M:%S %Z')"
  echo "- Phạm vi: **8 service** gọi \`AddMessageBus\`"
  echo ""
  echo "| KQ | Hạng mục | Bằng chứng |"
  echo "|----|----------|------------|"
} >> "$SUM"

echo ""
echo "── A. Image có phải bản vừa build không ──────────────────────────────"
for entry in "${SERVICES[@]}"; do
  name="${entry%%|*}"; c="${entry##*|}"
  age=$(docker ps --filter "name=^${c}$" --format '{{.RunningFor}}' 2>/dev/null)
  img=$(docker inspect "$c" --format '{{.Image}}' 2>/dev/null | cut -c8-19)
  # So id image container đang chạy với id image mới nhất cùng tên
  repo=$(docker inspect "$c" --format '{{.Config.Image}}' 2>/dev/null)
  latest=$(docker images --no-trunc --format '{{.Repository}} {{.ID}}' | awk -v r="$repo" '$1==r{print $2}' | cut -c8-19 | head -1)
  if [ -n "$img" ] && [ "$img" = "$latest" ]; then
    ok "$name dùng image mới nhất" "chạy $age"
  else
    bad "$name dùng image CŨ" "container=$img · mới nhất=$latest ⇒ cần docker compose up -d $name"
  fi
done

echo ""
echo "── B. Container còn sống ─────────────────────────────────────────────"
for entry in "${SERVICES[@]}"; do
  name="${entry%%|*}"; c="${entry##*|}"
  st=$(docker ps -a --filter "name=^${c}$" --format '{{.Status}}' 2>/dev/null)
  case "$st" in
    Up*) ok "$name đang chạy" "$st" ;;
    *)   bad "$name KHÔNG chạy" "${st:-không tìm thấy container}" ;;
  esac
done

echo ""
echo "── C. MassTransit bus khởi động ──────────────────────────────────────"
for entry in "${SERVICES[@]}"; do
  name="${entry%%|*}"; c="${entry##*|}"
  docker logs "$c" --since 20m > "$OUT/$c.log" 2>&1

  # Log là JSON có escape unicode ⇒ parse thay vì grep chuỗi thô.
  python3 - "$OUT/$c.log" > "$OUT/$c.bus.txt" 2>&1 <<'PYEOF'
import json, sys
started = None
errors = []
for line in open(sys.argv[1], encoding="utf-8", errors="replace"):
    line = line.strip()
    if not line.startswith("{"):
        continue
    try:
        r = json.loads(line)
    except Exception:
        continue
    msg = (r.get("Message") or "")
    if "Bus started" in msg:
        started = msg[:100]
    if r.get("LogLevel") in ("Error", "Critical"):
        ex = (r.get("Exception") or "")[:200]
        errors.append((msg[:120], ex))
print("STARTED|" + (started or ""))
for m, e in errors[:5]:
    print("ERROR|" + m.replace("\n", " ") + "|" + e.replace("\n", " "))
PYEOF

  # Dòng sinh ra có dạng: STARTED|Bus started: rabbitmq://rabbitmq/
  # ('|' trong BRE là ký tự thường, nên pattern phải khớp đúng phần sau dấu gạch)
  if grep -q "^STARTED|Bus started" "$OUT/$c.bus.txt"; then
    ok "$name bus khởi động" "$(grep '^STARTED|' "$OUT/$c.bus.txt" | cut -d'|' -f2-)"
  else
    bad "$name bus KHÔNG khởi động" "không thấy dòng 'Bus started' trong log 20 phút"
  fi
done

echo ""
echo "── D. Không có exception lúc khởi động ───────────────────────────────"
for entry in "${SERVICES[@]}"; do
  name="${entry%%|*}"; c="${entry##*|}"
  # Bỏ qua lỗi nghiệp vụ thoáng qua; chỉ soi lỗi liên quan bus/scheduler/DI
  hits=$(grep '^ERROR|' "$OUT/$c.bus.txt" 2>/dev/null \
         | grep -icE "MassTransit|Scheduler|PayloadNotFound|BusControl|RabbitMQ|InvalidOperation" || true)
  if [ "${hits:-0}" = "0" ]; then
    ok "$name không lỗi bus/scheduler" ""
  else
    bad "$name có $hits lỗi liên quan bus" "$(grep '^ERROR|' "$OUT/$c.bus.txt" | grep -iE 'MassTransit|Scheduler|PayloadNotFound|BusControl|RabbitMQ|InvalidOperation' | head -1 | cut -c1-160)"
  fi
done

echo ""
echo "── E. Riêng TicketService: scheduler phải hoạt động ──────────────────"

# ⚠️ KHÔNG viết `docker exec ... | grep -qx "quartz"`: script bật `set -o pipefail`, mà `grep -q`
# thoát NGAY khi tìm thấy khớp → đóng ống → `rabbitmqctl` ăn SIGPIPE và trả mã khác 0 → pipefail
# lấy mã đó → `if` thấy THẤT BẠI dù queue có thật. Hứng output ra biến rồi mới lọc.
QUARTZ_Q=0
for _ in 1 2 3 4 5; do
  qout=$(docker exec solar-rabbitmq rabbitmqctl list_queues name 2>/dev/null || true)
  if printf '%s\n' "$qout" | grep -qx "quartz"; then
    QUARTZ_Q=1; break
  fi
  sleep 5
done
[ "$QUARTZ_Q" = "1" ] \
  && ok "Queue quartz tồn tại" "AddQuartzConsumers có hiệu lực" \
  || bad "Queue quartz" "không thấy sau 5 lần thử"

# ⚠️ KHÔNG kiểm `qrtz_triggers > 0`: Quartz XOÁ trigger một-lần sau khi nó nổ, nên số này bằng 0
# là hành vi ĐÚNG khi mọi timeout đã kích hoạt xong. Bằng chứng BỀN VỮNG là job definition của
# MassTransit — nó tồn tại từ lần hẹn giờ đầu tiên và không biến mất.
JOB=$(docker exec solar-postgres psql -U postgres -d ticket_db -t -A -c \
  "SELECT COUNT(*) FROM qrtz_job_details WHERE job_class_name LIKE '%MassTransit%';" 2>/dev/null | tr -d '\r ')
[ "${JOB:-0}" -gt 0 ] \
  && ok "qrtz_job_details có job MassTransit" "$JOB job — chứng minh ScheduleMessage đã tới Quartz" \
  || bad "qrtz_job_details" "không có job MassTransit ⇒ chuỗi hẹn giờ chưa từng chạy"

# Kiểm động: tạo saga MỚI rồi soi trigger trong cửa sổ ngắn.
ALERT=$(python3 -c "import uuid;print(uuid.uuid4())")
./scripts/publish-event.sh BatteryAnomalyDetectedV2Event \
  "{\"alertId\":\"$ALERT\",\"batteryAssetId\":\"$(python3 -c 'import uuid;print(uuid.uuid4())')\",\"customerId\":\"e2e46e9c-8926-436a-95da-9568de096214\",\"siteId\":null,\"assetSerialNumber\":\"BUS-REGRESS\",\"anomalyType\":1,\"severity\":3,\"thresholdValue\":80.0,\"actualValue\":95.0,\"unit\":\"°C\",\"detectedAt\":\"$(date -u +%Y-%m-%dT%H:%M:%SZ)\",\"internalResistanceMilliohm\":null,\"cellVoltageDeltaMv\":null,\"environmentalIncidentId\":null}" \
  > "$OUT/e-publish.txt" 2>&1

TRIG=0
for _ in $(seq 1 15); do
  sleep 2
  TRIG=$(docker exec solar-postgres psql -U postgres -d ticket_db -t -A -c "SELECT COUNT(*) FROM qrtz_triggers;" 2>/dev/null | tr -d '\r ')
  [ "${TRIG:-0}" -gt 0 ] && break
done
[ "${TRIG:-0}" -gt 0 ] \
  && ok "Saga mới → qrtz_triggers = $TRIG" "hẹn giờ được nạp vào Quartz theo thời gian thực" \
  || bad "Saga mới không sinh trigger" "qrtz_triggers vẫn 0 sau 30s"

DLQ=$(docker exec solar-rabbitmq rabbitmqctl list_queues name messages 2>/dev/null | grep "_error" | awk '{s+=$2} END{print s+0}')
[ "${DLQ:-0}" = "0" ] \
  && ok "DLQ tổng = 0" "không message nào rơi vào _error sau bản sửa" \
  || bad "DLQ = $DLQ" "có message rơi _error"

{ echo ""; echo "**Tổng: $PASS PASS · $FAIL FAIL**"; } >> "$SUM"
echo ""
echo "═══════════════════════════════════════════════"
echo "  REGRESSION 8 SERVICE: $PASS PASS · $FAIL FAIL"
echo "═══════════════════════════════════════════════"
[ "$FAIL" -eq 0 ]
