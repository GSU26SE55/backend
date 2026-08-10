#!/usr/bin/env bash
# Kiểm hợp đồng API sau các bản sửa E2E — chạy trực tiếp lên stack docker đang chạy.
# Mỗi phép kiểm in PASS/FAIL kèm giá trị đo được, KHÔNG dừng ở lỗi đầu tiên.
#
# Mọi kỳ vọng dưới đây đã được đối chiếu với hành vi THẬT của hệ thống trước khi viết vào, không
# phải chép từ mô tả issue. Chỗ nào kỳ vọng khác trực giác đều có ghi lý do ngay tại đó.
set -uo pipefail

GW="${GW:-http://localhost:4001}"
PASS=0; FAIL=0

ok()    { printf '  \033[32mPASS\033[0m  %s\n' "$1"; PASS=$((PASS+1)); }
bad()   { printf '  \033[31mFAIL\033[0m  %s\n' "$1"; FAIL=$((FAIL+1)); }
note()  { printf '  \033[33mGHI\033[0m   %s\n' "$1"; }
head_() { printf '\n\033[1m== %s\033[0m\n' "$1"; }

expect() { [ "$2" = "$3" ] && ok "$1 (=$3)" || bad "$1 — mong $2, nhận $3"; }

jget() { python3 -c "import sys,json
try:
    d=json.load(sys.stdin)
    for k in '$1'.split('.'):
        if d is None: break
        d = d.get(k) if isinstance(d, dict) else None
    print('' if d is None else d)
except Exception: print('')"; }

login() {
  curl -s --max-time 30 -X POST "$GW/api/auth/login" -H 'Content-Type: application/json' \
    -d "{\"email\":\"$1\",\"password\":\"$2\"}" | jget "data.tokens.accessToken"
}

code() { # code <method> <path> [token] [body]
  local m=$1 p=$2 t=${3:-} b=${4:-}
  local args=(-s -o /dev/null -w '%{http_code}' -X "$m" "$GW$p" --max-time 30)
  [ -n "$t" ] && args+=(-H "Authorization: Bearer $t")
  [ -n "$b" ] && args+=(-H 'Content-Type: application/json' -d "$b")
  curl "${args[@]}"
}

body() {
  local m=$1 p=$2 t=${3:-} b=${4:-}
  local args=(-s -X "$m" "$GW$p" --max-time 30)
  [ -n "$t" ] && args+=(-H "Authorization: Bearer $t")
  [ -n "$b" ] && args+=(-H 'Content-Type: application/json' -d "$b")
  curl "${args[@]}"
}

head_ "Đăng nhập 4 vai"
ADMIN=$(login "admin@yourdomain.com" 'Admin123@')
MANAGER=$(login "manager.demo@solarbattery.local" 'Password123@')
STAFF=$(login "staff.tier1@solarbattery.local" 'Password123@')
CUSTOMER=$(login "customer.demo@solarbattery.local" 'Password123@')
for n in ADMIN MANAGER STAFF CUSTOMER; do
  [ -n "${!n}" ] && ok "có token $n" || bad "KHÔNG lấy được token $n"
done
[ -z "$ADMIN" ] && { echo "Không có token Admin — dừng."; exit 1; }

head_ "GH-774 — thống kê toàn hệ thống chỉ Admin/Manager"
expect "Admin không siteId → 200"    200 "$(code GET /api/battery/dashboard/stats "$ADMIN")"
expect "Manager không siteId → 200"  200 "$(code GET /api/battery/dashboard/stats "$MANAGER")"
expect "Staff không siteId → 403"    403 "$(code GET /api/battery/dashboard/stats "$STAFF")"
expect "Customer không siteId → 403" 403 "$(code GET /api/battery/dashboard/stats "$CUSTOMER")"

# Luật "404 chứ không 403" nhắm vào người BỊ GIỚI HẠN: mục đích là để họ không phân biệt được
# "site không tồn tại" với "site của khách hàng khác" — nếu khác nhau thì endpoint thành công cụ dò.
# Admin/Manager/Staff có scope không giới hạn (spec §34.10.6) nên guard cho qua ngay và siteId lạ
# trả 200 với số liệu rỗng; đó KHÔNG phải lộ lọt vì họ vốn xem được mọi site.
head_ "GH-774 — không phân biệt được 'không tồn tại' với 'của người khác'"
NOPE=$(code GET "/api/battery/dashboard/stats?siteId=00000000-0000-0000-0000-000000000001" "$CUSTOMER")
OTHER_SITE=$(body GET "/api/sites?pageNumber=1&pageSize=1" "$ADMIN" | python3 -c 'import sys,json
try:
  i=(json.load(sys.stdin).get("data") or {}).get("items") or []; print(i[0]["id"] if i else "")
except Exception: print("")')
FOREIGN=$(code GET "/api/battery/dashboard/stats?siteId=$OTHER_SITE" "$CUSTOMER")
expect "Customer + site không tồn tại → 404" 404 "$NOPE"
expect "Customer + site người khác → 404"    404 "$FOREIGN"
[ "$NOPE" = "$FOREIGN" ] && ok "hai ca trả GIỐNG NHAU ⇒ không dò được" \
                         || bad "hai ca khác nhau ($NOPE vs $FOREIGN) ⇒ dò được sự tồn tại"

head_ "GH-776 — introspection phải có khoá riêng"
expect "thiếu X-Introspection-Key → 401" 401 \
  "$(curl -s -o /dev/null -w '%{http_code}' --max-time 20 -X POST "$GW/api/auth/introspect" \
      -H 'Content-Type: application/json' -d '{"token":"khong-quan-trong"}')"

head_ "GH-785 — scope mặc định của thiết bị biên phải gồm EnvironmentalIngest"
# Device code chỉ nhận CHỮ IN HOA, số và gạch ngang (validate của BE).
DEV_CODE="E2E-$(od -An -N3 -tx1 /dev/urandom | tr -d ' \n' | tr 'a-f' 'A-F')"
if [ -n "$OTHER_SITE" ]; then
  SITE_ID="$OTHER_SITE"
  ok "lấy được siteId thật: $SITE_ID"
  CREATED=$(body POST /api/admin/iot-devices "$ADMIN" \
    "{\"deviceCode\":\"$DEV_CODE\",\"displayName\":\"E2E contract check\",\"siteId\":\"$SITE_ID\",\"apiKeyScopes\":15}")
  expect "thiết bị tạo với scope mặc định của FE (15)" 15 "$(printf '%s' "$CREATED" | jget 'data.apiKeyScopes')"
  RAWKEY=$(printf '%s' "$CREATED" | jget 'data.rawApiKey')

  # GH-784 — bốn trường MQTT chỉ có giá trị khi bridge được bật. `.env.Docker` để Mqtt__Enabled=false
  # nên MqttBrokerEndpointProvider trả Disabled và CẢ BỐN cùng null. Kiểm tính NHẤT QUÁN thay vì
  # đòi có giá trị: null lẫn lộn (có host mà thiếu TLS) mới là hỏng.
  HOST=$(printf '%s' "$CREATED" | jget 'data.mqttBrokerHost')
  TLS=$(printf '%s' "$CREATED" | jget 'data.mqttUseTls')
  PREFIX=$(printf '%s' "$CREATED" | jget 'data.mqttTopicPrefix')
  if [ -z "$HOST" ]; then
    note "MQTT bridge đang TẮT (Mqtt__Enabled=false) — host/TLS/prefix cùng rỗng, đúng thiết kế"
    { [ -z "$TLS" ] && [ -z "$PREFIX" ]; } \
      && ok "GH-784 — bốn trường MQTT nhất quán cùng rỗng" \
      || bad "GH-784 — lệch: host rỗng nhưng TLS='$TLS' prefix='$PREFIX'"
  else
    [ -n "$TLS" ] && ok "GH-784 — có mqttUseTls ($TLS)" || bad "GH-784 — có host mà thiếu mqttUseTls"
    [ -n "$PREFIX" ] && ok "GH-784 — có mqttTopicPrefix ($PREFIX)" || bad "GH-784 — có host mà thiếu prefix"
    [ "$PREFIX" = "$(printf '%s' "$PREFIX" | tr 'A-Z' 'a-z')" ] \
      && ok "GH-784 — tiền tố đã chuẩn hoá chữ thường" || bad "GH-784 — tiền tố còn chữ hoa: $PREFIX"
  fi

  head_ "GH-806 — khoá thiết bị chỉ ghi được cho site của chính nó"
  if [ -n "$RAWKEY" ]; then
    # Mốc thời gian phải DUY NHẤT: (site_id, time) là khoá, gửi trùng sẽ ra 500 DbUpdateException
    # — hành vi có sẵn của endpoint ambient, không liên quan phép kiểm này.
    TS=$(python3 -c 'import datetime;print(datetime.datetime.now(datetime.UTC).strftime("%Y-%m-%dT%H:%M:%S.%fZ"))')
    amb() { printf '{"items":[{"siteId":"%s","time":"%s","ambientTemperature":25.0}]}' "$1" "$2"; }
    C_OWN=$(curl -s -o /dev/null -w '%{http_code}' --max-time 30 -X POST "$GW/api/ambient/readings/batch" \
      -H "X-Api-Key: $RAWKEY" -H 'Content-Type: application/json' -d "$(amb "$SITE_ID" "$TS")")
    C_OTHER=$(curl -s -o /dev/null -w '%{http_code}' --max-time 30 -X POST "$GW/api/ambient/readings/batch" \
      -H "X-Api-Key: $RAWKEY" -H 'Content-Type: application/json' \
      -d "$(amb 00000000-0000-0000-0000-000000000002 "$TS")")
    case "$C_OWN" in
      200|201|202) ok "site của chính mình → $C_OWN (scope 15 cho ghi dữ liệu môi trường)" ;;
      *)           bad "site của chính mình → $C_OWN (kỳ vọng 2xx)" ;;
    esac
    expect "site khác → 403" 403 "$C_OTHER"
  else
    bad "không lấy được rawApiKey — bỏ qua phần GH-806"
  fi
else
  bad "không lấy được site nào — bỏ qua GH-785/806"
fi

head_ "GH-788 — đường tải file phải kiểm quyền, presigned giữ đúng scheme"
# PNG 1x1 hợp lệ: purpose TicketAttachment chỉ nhận ảnh/tài liệu/âm thanh, .txt bị từ chối.
python3 -c "
import base64,pathlib
pathlib.Path('/tmp/e2e-contract.png').write_bytes(base64.b64decode(
 'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg=='))"
UP=$(curl -s --max-time 30 -X POST "$GW/api/files/upload" -H "Authorization: Bearer $ADMIN" \
  -F "File=@/tmp/e2e-contract.png;type=image/png" -F "Purpose=2")
FID=$(printf '%s' "$UP" | jget 'data.fileId')
PUB=$(printf '%s' "$UP" | jget 'data.publicUrl')
if [ -n "$FID" ]; then
  ok "upload được file: $FID"
  expect "download có token → 200"      200 "$(code GET "/api/files/$FID/download" "$ADMIN")"
  expect "download KHÔNG token → 401"   401 "$(code GET "/api/files/$FID/download")"
  expect "presigned-url có token → 200" 200 "$(code GET "/api/files/$FID/presigned-url" "$ADMIN")"
  # GH-788 — presigned URL phải giữ ĐÚNG scheme của endpoint. Mặc định của AWS SDK là HTTPS, nên
  # trước bản vá URL ký ra là https:// trong khi MinIO dev chỉ nghe http ⇒ trình duyệt không mở được.
  PRE=$(body GET "/api/files/$FID/presigned-url" "$ADMIN" | jget 'data')
  case "$PRE" in
    http://*)  ok "presigned URL dùng http:// khớp endpoint dev" ;;
    https://*) bad "presigned URL ra https:// trong khi endpoint là http" ;;
    *)         bad "presigned URL không đọc được: $PRE" ;;
  esac
  # publicUrl: rỗng ở prod/k8s (bucket private), CÓ giá trị ở dev vì compose đặt PublicBaseUrl.
  if [ -z "$PUB" ]; then
    note "publicUrl = null ⇒ client buộc đi presigned/download (tư thế prod)"
  else
    note "publicUrl = $PUB (dev: docker-compose.yml đặt PublicBaseUrl, ĐÈ giá trị rỗng ở .env.Docker)"
  fi
else
  bad "upload thất bại: $(printf '%s' "$UP" | head -c 200)"
fi

head_ "GH-792 — dòng đang gửi không được rơi khỏi mọi ô đếm"
BATCH=$(body GET "/api/admin/notifications/batches?pageNumber=1&pageSize=1" "$ADMIN" | python3 -c 'import sys,json
try:
  i=(json.load(sys.stdin).get("data") or {}).get("items") or []; print(i[0]["id"] if i else "")
except Exception: print("")')
if [ -n "$BATCH" ]; then
  body GET "/api/admin/notifications/batches/$BATCH" "$ADMIN" | python3 -c '
import sys,json
d=json.load(sys.stdin).get("data") or {}
t,s,f,p,r = (d.get(k,0) for k in ("totalRows","sentCount","failedCount","pendingCount","readCount"))
# `status` là MỘT trường: dòng đã đọc mang Read và không còn đếm vào Sent nữa, nên
# sent+failed+pending == total KHÔNG phải bất biến chung. Điều phải đúng là: không dòng nào
# rơi khỏi CẢ BỐN ô. (Bất biến chặt hơn được khẳng định ở unit test, nơi kiểm soát được dữ liệu.)
covered = s+f+p+r
print(("  \033[32mPASS\033[0m  " if covered >= t else "  \033[31mFAIL\033[0m  ")
      + f"sent({s})+failed({f})+pending({p})+read({r}) = {covered} phủ hết total({t})")'
else
  note "không có batch nào trong DB — bỏ qua"
fi

head_ "Hạ tầng"
MOSQ=$(docker inspect --format '{{.State.Health.Status}}' solar-mosquitto 2>/dev/null || echo "khong-co")
MQTT_ON=$(grep -E '^Mqtt__Enabled=' .env.Docker 2>/dev/null | cut -d= -f2)
if [ "$MQTT_ON" = "true" ]; then
  expect "healthcheck Mosquitto" "healthy" "$MOSQ"
else
  note "Mosquitto = $MOSQ — MQTT chưa provision ở env này (Mqtt__Enabled=$MQTT_ON, mật khẩu còn là chuỗi giữ chỗ)."
  note "  Healthcheck GH-786 publish CÓ XÁC THỰC nên báo unhealthy là ĐÚNG; không service nào depends_on nó."
fi
DOWN=$(docker compose ps --format '{{.Service}}\t{{.Status}}' | grep -cv "Up" || true)
expect "mọi container đều Up" 0 "$DOWN"

printf '\n\033[1mTỔNG: %d PASS · %d FAIL\033[0m\n' "$PASS" "$FAIL"
[ "$FAIL" -eq 0 ]
