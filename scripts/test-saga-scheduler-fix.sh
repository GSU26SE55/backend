#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# Kiểm chứng 2 bản sửa ngày 30/07/2026:
#
#   VĐ1 — Saga timeout: nối đủ 4 mảnh Quartz
#         (AddQuartz · AddPublishMessageScheduler · AddQuartzConsumers · UsePublishMessageScheduler)
#         Bằng chứng ĐẮT nhất: `qrtz_triggers` phải > 0 sau khi có saga hẹn giờ,
#         và không còn PayloadNotFoundException trong log.
#
#   VĐ2 — POST /api/notifications nhắm được người nhận (bỏ [JsonIgnore] + validate Guid.Empty)
#
# ⚠️ solar-rabbitmq ở cổng 15673 (15672 là iot-rabbitmq của dự án khác).
# ─────────────────────────────────────────────────────────────────────────────
set -uo pipefail

GW="${GW:-http://localhost:4001}"
export MQ="${MQ:-http://localhost:15673}"
EV="${EV:?Cần đặt biến EV = thư mục evidence}"
OUT="$EV/09-fixes"
mkdir -p "$OUT"

PASS=0; FAIL=0
SUM="$OUT/_summary.md"; : > "$SUM"
ok()  { PASS=$((PASS+1)); printf '  ✅ %s\n' "$1"; printf '| ✅ PASS | %s | %s |\n' "$1" "${2:-}" >> "$SUM"; }
bad() { FAIL=$((FAIL+1)); printf '  ❌ %s — %s\n' "$1" "${2:-}"; printf '| ❌ FAIL | %s | %s |\n' "$1" "${2:-}" >> "$SUM"; }

{
  echo "# Kiểm chứng 2 bản sửa (30/07/2026)"
  echo ""
  echo "- Thời điểm: $(date '+%Y-%m-%d %H:%M:%S %Z')"
  echo ""
  echo "| KQ | Ca kiểm thử | Bằng chứng |"
  echo "|----|-------------|------------|"
} >> "$SUM"

tq() { docker exec solar-postgres psql -U postgres -d ticket_db      -t -A -c "$1" 2>/dev/null | tr -d '\r '; }
nq() { docker exec solar-postgres psql -U postgres -d notification_db -t -A -c "$1" 2>/dev/null | tr -d '\r '; }
call() {
  local m="$1" p="$2" t="$3" b="$4" f="$5"
  local a=(-s -o "$OUT/$f" -w '%{http_code}' -X "$m" "$GW$p")
  [ "$t" != "-" ] && a+=(-H "Authorization: Bearer $t")
  [ "$b" != "-" ] && a+=(-H 'Content-Type: application/json' -d "$b")
  curl "${a[@]}" 2>/dev/null
}

ADMIN=$(curl -s -X POST "$GW/api/auth/login" -H 'Content-Type: application/json' \
  -d "{\"email\":\"${ADMIN_EMAIL:-admin@yourdomain.com}\",\"password\":\"${ADMIN_PASSWORD:-Admin123@}\"}" \
  | python3 -c "import json,sys;print((json.load(sys.stdin).get('data') or {}).get('tokens',{}).get('accessToken',''))")
[ -n "$ADMIN" ] || { echo "‼️ không đăng nhập được"; exit 1; }

# ═══════════════════════════════════════════════════════════════════════════
echo ""
echo "══ VĐ1 — Saga timeout / Quartz scheduler ═════════════════════════════"
echo ""
echo "── A. Hạ tầng: queue quartz + không còn PayloadNotFound ──────────────"

docker exec solar-rabbitmq rabbitmqctl list_queues name 2>/dev/null > "$OUT/a-queues.txt"
grep -qi "quartz" "$OUT/a-queues.txt" \
  && ok "Queue \`quartz\` được tạo (AddQuartzConsumers có hiệu lực)" "$(grep -i quartz "$OUT/a-queues.txt" | tr '\n' ' ')" \
  || bad "Queue quartz" "không thấy — AddQuartzConsumers chưa chạy"

docker logs solar-ticketservice --since 10m > "$OUT/a-ticketservice.log" 2>&1
if grep -q "PayloadNotFoundException" "$OUT/a-ticketservice.log"; then
  bad "Không còn PayloadNotFoundException" "vẫn thấy trong log 10 phút gần nhất"
else
  ok "Log TicketService KHÔNG còn PayloadNotFoundException" "trước bản sửa: lỗi này lặp liên tục"
fi

python3 - "$OUT/a-ticketservice.log" > "$OUT/a-quartz-endpoint.txt" 2>&1 <<'PYEOF'
import json, sys
found = []
for line in open(sys.argv[1], encoding="utf-8", errors="replace"):
    line = line.strip()
    if not line.startswith("{"):
        continue
    try:
        r = json.loads(line)
    except Exception:
        continue
    m = r.get("Message") or ""
    if "quartz" in m.lower() and "endpoint" in m.lower():
        found.append(m[:160])
for f in dict.fromkeys(found):
    print(f)
PYEOF
[ -s "$OUT/a-quartz-endpoint.txt" ] \
  && ok "MassTransit cấu hình endpoint Quartz" "$(head -1 "$OUT/a-quartz-endpoint.txt")" \
  || bad "Endpoint Quartz" "không thấy dòng cấu hình endpoint trong log"

# ═══════════════════════════════════════════════════════════════════════════
echo ""
echo "── B. Sinh saga thật → Quartz phải nhận trigger ──────────────────────"

TRIG_BEFORE=$(tq "SELECT COUNT(*) FROM qrtz_triggers;")
echo "  qrtz_triggers trước: ${TRIG_BEFORE:-?}"

ALERT_ID=$(python3 -c "import uuid;print(uuid.uuid4())")
BATTERY_ID=$(python3 -c "import uuid;print(uuid.uuid4())")

# BatteryAnomalyDetectedV2Event khởi động AlertTicketSaga → transition đầu tiên có .Schedule(...)
./scripts/publish-event.sh BatteryAnomalyDetectedV2Event \
  "{\"alertId\":\"$ALERT_ID\",\"batteryAssetId\":\"$BATTERY_ID\",\"customerId\":\"e2e46e9c-8926-436a-95da-9568de096214\",\"siteId\":null,\"assetSerialNumber\":\"E2E-FIX-001\",\"anomalyType\":1,\"severity\":3,\"thresholdValue\":80.0,\"actualValue\":99.9,\"unit\":\"°C\",\"detectedAt\":\"$(date -u +%Y-%m-%dT%H:%M:%SZ)\",\"internalResistanceMilliohm\":null,\"cellVoltageDeltaMv\":null,\"environmentalIncidentId\":null}" \
  > "$OUT/b-publish.txt" 2>&1
cat "$OUT/b-publish.txt"

echo "  (chờ saga + Quartz — tối đa 40s)"
TRIG_AFTER=0
for _ in $(seq 1 20); do
  sleep 2
  TRIG_AFTER=$(tq "SELECT COUNT(*) FROM qrtz_triggers;")
  [ "${TRIG_AFTER:-0}" -gt "${TRIG_BEFORE:-0}" ] && break
done

tq "SELECT trigger_name||' | '||trigger_state||' | next_fire='||next_fire_time FROM qrtz_triggers LIMIT 5;" > "$OUT/b-triggers.txt"
echo "  qrtz_triggers sau: ${TRIG_AFTER:-?}"
cat "$OUT/b-triggers.txt"

[ "${TRIG_AFTER:-0}" -gt 0 ] \
  && ok "Quartz NHẬN được lệnh hẹn giờ — qrtz_triggers = $TRIG_AFTER" "trước bản sửa luôn = 0 dù saga chạy suốt" \
  || bad "qrtz_triggers" "vẫn = 0 ⇒ chuỗi hẹn giờ còn đứt"

tq "SELECT current_state||' × '||COUNT(*) FROM alert_ticket_saga_states GROUP BY current_state;" > "$OUT/b-saga-states.txt"
cat "$OUT/b-saga-states.txt"
SAGA_N=$(tq "SELECT COUNT(*) FROM alert_ticket_saga_states WHERE correlation_id='$ALERT_ID' OR alert_id='$ALERT_ID';")
[ "${SAGA_N:-0}" -ge 1 ] \
  && ok "Saga instance được tạo từ event" "" \
  || bad "Saga instance" "không tìm thấy saga cho alertId vừa publish"

# ═══════════════════════════════════════════════════════════════════════════
echo ""
echo "── C. DLQ không tăng thêm sau bản sửa ────────────────────────────────"

curl -su guest:guest "$MQ/api/queues" 2>/dev/null | python3 -c "
import json,sys
d=json.load(sys.stdin)
err={q['name']:q.get('messages',0) for q in d if q['name'].endswith('_error') and q.get('messages',0)>0}
print(json.dumps(err, indent=2))
print('TỔNG:', sum(err.values()))
" > "$OUT/c-dlq-after.txt" 2>&1
cat "$OUT/c-dlq-after.txt"
ok "Ghi nhận DLQ sau bản sửa" "$(grep TỔNG "$OUT/c-dlq-after.txt")"

# ═══════════════════════════════════════════════════════════════════════════
echo ""
echo "══ VĐ2 — POST /api/notifications nhắm được người nhận ════════════════"
echo ""

ME=$(python3 -c "
import json,base64
t='$ADMIN'.split('.')[1]; t+='='*(-len(t)%4)
print(json.loads(base64.urlsafe_b64decode(t))['AccountId'])")
STAMP=$(date +%s)

code=$(call POST /api/notifications "$ADMIN" \
  "{\"userId\":\"$ME\",\"type\":1,\"channel\":4,\"title\":\"FIX-VD2 $STAMP\",\"body\":\"kiem thu userId tu body\",\"entityType\":\"Ticket\"}" \
  "d-create.json")
[ "$code" = "200" ] || [ "$code" = "201" ] \
  && ok "POST /api/notifications với userId trong body → HTTP $code" "" \
  || bad "POST /api/notifications" "HTTP $code — $(head -c 200 "$OUT/d-create.json")"

STORED=$(nq "SELECT user_id FROM notifications WHERE title='FIX-VD2 $STAMP' LIMIT 1;")
[ "$STORED" = "$ME" ] \
  && ok "Bản ghi lưu ĐÚNG userId từ body" "$STORED" \
  || bad "userId bị bỏ qua" "DB lưu '$STORED', kỳ vọng '$ME'"

echo "  (chờ dispatch worker — tối đa 30s)"
for _ in $(seq 1 15); do
  sleep 2
  ST=$(nq "SELECT status FROM notifications WHERE title='FIX-VD2 $STAMP' LIMIT 1;")
  [ "${ST:-1}" != "1" ] && break
done
nq "SELECT status||' | '||COALESCE(failure_reason,'(ok)') FROM notifications WHERE title='FIX-VD2 $STAMP';" > "$OUT/d-dispatch.txt"
cat "$OUT/d-dispatch.txt"
[ "$ST" = "2" ] \
  && ok "Dispatch thành công → Sent (status=2)" "trước bản sửa: luôn Failed/empty_user_id" \
  || bad "Dispatch" "status=$ST — $(cat "$OUT/d-dispatch.txt")"

# userId rỗng phải bị từ chối sớm
code=$(call POST /api/notifications "$ADMIN" \
  '{"userId":"00000000-0000-0000-0000-000000000000","type":1,"channel":4,"title":"phai bi tu choi","body":"x"}' \
  "d-empty.json")
[ "$code" = "400" ] \
  && ok "userId rỗng → 400 (từ chối sớm)" "$(python3 -c "
import json;d=json.load(open('$OUT/d-empty.json'))
e=(d.get('listErrors') or [{}])[0]
print(e.get('detail') or d.get('message',''))" 2>/dev/null | head -c 110)" \
  || bad "userId rỗng" "kỳ vọng 400, nhận $code"

# thiếu hẳn userId cũng phải 400
code=$(call POST /api/notifications "$ADMIN" \
  '{"type":1,"channel":4,"title":"thieu userId","body":"x"}' "d-missing.json")
[ "$code" = "400" ] && ok "thiếu userId → 400" "" || bad "thiếu userId" "kỳ vọng 400, nhận $code"

# quyền: Customer không được tạo
CUST=$(curl -s -X POST "$GW/api/auth/login" -H 'Content-Type: application/json' \
  -d '{"email":"customer.demo@solarbattery.local","password":"Password123@"}' \
  | python3 -c "import json,sys;print((json.load(sys.stdin).get('data') or {}).get('tokens',{}).get('accessToken',''))")
if [ -n "$CUST" ]; then
  code=$(call POST /api/notifications "$CUST" \
    "{\"userId\":\"$ME\",\"type\":1,\"channel\":4,\"title\":\"x\",\"body\":\"x\"}" "d-forbidden.json")
  { [ "$code" = "403" ] || [ "$code" = "401" ]; } \
    && ok "Customer tạo notification → $code" "chỉ Admin chỉ định được người nhận" \
    || bad "Phân quyền tạo notification" "kỳ vọng 401/403, nhận $code"
fi

# ═══════════════════════════════════════════════════════════════════════════
{ echo ""; echo "**Tổng: $PASS PASS · $FAIL FAIL**"; } >> "$SUM"
echo ""
echo "═══════════════════════════════════════════════"
echo "  2 BẢN SỬA: $PASS PASS · $FAIL FAIL"
echo "═══════════════════════════════════════════════"
[ "$FAIL" -eq 0 ]
