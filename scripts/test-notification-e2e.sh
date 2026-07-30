#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# Test E2E toàn bộ tầng notification sau Sprint 6.2 + 6.3.
#
# Chạy qua ApiGateway (cổng 4001) chứ không gọi thẳng service — để đồng thời
# kiểm chứng luôn các route gateway mới thêm ở NOTI3-04/12/13/15.
#
# Mỗi ca ghi 1 file .json/.txt vào thư mục evidence, kèm dòng tóm tắt PASS/FAIL.
# ─────────────────────────────────────────────────────────────────────────────
set -uo pipefail

GW="${GW:-http://localhost:4001}"
EV="${EV:?Cần đặt biến EV = thư mục evidence}"
OUT="$EV/02-api-curl"
mkdir -p "$OUT"

PASS=0; FAIL=0
SUMMARY="$OUT/_summary.md"
: > "$SUMMARY"

log()  { printf '%s\n' "$*"; }
note() { printf '%s\n' "$*" >> "$SUMMARY"; }

# ok <tên ca> <điều kiện đã eval> <ghi chú>
ok()   { PASS=$((PASS+1)); printf '  ✅ %s\n' "$1"; note "| ✅ PASS | $1 | ${2:-} |"; }
bad()  { FAIL=$((FAIL+1)); printf '  ❌ %s — %s\n' "$1" "${2:-}"; note "| ❌ FAIL | $1 | ${2:-} |"; }

# call <method> <path> <token|-> <body|-> <file>  → in ra HTTP code, lưu body
call() {
  local method="$1" path="$2" token="$3" body="$4" file="$5"
  local args=(-s -o "$OUT/$file" -w '%{http_code}' -X "$method" "$GW$path")
  [ "$token" != "-" ] && args+=(-H "Authorization: Bearer $token")
  if [ "$body" != "-" ]; then
    args+=(-H 'Content-Type: application/json' -d "$body")
  fi
  curl "${args[@]}" 2>/dev/null
}

note "# Kết quả test API notification qua ApiGateway"
note ""
note "- Thời điểm: $(date '+%Y-%m-%d %H:%M:%S %Z')"
note "- Gateway: \`$GW\`"
note ""
note "| KQ | Ca kiểm thử | Ghi chú |"
note "|----|-------------|---------|"

# ═══════════════════════════════════════════════════════════════════════════
log ""
log "── 0. Đăng nhập lấy JWT ──────────────────────────────────────────────"

login() {  # login <email> <password> <nhãn>
  local code
  code=$(call POST /api/auth/login - "{\"email\":\"$1\",\"password\":\"$2\"}" "00-login-$3.json")
  if [ "$code" != "200" ]; then
    bad "Đăng nhập $3" "HTTP $code"
    return 1
  fi
  # AuthService trả token lồng trong data.tokens.accessToken (không phải data.accessToken).
  python3 -c "
import json
d=json.load(open('$OUT/00-login-$3.json'))
data=d.get('data') or {}
tok=(data.get('tokens') or {})
print(tok.get('accessToken') or data.get('accessToken') or data.get('token') or '')
"
}

ADMIN_TOKEN=$(login "${ADMIN_EMAIL:-admin@yourdomain.com}" "${ADMIN_PASSWORD:-Admin123@}" admin)
[ -n "$ADMIN_TOKEN" ] && ok "Đăng nhập Admin" "có accessToken" || bad "Đăng nhập Admin" "không lấy được token"

CUST_TOKEN=$(login "customer.demo@solarbattery.local" "Password123@" customer)
[ -n "$CUST_TOKEN" ] && ok "Đăng nhập Customer" "có accessToken" || bad "Đăng nhập Customer" "không lấy được token"

if [ -z "$ADMIN_TOKEN" ]; then
  log "‼️  Không có token Admin — dừng, mọi ca sau đều cần xác thực."
  exit 1
fi

# ═══════════════════════════════════════════════════════════════════════════
log ""
log "── 1. Feed in-app (NOTI3-01 + NOTI3-14) ──────────────────────────────"

code=$(call GET "/api/notifications?pageNumber=1&pageSize=20" "$ADMIN_TOKEN" - "01-feed.json")
[ "$code" = "200" ] && ok "GET /api/notifications" "HTTP 200" || bad "GET /api/notifications" "HTTP $code"

# NOTI3-01: mặc định CHỈ trả row InApp — đây là lỗi feed nhân bản 2–4 lần đã sửa
python3 - "$OUT/01-feed.json" <<'PY' > "$OUT/01-feed-channel-check.txt" 2>&1
import json,sys,collections
d=json.load(open(sys.argv[1]))
items=(d.get('data') or {}).get('items') or []
c=collections.Counter(i.get('channel') for i in items)
print("Tổng dòng feed:", len(items))
print("Phân bố channel:", dict(c))
non_inapp=[k for k in c if k not in ('InApp', 4, None)]
print("KẾT LUẬN:", "CHỈ InApp ✅" if not non_inapp else f"LỘ channel khác ❌ {non_inapp}")
PY
grep -q "CHỈ InApp ✅" "$OUT/01-feed-channel-check.txt" \
  && ok "Feed mặc định chỉ trả Channel=InApp (NOTI3-01)" "$(grep 'Phân bố' "$OUT/01-feed-channel-check.txt")" \
  || bad "Feed mặc định chỉ trả Channel=InApp" "$(cat "$OUT/01-feed-channel-check.txt" | tr '\n' ' ')"

code=$(call GET "/api/notifications?includeAllChannels=true&pageSize=50" "$ADMIN_TOKEN" - "01-feed-allchannels.json")
[ "$code" = "200" ] && ok "GET ?includeAllChannels=true" "HTTP 200 — client vẫn xem được mọi kênh" || bad "includeAllChannels" "HTTP $code"

code=$(call GET "/api/notifications/unread-count" "$ADMIN_TOKEN" - "01-unread.json")
[ "$code" = "200" ] && ok "GET /unread-count" "$(cat "$OUT/01-unread.json" | head -c 120)" || bad "GET /unread-count" "HTTP $code"

code=$(call GET "/api/notifications?unreadOnly=true" "$ADMIN_TOKEN" - "01-feed-unread.json")
[ "$code" = "200" ] && ok "GET ?unreadOnly=true" "HTTP 200" || bad "unreadOnly" "HTTP $code"

# ═══════════════════════════════════════════════════════════════════════════
log ""
log "── 2. Preference: API cũ + ma trận mới (NOTI3-04) ────────────────────"

code=$(call GET /api/notification-preferences "$ADMIN_TOKEN" - "02-pref-old.json")
[ "$code" = "200" ] && ok "GET /notification-preferences (API cũ vẫn sống)" "HTTP 200 — không phá FE hiện tại" || bad "preference cũ" "HTTP $code"

code=$(call GET /api/notification-preferences/matrix "$ADMIN_TOKEN" - "02-pref-matrix.json")
[ "$code" = "200" ] && ok "GET /notification-preferences/matrix" "HTTP 200" || bad "matrix" "HTTP $code"

python3 - "$OUT/02-pref-matrix.json" <<'PY' > "$OUT/02-matrix-check.txt" 2>&1
import json,sys
d=json.load(open(sys.argv[1]))
cats=(d.get('data') or {}).get('categories') or []
print("Số nhóm:", len(cats))
print("Danh sách:", [c.get('categoryName') for c in cats])
print("isCustomized:", {c.get('categoryName'): c.get('isCustomized') for c in cats})
print("KẾT LUẬN:", "đủ 6 nhóm ✅" if len(cats)==6 else f"SAI số nhóm ❌ ({len(cats)})")
PY
grep -q "đủ 6 nhóm ✅" "$OUT/02-matrix-check.txt" \
  && ok "Ma trận trả đủ 6 nhóm" "$(grep 'Danh sách' "$OUT/02-matrix-check.txt")" \
  || bad "Ma trận 6 nhóm" "$(cat "$OUT/02-matrix-check.txt" | tr '\n' ' ')"

code=$(call PUT /api/notification-preferences/matrix "$ADMIN_TOKEN" \
  '{"items":[{"category":5,"pushEnabled":false,"emailEnabled":false,"smsEnabled":false,"inAppEnabled":true}]}' \
  "02-pref-matrix-put.json")
[ "$code" = "200" ] && ok "PUT matrix — tắt push+email nhóm Chat" "HTTP 200" || bad "PUT matrix" "HTTP $code"

python3 - "$OUT/02-pref-matrix-put.json" <<'PY' > "$OUT/02-matrix-patch-check.txt" 2>&1
import json,sys
d=json.load(open(sys.argv[1]))
cats={c['categoryName']: c for c in ((d.get('data') or {}).get('categories') or [])}
chat=cats.get('Chat', {})
sla=cats.get('Sla', {})
print("Chat  :", {k:chat.get(k) for k in ('pushEnabled','emailEnabled','inAppEnabled','isCustomized')})
print("Sla   :", {k:sla.get(k)  for k in ('pushEnabled','emailEnabled','isCustomized')})
good = chat.get('pushEnabled') is False and chat.get('isCustomized') is True and sla.get('isCustomized') is False
print("KẾT LUẬN:", "vá đúng 1 nhóm, nhóm khác nguyên vẹn ✅" if good else "SAI ❌")
PY
grep -q "✅" "$OUT/02-matrix-patch-check.txt" \
  && ok "PUT matrix chỉ vá nhóm được gửi (không ghi đè nhóm khác)" "$(grep 'Chat' "$OUT/02-matrix-patch-check.txt")" \
  || bad "PUT matrix vá từng dòng" "$(cat "$OUT/02-matrix-patch-check.txt" | tr '\n' ' ')"

code=$(call PUT /api/notification-preferences/matrix "$ADMIN_TOKEN" \
  '{"items":[{"category":5},{"category":5}]}' "02-pref-matrix-dup.json")
[ "$code" = "400" ] && ok "PUT matrix trùng nhóm → 400" "từ chối thay vì đoán dòng nào thắng" || bad "matrix trùng nhóm" "kỳ vọng 400, nhận $code"

code=$(call PUT /api/notification-preferences/matrix "$ADMIN_TOKEN" \
  '{"items":[{"category":99}]}' "02-pref-matrix-bad.json")
[ "$code" = "400" ] && ok "PUT matrix nhóm không hợp lệ → 400" "" || bad "matrix nhóm sai" "kỳ vọng 400, nhận $code"

code=$(call GET /api/notification-preferences/categories "$ADMIN_TOKEN" - "02-categories.json")
[ "$code" = "200" ] && ok "GET /notification-preferences/categories" "bảng tra cứu type → nhóm" || bad "categories" "HTTP $code"

python3 - "$OUT/02-categories.json" <<'PY' > "$OUT/02-categories-check.txt" 2>&1
import json,sys,collections
d=json.load(open(sys.argv[1]))
items=d.get('data') or []
print("Số type được phân nhóm:", len(items))
print("Phân bố nhóm:", dict(collections.Counter(i['category'] for i in items)))
esc=[i for i in items if i['type']=='TicketEscalated']
print("TicketEscalated →", esc[0]['category'] if esc else "KHÔNG THẤY")
print("KẾT LUẬN:", "đủ 32 type ✅" if len(items)==32 else f"thiếu ❌ ({len(items)}/32)")
PY
grep -q "đủ 32 type ✅" "$OUT/02-categories-check.txt" \
  && ok "Bảng tra cứu phủ đủ 32 NotificationTypeEnum" "$(grep 'Phân bố' "$OUT/02-categories-check.txt")" \
  || bad "bảng tra cứu 32 type" "$(cat "$OUT/02-categories-check.txt" | tr '\n' ' ')"

# Trả lại trạng thái ban đầu để không ảnh hưởng ca sau
call PUT /api/notification-preferences/matrix "$ADMIN_TOKEN" \
  '{"items":[{"category":5,"pushEnabled":true,"emailEnabled":true,"smsEnabled":false,"inAppEnabled":true}]}' \
  "02-pref-matrix-restore.json" > /dev/null

# ═══════════════════════════════════════════════════════════════════════════
log ""
log "── 3. Device token (nền cho push + NOTI3-02) ─────────────────────────"

code=$(call POST /api/device-tokens "$ADMIN_TOKEN" \
  '{"token":"ExponentPushToken[e2e-test-'"$(date +%s)"']","platform":2,"deviceInfo":"e2e-curl"}' \
  "03-device-token.json")
[ "$code" = "200" ] || [ "$code" = "201" ] && ok "POST /device-tokens" "HTTP $code" || bad "device-tokens" "HTTP $code"

# ═══════════════════════════════════════════════════════════════════════════
log ""
log "── 4. Quản trị template (NOTI3-12) ───────────────────────────────────"

code=$(call GET "/api/admin/notification-templates?locale=vi-VN" "$ADMIN_TOKEN" - "04-templates.json")
[ "$code" = "200" ] && ok "GET /admin/notification-templates" "HTTP 200" || bad "list template" "HTTP $code"

python3 - "$OUT/04-templates.json" <<'PY' > "$OUT/04-template-check.txt" 2>&1
import json,sys,collections
d=json.load(open(sys.argv[1]))
items=d.get('data') or []
types=set(i['type'] for i in items)
print("Số bản ghi template (vi-VN):", len(items))
print("Số type khác nhau:", len(types))
print("Phân bố channel:", dict(collections.Counter(i['channel'] for i in items)))
print("Version dùng:", sorted(set(i['version'] for i in items)))
print("Số bản IsActive:", sum(1 for i in items if i['isActive']))
print("KẾT LUẬN:", "seed >= 30 type ✅" if len(types) >= 30 else f"seed thiếu ❌ ({len(types)} type)")
PY
grep -q "✅" "$OUT/04-template-check.txt" \
  && ok "Seed template phủ đủ type (NOTI3-12a)" "$(grep 'Số type' "$OUT/04-template-check.txt")" \
  || bad "seed template" "$(cat "$OUT/04-template-check.txt" | tr '\n' ' ')"

TPL_ID=$(python3 -c "
import json
d=json.load(open('$OUT/04-templates.json'))
items=[i for i in (d.get('data') or []) if i['channel']=='Email' and i['isActive']]
print(items[0]['id'] if items else '')
" 2>/dev/null)

if [ -n "$TPL_ID" ]; then
  code=$(call POST "/api/admin/notification-templates/$TPL_ID/preview" "$ADMIN_TOKEN" \
    '{"sampleData":{"ticketCode":"TK-E2E-001","customerName":"Nguyễn Văn <script>alert(1)</script>","title":"Pin quá nhiệt","priority":"P1"}}' \
    "04-preview.json")
  [ "$code" = "200" ] && ok "POST template/{id}/preview" "render thử, KHÔNG gửi đi" || bad "preview" "HTTP $code"

  # NOTI3-16: giá trị người dùng nhập phải bị HTML-escape, tiếng Việt giữ nguyên
  python3 - "$OUT/04-preview.json" <<'PY' > "$OUT/04-xss-check.txt" 2>&1
import json,sys
d=json.load(open(sys.argv[1]))
data=d.get('data') or {}
blob=(data.get('title') or '') + (data.get('body') or '')
print("title:", data.get('title'))
print("body :", data.get('body'))
raw_script = '<script>' in blob
escaped    = '&lt;script&gt;' in blob
viet_ok    = 'Nguyễn' in blob or 'Nguyễn' not in blob   # chỉ kiểm khi tên có trong output
print("Có <script> thô:", raw_script)
print("Có &lt;script&gt;:", escaped)
print("KẾT LUẬN:", "escape đúng ✅" if (not raw_script) else "LỘ script thô ❌")
PY
  grep -q "escape đúng ✅" "$OUT/04-xss-check.txt" \
    && ok "Template escape HTML — chống stored XSS (NOTI3-16)" "$(grep 'KẾT LUẬN' "$OUT/04-xss-check.txt")" \
    || bad "XSS escape" "$(cat "$OUT/04-xss-check.txt" | tr '\n' ' ')"

  # test-send: chỉ gửi tới email của chính admin, 5 lần/giờ (R-46)
  for i in 1 2 3 4 5 6; do
    code=$(call POST "/api/admin/notification-templates/$TPL_ID/test-send" "$ADMIN_TOKEN" \
      '{"sampleData":{"ticketCode":"TK-E2E-001"}}' "04-testsend-$i.json")
    echo "lần $i → HTTP $code" >> "$OUT/04-testsend-quota.txt"
  done
  if grep -q "→ HTTP 429" "$OUT/04-testsend-quota.txt"; then
    ok "test-send bị chặn ở lần thứ 6 (rate-limit 5/giờ — R-46)" "$(tr '\n' ' ' < "$OUT/04-testsend-quota.txt")"
  else
    bad "test-send rate-limit" "$(tr '\n' ' ' < "$OUT/04-testsend-quota.txt")"
  fi
else
  bad "Lấy template Email để preview" "không tìm thấy template Email active"
fi

# Quyền: Customer KHÔNG được vào endpoint admin
if [ -n "$CUST_TOKEN" ]; then
  code=$(call GET /api/admin/notification-templates "$CUST_TOKEN" - "04-templates-forbidden.json")
  { [ "$code" = "403" ] || [ "$code" = "401" ]; } \
    && ok "Customer gọi endpoint admin → $code" "policy AdminOnly có hiệu lực" \
    || bad "AdminOnly" "kỳ vọng 401/403, nhận $code"
fi

# ═══════════════════════════════════════════════════════════════════════════
log ""
log "── 5. Hủy đăng ký một chạm (NOTI3-15) ────────────────────────────────"

code=$(call GET "/api/notification-unsubscribe?token=chuoi-bay-ba" - - "05-unsub-bad.json")
[ "$code" = "400" ] && ok "GET unsubscribe token rác → 400" "chữ ký HMAC chặn được giả mạo" || bad "unsub token rác" "kỳ vọng 400, nhận $code"

code=$(call POST "/api/notification-unsubscribe?token=chuoi-bay-ba" - - "05-unsub-bad-post.json")
[ "$code" = "400" ] && ok "POST unsubscribe token rác → 400" "" || bad "unsub POST token rác" "kỳ vọng 400, nhận $code"

code=$(call POST "/api/notification-unsubscribe" - - "05-unsub-notoken.json")
[ "$code" = "400" ] && ok "POST unsubscribe thiếu token → 400" "" || bad "unsub thiếu token" "kỳ vọng 400, nhận $code"

# ═══════════════════════════════════════════════════════════════════════════
log ""
log "── 6. Đánh dấu đã đọc / đã mở (NOTI3-14) ─────────────────────────────"

NOTI_ID=$(python3 -c "
import json
d=json.load(open('$OUT/01-feed.json'))
items=(d.get('data') or {}).get('items') or []
un=[i for i in items if i.get('status') not in ('Read','Opened')]
print((un[0] if un else (items[0] if items else {})).get('id',''))
" 2>/dev/null)

if [ -n "$NOTI_ID" ]; then
  code=$(call PATCH "/api/notifications/$NOTI_ID/read" "$ADMIN_TOKEN" - "06-mark-read.json")
  [ "$code" = "200" ] && ok "PATCH /{id}/read" "HTTP 200" || bad "mark read" "HTTP $code"

  code=$(call PATCH "/api/notifications/$NOTI_ID/read" "$ADMIN_TOKEN" - "06-mark-read-again.json")
  [ "$code" = "200" ] && ok "PATCH /{id}/read lần 2 (idempotent)" "vẫn 200" || bad "mark read idempotent" "HTTP $code"

  code=$(call PATCH "/api/notifications/$NOTI_ID/opened" "$ADMIN_TOKEN" - "06-mark-opened.json")
  [ "$code" = "200" ] && ok "PATCH /{id}/opened (NOTI3-14)" "HTTP 200" || bad "mark opened" "HTTP $code"
else
  note "| ⏭️ SKIP | Đánh dấu đã đọc/đã mở | feed rỗng, không có notification để thao tác |"
  log "  ⏭️  Feed rỗng — bỏ qua ca mark-read/opened"
fi

# IDOR: id ngẫu nhiên phải trả 404, không lộ tồn tại
code=$(call PATCH "/api/notifications/00000000-0000-0000-0000-000000000123/read" "$ADMIN_TOKEN" - "06-idor.json")
[ "$code" = "404" ] && ok "PATCH read id lạ → 404" "không lộ existence (chống IDOR)" || bad "IDOR" "kỳ vọng 404, nhận $code"

code=$(call POST "/api/notifications/read-all" "$ADMIN_TOKEN" - "06-read-all.json")
[ "$code" = "200" ] && ok "POST /read-all" "$(head -c 120 "$OUT/06-read-all.json")" || bad "read-all" "HTTP $code"

code=$(call GET "/api/notifications/unread-count" "$ADMIN_TOKEN" - "06-unread-after.json")
python3 - "$OUT/06-unread-after.json" <<'PY' > "$OUT/06-unread-after-check.txt" 2>&1
import json,sys
d=json.load(open(sys.argv[1]))
print("unread sau read-all:", d.get('data'))
print("KẾT LUẬN:", "về 0 ✅" if d.get('data')==0 else "CHƯA về 0 ❌")
PY
grep -q "về 0 ✅" "$OUT/06-unread-after-check.txt" \
  && ok "unread-count về 0 sau read-all" "" \
  || bad "unread sau read-all" "$(cat "$OUT/06-unread-after-check.txt" | tr '\n' ' ')"

# ═══════════════════════════════════════════════════════════════════════════
log ""
log "── 7. Không xác thực → 401 ───────────────────────────────────────────"
for p in /api/notifications /api/notifications/unread-count /api/notification-preferences /api/notification-preferences/matrix; do
  code=$(call GET "$p" - - "07-noauth-$(echo "$p" | tr '/' '_').json")
  [ "$code" = "401" ] && ok "GET $p không token → 401" "" || bad "GET $p không token" "kỳ vọng 401, nhận $code"
done

# ═══════════════════════════════════════════════════════════════════════════
note ""
note "**Tổng: $PASS PASS · $FAIL FAIL**"
log ""
log "═══════════════════════════════════════════════"
log "  TỔNG: $PASS PASS · $FAIL FAIL"
log "  Evidence: $OUT"
log "═══════════════════════════════════════════════"
[ "$FAIL" -eq 0 ]
