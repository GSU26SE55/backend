#!/usr/bin/env bash
#
# E2E Sprint 6.4 — Nhóm người nhận & gửi thông báo hàng loạt.
#
# Chạy:  bash backend/tools/e2e/notification-groups.sh
# Yêu cầu: docker compose đang chạy (postgres + authservice + notificationservice + apigateway).
#
# Mọi thứ script tạo ra đều mang tiền tố E2E64- và được dọn ở cuối, kể cả khi có phép kiểm thất
# bại (bẫy EXIT). Dữ liệu sẵn có của người dùng KHÔNG bị đụng tới.
#
# ─────────────────────────────────────────────────────────────────────────────────────────────
# BỐN BẪY ĐÃ TRẢ GIÁ KHI VIẾT BỘ NÀY — đừng lặp lại:
#
#   1. `${4:-$TOKEN}` rơi về giá trị mặc định cả khi tham số là CHUỖI RỖNG. Phép kiểm "gọi API
#      không kèm token" vì thế vẫn gửi token thật và báo 200 — trông y như một lỗ hổng xác thực.
#      Phải dùng `${4-$TOKEN}` (không có dấu hai chấm): chỉ rơi về khi tham số CHƯA ĐẶT.
#
#   2. `${t:+-H "Authorization: Bearer $t"}` bị bash tách từ theo khoảng trắng, thành nhiều tham
#      số rời mà curl bỏ qua. Phải viết nhánh if/else tường minh.
#
#   3. `psql -t -A` với `RETURNING id` in ra CẢ dòng `INSERT 0 1`. Không `| head -1` thì biến
#      chứa hai dòng và mọi câu SQL dùng nó sau đó đều vỡ.
#
#   4. Nội suy chuỗi có dấu nháy vào JSON bằng shell rất dễ sinh JSON hỏng (id rỗng → lỗi
#      "could not be converted to System.Guid" trông như lỗi validate của server). Payload nào
#      phức tạp thì dựng bằng python rồi gửi qua `--data-binary @file`.
#
#   5. `psql ... 2>/dev/null` ở đường DỌN DẸP nuốt luôn lỗi. Đã trả giá: lệnh xoá
#      `notification_audit_logs` bị trigger append-only từ chối, nhưng stderr bị ẩn nên trông như
#      xoá thành công, và rò rỉ tích luỹ âm thầm. Dọn dẹp phải LỘ lỗi ra.
#
#   6. Mọi dữ liệu script tạo ra PHẢI mang tiền tố `E2E64` — kể cả chuỗi biên như "tên đúng 128 ký
#      tự". Thiếu tiền tố thì bẫy dọn không quét được, và lần chạy SAU hỏng ở một phép kiểm chẳng
#      liên quan (409 trùng tên), rất khó truy ngược. Mục Y bắt lớp lỗi này tự động.
# ─────────────────────────────────────────────────────────────────────────────────────────────

set -uo pipefail

BASE=${E2E_BASE:-http://localhost:4001}
ADMIN_EMAIL=${E2E_ADMIN_EMAIL:-admin@solars.io.vn}
ADMIN_PASSWORD=${E2E_ADMIN_PASSWORD:-Pasword123@}
MANAGER_EMAIL=${E2E_MANAGER_EMAIL:-manager@solars.io.vn}
MANAGER_PASSWORD=${E2E_MANAGER_PASSWORD:-Password123@}
PG=${E2E_PG_CONTAINER:-solar-postgres}
DB=${E2E_DB:-notification_db}

WORK=$(mktemp -d)
PASS=0
FAIL=0
declare -a FAILED_NAMES=()

PSQL() { docker exec "$PG" psql -U postgres -d "$DB" -t -A -c "$1" 2>/dev/null; }

cleanup() {
  # ⚠️ KHÔNG dọn notification_audit_logs. Bảng đó có trigger
  # `notification_audit_logs_append_only_soft` CHẶN cả DELETE lẫn UPDATE — vết audit là bất biến
  # theo thiết kế, và đó là thiết kế đúng. Thử xoá sẽ bị Postgres từ chối; nếu lệnh lại bị ẩn
  # stderr thì thất bại đó trôi qua không ai biết (đúng cái đã xảy ra khi viết bộ này).
  #
  # Hệ quả: mỗi lượt chạy để lại ~16 dòng audit. Đó KHÔNG phải rác — chúng là bản ghi trung thực
  # rằng các thao tác này đã thực sự xảy ra. Phép kiểm cuối vì vậy chỉ đòi các bảng NGHIỆP VỤ trở
  # về đúng nền, và báo riêng mức tăng của bảng audit thay vì lờ đi.
  #
  # `notification_audit_outbox` là bảng VẬN CHUYỂN (dòng đã `processed_at` là dòng hết việc) nên
  # xoá được — nhưng cũng không đụng, để giữ cặp log↔outbox nguyên vẹn cho ai đi soi sau này.
  #
  # KHÔNG ẩn stderr ở đây: dọn mà thất bại im lặng thì rò rỉ tích luỹ âm thầm, đúng lớp lỗi đã mắc.
  local err
  for stmt in \
    "DELETE FROM notifications WHERE title LIKE 'E2E64%'" \
    "DELETE FROM notification_batch_targets WHERE batch_id IN (SELECT id FROM notification_batches WHERE title LIKE 'E2E64%')" \
    "DELETE FROM notification_batches WHERE title LIKE 'E2E64%'" \
    "DELETE FROM notification_group_members WHERE group_id IN (SELECT id FROM notification_groups WHERE name LIKE 'E2E64%')" \
    "DELETE FROM notification_groups WHERE name LIKE 'E2E64%'"
  do
    err=$(docker exec "$PG" psql -U postgres -d "$DB" -c "$stmt" 2>&1 >/dev/null)
    [ -n "$err" ] && printf '\033[31m  ! dọn dẹp lỗi:\033[0m %s\n     %s\n' "$stmt" "$err" >&2
  done
  rm -rf "$WORK"
}

trap cleanup EXIT

# call METHOD PATH [BODY] [TOKEN] — trả body JSON
call() {
  local m=$1 p=$2 b=${3:-} t=${4-$TOKEN}
  if [ -n "$b" ] && [ -n "$t" ]; then
    curl -s -X "$m" "$BASE$p" -H "Authorization: Bearer $t" -H 'Content-Type: application/json' --data-binary "$b"
  elif [ -n "$t" ]; then
    curl -s -X "$m" "$BASE$p" -H "Authorization: Bearer $t"
  elif [ -n "$b" ]; then
    curl -s -X "$m" "$BASE$p" -H 'Content-Type: application/json' --data-binary "$b"
  else
    curl -s -X "$m" "$BASE$p"
  fi
}

# code METHOD PATH [BODY] [TOKEN] — trả mã HTTP. Truyền "" cho TOKEN để thử KHÔNG xác thực.
code() {
  local m=$1 p=$2 b=${3:-} t=${4-$TOKEN}
  if [ -n "$b" ] && [ -n "$t" ]; then
    curl -s -o /dev/null -w '%{http_code}' -X "$m" "$BASE$p" -H "Authorization: Bearer $t" -H 'Content-Type: application/json' --data-binary "$b"
  elif [ -n "$t" ]; then
    curl -s -o /dev/null -w '%{http_code}' -X "$m" "$BASE$p" -H "Authorization: Bearer $t"
  elif [ -n "$b" ]; then
    curl -s -o /dev/null -w '%{http_code}' -X "$m" "$BASE$p" -H 'Content-Type: application/json' --data-binary "$b"
  else
    curl -s -o /dev/null -w '%{http_code}' -X "$m" "$BASE$p"
  fi
}

# jq_ FIELD_PATH — đọc field lồng nhau: jq_ statusCode | jq_ data.added
jq_() {
  python3 -c '
import sys, json
try:
    d = json.load(sys.stdin)
except Exception:
    print("PARSE_ERROR"); raise SystemExit
for k in sys.argv[1].split("."):
    if not isinstance(d, dict): d = None; break
    d = d.get(k)
    if d is None: break
print(d)' "$1"
}

check() {
  if [ "$2" = "$3" ]; then
    PASS=$((PASS + 1)); printf '  \033[32m✓\033[0m %-62s %s\n' "$1" "$3"
  else
    FAIL=$((FAIL + 1)); FAILED_NAMES+=("$1")
    printf '  \033[31m✗\033[0m %-62s kỳ vọng=%s thực tế=%s\n' "$1" "$2" "$3"
  fi
}

section() { printf '\n\033[1m── %s\033[0m\n' "$1"; }

# ── Đăng nhập ────────────────────────────────────────────────────────────────────────────────
login() {
  python3 -c '
import json, sys
print(json.dumps({"email": sys.argv[1], "password": sys.argv[2]}))' "$1" "$2" > "$WORK/login.json"
  curl -s -X POST "$BASE/api/auth/login" -H 'Content-Type: application/json' \
    --data-binary @"$WORK/login.json" \
  | python3 -c '
import sys, json
d = json.load(sys.stdin)
print(((d.get("data") or {}).get("tokens") or {}).get("accessToken", ""))'
}

TOKEN=$(login "$ADMIN_EMAIL" "$ADMIN_PASSWORD")
MGRTOKEN=$(login "$MANAGER_EMAIL" "$MANAGER_PASSWORD")

if [ -z "$TOKEN" ]; then
  echo "✗ Không đăng nhập được tài khoản Admin — kiểm tra docker compose và E2E_ADMIN_* ." >&2
  exit 1
fi

printf '\033[1mE2E Sprint 6.4 — Nhóm người nhận & gửi hàng loạt\033[0m\n'
printf 'Gốc: %s\n' "$BASE"

# Ảnh chụp nền. Phép kiểm cuối đòi các bảng NGHIỆP VỤ trở về đúng đây — đó là thứ tự bắt được
# lớp lỗi "script tạo dữ liệu nhưng quên dọn", vốn chỉ lộ ra ở lần chạy SAU và rất khó truy.
snapshot_business() {
  PSQL "SELECT
      (SELECT count(*) FROM notifications)
    ||'|'||(SELECT count(*) FROM notification_groups)
    ||'|'||(SELECT count(*) FROM notification_group_members)
    ||'|'||(SELECT count(*) FROM notification_batches)
    ||'|'||(SELECT count(*) FROM notification_batch_targets)
    ||'|'||(SELECT count(*) FROM account_read_models)
    ||'|'||(SELECT count(*) FROM notification_templates)" | head -1
}
snapshot_audit() {
  PSQL "SELECT (SELECT count(*) FROM notification_audit_logs)||'|'||(SELECT count(*) FROM notification_audit_outbox)" | head -1
}
BASELINE_BUSINESS=$(snapshot_business)
BASELINE_AUDIT=$(snapshot_audit)

# ── A. Xác thực & phân quyền ─────────────────────────────────────────────────────────────────
section "A. Xác thực & phân quyền"
check "Không token → 401"                 401 "$(code GET /api/admin/notification-groups '' '')"
check "Token rác → 401"                   401 "$(code GET /api/admin/notification-groups '' 'rac.rac.rac')"
check "Manager xem nhóm → 403"            403 "$(code GET /api/admin/notification-groups '' "$MGRTOKEN")"
check "Manager gửi hàng loạt → 403"       403 "$(code POST /api/admin/notifications/broadcast '{}' "$MGRTOKEN")"
check "Manager xem lịch sử gửi → 403"     403 "$(code GET /api/admin/notifications/batches '' "$MGRTOKEN")"
check "Admin xem nhóm → 200"              200 "$(code GET /api/admin/notification-groups)"

# ── B. Seeder & số người nhận ────────────────────────────────────────────────────────────────
section "B. Nhóm hệ thống & số người nhận"
LIST=$(call GET "/api/admin/notification-groups?pageSize=100")
check "Có đủ 4 nhóm hệ thống" 4 \
  "$(echo "$LIST" | python3 -c 'import sys,json;print(sum(1 for g in json.load(sys.stdin)["data"]["items"] if g["isSystem"]))')"
check "Nhóm hệ thống đều kind=Role(2)" True \
  "$(echo "$LIST" | python3 -c 'import sys,json;print(all(g["kind"]==2 for g in json.load(sys.stdin)["data"]["items"] if g["isSystem"]))')"
check "Nhóm hệ thống xếp trước" True \
  "$(echo "$LIST" | python3 -c 'import sys,json;i=[g["isSystem"] for g in json.load(sys.stdin)["data"]["items"]];print(i==sorted(i,reverse=True))')"
for R in Admin Manager Staff Customer; do
  API=$(echo "$LIST" | python3 -c "import sys,json;print(next(g['memberCount'] for g in json.load(sys.stdin)['data']['items'] if g.get('roleFilter')=='$R'))")
  DBN=$(PSQL "SELECT count(*) FROM account_read_models WHERE NOT is_deleted AND is_active AND lower(role)=lower('$R')" | head -1)
  check "memberCount nhóm $R khớp DB" "$DBN" "$API"
done

# ── C. CRUD nhóm ─────────────────────────────────────────────────────────────────────────────
section "C. CRUD nhóm"
GID=$(call POST /api/admin/notification-groups '{"name":"E2E64-Trực sự cố","description":"nhóm test"}' | jq_ data)
check "Tạo nhóm → có Id trả về" True "$([ -n "$GID" ] && [ "$GID" != "None" ] && echo True || echo False)"
check "Trùng tên y hệt → 409"             409 "$(code POST /api/admin/notification-groups '{"name":"E2E64-Trực sự cố"}')"
check "Trùng tên khác hoa-thường → 409"   409 "$(code POST /api/admin/notification-groups '{"name":"  e2e64-TRỰC SỰ CỐ  "}')"
check "Tên rỗng → 400"                    400 "$(code POST /api/admin/notification-groups '{"name":"   "}')"
# Tên biên PHẢI mang tiền tố E2E64 để bẫy dọn dẹp quét được — thiếu tiền tố thì nhóm 128 ký tự
# còn lại trong DB và lần chạy sau sẽ đụng 409 ở đúng phép kiểm này.
python3 -c 'import json;print(json.dumps({"name":"E2E64"+"x"*124}))' > "$WORK/n129.json"
check "Tên 129 ký tự → 400"               400 "$(code POST /api/admin/notification-groups "$(cat "$WORK/n129.json")")"
python3 -c 'import json;print(json.dumps({"name":"E2E64"+"x"*123}))' > "$WORK/n128.json"
check "Tên đúng 128 ký tự → chấp nhận"    201 "$(code POST /api/admin/notification-groups "$(cat "$WORK/n128.json")")"
check "Nhóm mới luôn kind=Static(1)"      1   "$(call GET "/api/admin/notification-groups/$GID" | jq_ data.kind)"
check "Nhóm mới isSystem=false"           False "$(call GET "/api/admin/notification-groups/$GID" | jq_ data.isSystem)"
check "GET id không tồn tại → 404"        404 "$(code GET /api/admin/notification-groups/00000000-0000-0000-0000-000000000123)"
check "Sửa tên → 200"                     200 "$(code PUT "/api/admin/notification-groups/$GID" '{"name":"E2E64-Trực cuối tuần"}')"
check "Đổi hoa-thường CHÍNH NÓ → 200"     200 "$(code PUT "/api/admin/notification-groups/$GID" '{"name":"E2E64-TRỰC CUỐI TUẦN"}')"

SYSM=$(PSQL "SELECT id FROM notification_groups WHERE role_filter='Manager'" | head -1)
SYSS=$(PSQL "SELECT id FROM notification_groups WHERE role_filter='Staff'" | head -1)
check "Sửa nhóm hệ thống → 409"           409 "$(code PUT "/api/admin/notification-groups/$SYSM" '{"name":"đổi bậy"}')"
check "Xoá nhóm hệ thống → 409"           409 "$(code DELETE "/api/admin/notification-groups/$SYSM")"

# ── D. Thành viên ────────────────────────────────────────────────────────────────────────────
section "D. Thành viên"
MGR=$(PSQL "SELECT id FROM account_read_models WHERE role='Manager' AND is_active LIMIT 1" | head -1)
ST1=$(PSQL "SELECT id FROM account_read_models WHERE role='Staff' AND is_active ORDER BY email LIMIT 1" | head -1)
DEAD=$(PSQL "SELECT id FROM account_read_models WHERE NOT is_active AND NOT is_deleted LIMIT 1" | head -1)
GHOST=00000000-0000-0000-0000-0000000000ff

python3 -c '
import json, sys
print(json.dumps({"userIds": [sys.argv[1], sys.argv[2], sys.argv[2], sys.argv[3], sys.argv[4]]}))' \
  "$MGR" "$ST1" "$GHOST" "$DEAD" > "$WORK/add.json"
ADD=$(call POST "/api/admin/notification-groups/$GID/members" "$(cat "$WORK/add.json")")
check "Thêm hàng loạt → 200"              200 "$(echo "$ADD" | jq_ statusCode)"
check "  added = 3 (id lặp bị gộp)"       3 "$(echo "$ADD" | jq_ data.added)"
check "  unknownAccounts = 1 (id ma)"     1 "$(echo "$ADD" | jq_ data.unknownAccounts)"
check "  memberCount = 2 (loại người ngừng)" 2 "$(echo "$ADD" | jq_ data.memberCount)"

python3 -c 'import json,sys;print(json.dumps({"userIds":[sys.argv[1]]}))' "$MGR" > "$WORK/again.json"
check "Thêm lại người đã có → alreadyMembers=1" 1 \
  "$(call POST "/api/admin/notification-groups/$GID/members" "$(cat "$WORK/again.json")" | jq_ data.alreadyMembers)"
check "Thêm vào nhóm vai trò → 409"       409 "$(code POST "/api/admin/notification-groups/$SYSM/members" "$(cat "$WORK/again.json")")"
check "Bỏ khỏi nhóm vai trò → 409"        409 "$(code DELETE "/api/admin/notification-groups/$SYSM/members/$MGR")"
check "userIds rỗng → 400"                400 "$(code POST "/api/admin/notification-groups/$GID/members" '{"userIds":[]}')"
check "userIds chứa Guid rỗng → 400"      400 "$(code POST "/api/admin/notification-groups/$GID/members" '{"userIds":["00000000-0000-0000-0000-000000000000"]}')"
python3 -c 'import json,uuid;print(json.dumps({"userIds":[str(uuid.uuid4()) for _ in range(501)]}))' > "$WORK/n501.json"
check "Thêm 501 người → 400 (trần 500)"   400 "$(code POST "/api/admin/notification-groups/$GID/members" "$(cat "$WORK/n501.json")")"

check "Liệt kê thành viên: 3 dòng"        3 "$(call GET "/api/admin/notification-groups/$GID/members?pageSize=50" | jq_ data.totalItems)"
check "activeOnly=true → 2 dòng"          2 "$(call GET "/api/admin/notification-groups/$GID/members?activeOnly=true" | jq_ data.totalItems)"
check "Thành viên nhóm vai trò suy ra được" True \
  "$([ "$(call GET "/api/admin/notification-groups/$SYSM/members" | jq_ data.totalItems)" -ge 1 ] && echo True || echo False)"
check "  addedAt = null (không có dòng thật)" None \
  "$(call GET "/api/admin/notification-groups/$SYSM/members" | python3 -c 'import sys,json;print(json.load(sys.stdin)["data"]["items"][0]["addedAt"])')"
check "Bỏ 1 thành viên → 200"             200 "$(code DELETE "/api/admin/notification-groups/$GID/members/$ST1")"
check "Bỏ lại chính người đó → 404"       404 "$(code DELETE "/api/admin/notification-groups/$GID/members/$ST1")"
call POST "/api/admin/notification-groups/$GID/members" \
  "$(python3 -c 'import json,sys;print(json.dumps({"userIds":[sys.argv[1]]}))' "$ST1")" >/dev/null
check "Thêm lại sau khi bỏ: HỒI SINH, không tạo dòng thứ hai" 1 \
  "$(PSQL "SELECT count(*) FROM notification_group_members WHERE group_id='$GID' AND user_id='$ST1'" | head -1)"

# ── E. Xem trước ─────────────────────────────────────────────────────────────────────────────
section "E. Xem trước (không gửi gì)"
BATCH_BEFORE=$(PSQL "SELECT count(*) FROM notification_batches" | head -1)
python3 -c '
import json, sys
print(json.dumps({"groupIds": [sys.argv[1], sys.argv[2]], "userIds": [], "channels": [1, 2]}))' \
  "$GID" "$SYSM" > "$WORK/prev.json"
P=$(call POST /api/admin/notifications/broadcast/preview "$(cat "$WORK/prev.json")")
check "Gom trùng: recipientCount = 2"     2 "$(echo "$P" | jq_ data.recipientCount)"
check "  rawCount = 3 (cộng dồn không gom)" 3 "$(echo "$P" | jq_ data.rawCount)"
check "  notificationCount = 4"           4 "$(echo "$P" | jq_ data.notificationCount)"
check "Nhóm không tồn tại → missingGroups=1" 1 \
  "$(call POST /api/admin/notifications/broadcast/preview '{"groupIds":["00000000-0000-0000-0000-0000000000aa"],"userIds":[],"channels":[1]}' | jq_ data.missingGroups)"
check "Người đã ngừng → skippedUsers=1"   1 \
  "$(call POST /api/admin/notifications/broadcast/preview "$(python3 -c 'import json,sys;print(json.dumps({"groupIds":[],"userIds":[sys.argv[1]],"channels":[1]}))' "$DEAD")" | jq_ data.skippedUsers)"
check "Xem trước KHÔNG tạo batch nào"     "$BATCH_BEFORE" "$(PSQL "SELECT count(*) FROM notification_batches" | head -1)"

# ── F. Validate khi gửi ──────────────────────────────────────────────────────────────────────
section "F. Validate khi gửi"
BAD=$(call POST /api/admin/notifications/broadcast '{"type":999,"channels":[],"title":"","body":"","groupIds":[],"userIds":[]}')
NERR=$(echo "$BAD" | python3 -c 'import sys,json;print(len(json.load(sys.stdin).get("listErrors") or []))')
check "Payload sai toàn tập → 400"        400 "$(echo "$BAD" | jq_ statusCode)"
check "  thu ĐỦ lỗi một lượt (≥5)"       True "$([ "$NERR" -ge 5 ] && echo True || echo False)"
mkjson() { python3 -c '
import json, sys
p = json.loads(sys.argv[1]); p["groupIds"] = [sys.argv[2]]
print(json.dumps(p))' "$1" "$SYSS"; }
check "Tiêu đề 201 ký tự → 400"           400 "$(code POST /api/admin/notifications/broadcast "$(mkjson "$(python3 -c 'import json;print(json.dumps({"type":99,"channels":[4],"title":"a"*201,"body":"b"}))')")")"
check "Tiêu đề đúng 200 ký tự → 201"      201 "$(code POST /api/admin/notifications/broadcast "$(mkjson "$(python3 -c 'import json;print(json.dumps({"type":99,"channels":[4],"title":"E2E64"+"a"*195,"body":"b"}))')")")"
check "Nội dung 2001 ký tự → 400"         400 "$(code POST /api/admin/notifications/broadcast "$(mkjson "$(python3 -c 'import json;print(json.dumps({"type":99,"channels":[4],"title":"E2E64 t","body":"b"*2001}))')")")"
check "payloadJson không phải JSON → 400" 400 "$(code POST /api/admin/notifications/broadcast "$(mkjson '{"type":99,"channels":[4],"title":"E2E64 t","body":"b","payloadJson":"{hong"}')")"
check "payloadJson là mảng → 400"         400 "$(code POST /api/admin/notifications/broadcast "$(mkjson '{"type":99,"channels":[4],"title":"E2E64 t","body":"b","payloadJson":"[1,2]"}')")"
check "Kênh không hợp lệ (9) → 400"       400 "$(code POST /api/admin/notifications/broadcast "$(mkjson '{"type":99,"channels":[9],"title":"E2E64 t","body":"b"}')")"
check "Loại không hợp lệ (777) → 400"     400 "$(code POST /api/admin/notifications/broadcast "$(mkjson '{"type":777,"channels":[4],"title":"E2E64 t","body":"b"}')")"

BATCH_NOW=$(PSQL "SELECT count(*) FROM notification_batches" | head -1)
EMPTY=$(call POST /api/admin/notifications/broadcast \
  "$(python3 -c 'import json,sys;print(json.dumps({"type":99,"channels":[4],"title":"E2E64 rỗng","body":"x","groupIds":[],"userIds":[sys.argv[1]]}))' "$DEAD")")
check "Tập người nhận rỗng → 400"         400 "$(echo "$EMPTY" | jq_ statusCode)"
check "  nói RÕ lý do"                    True "$(echo "$EMPTY" | python3 -c 'import sys,json;print("ngừng hoạt động" in json.load(sys.stdin)["message"])')"
check "  KHÔNG tạo batch mồ côi"          "$BATCH_NOW" "$(PSQL "SELECT count(*) FROM notification_batches" | head -1)"

# ── G. Gửi thật ──────────────────────────────────────────────────────────────────────────────
section "G. Gửi thật & đối chiếu DB"
python3 -c '
import json, sys
print(json.dumps({"type": 99, "channels": [4, 2], "title": "E2E64 Bảo trì hệ thống",
                  "body": "kiểm chứng", "payloadJson": "{\"screen\":\"Home\"}",
                  "groupIds": [sys.argv[1], sys.argv[2]], "userIds": [sys.argv[3]]}))' \
  "$GID" "$SYSM" "$ST1" > "$WORK/send.json"
SEND=$(call POST /api/admin/notifications/broadcast "$(cat "$WORK/send.json")")
BID=$(echo "$SEND" | jq_ data.batchId)
check "Gửi → 201"                         201 "$(echo "$SEND" | jq_ statusCode)"
check "  recipientCount = 2"              2 "$(echo "$SEND" | jq_ data.recipientCount)"
check "  notificationCount = 4"           4 "$(echo "$SEND" | jq_ data.notificationCount)"
check "DB: 4 dòng notification"           4 "$(PSQL "SELECT count(*) FROM notifications WHERE batch_id='$BID'" | head -1)"
check "DB: 2 người nhận riêng biệt"       2 "$(PSQL "SELECT count(DISTINCT user_id) FROM notifications WHERE batch_id='$BID'" | head -1)"
check "DB: người ở 2 nhóm nhận đúng 2 dòng" 2 "$(PSQL "SELECT count(*) FROM notifications WHERE batch_id='$BID' AND user_id='$MGR'" | head -1)"
check "DB: 3 mục tiêu (2 nhóm + 1 cá nhân)" 3 "$(PSQL "SELECT count(*) FROM notification_batch_targets WHERE batch_id='$BID'" | head -1)"
check "DB: trạng thái FannedOut(2)"       2 "$(PSQL "SELECT status FROM notification_batches WHERE id='$BID'" | head -1)"
check "DB: channels = {4,2}"              "{4,2}" "$(PSQL "SELECT channels FROM notification_batches WHERE id='$BID'" | head -1)"

# ── H. Lịch sử, lọc, phân trang biên ─────────────────────────────────────────────────────────
section "H. Lịch sử & phân trang biên"
D=$(call GET "/api/admin/notifications/batches/$BID")
check "Chi tiết → 200"                    200 "$(echo "$D" | jq_ statusCode)"
check "  totalRows = 4"                   4 "$(echo "$D" | jq_ data.totalRows)"
check "  distinctRecipients = 2"          2 "$(echo "$D" | jq_ data.distinctRecipients)"
check "  liệt kê đủ 3 mục tiêu"           3 "$(echo "$D" | python3 -c 'import sys,json;print(len(json.load(sys.stdin)["data"]["targets"]))')"
check "Chi tiết id không tồn tại → 404"   404 "$(code GET /api/admin/notifications/batches/00000000-0000-0000-0000-000000000123)"
# Lớp lỗi tràn số nguyên đã từng gây HTTP 500 trên 7 endpoint (xem CHANGELOG 02/08/2026)
check "batches pageNumber=300000000 → 200" 200 "$(code GET '/api/admin/notifications/batches?pageNumber=300000000&pageSize=10')"
check "nhóm pageNumber=300000000 → 200"    200 "$(code GET '/api/admin/notification-groups?pageNumber=300000000&pageSize=10')"
check "thành viên pageNumber=300000000 → 200" 200 "$(code GET "/api/admin/notification-groups/$GID/members?pageNumber=300000000&pageSize=10")"
check "pageSize=99999 kẹp về 100"         100 "$(call GET '/api/admin/notification-groups?pageSize=99999' | jq_ data.pageSize)"
check "pageNumber=0 kẹp về 1"             1 "$(call GET '/api/admin/notification-groups?pageNumber=0' | jq_ data.pageNumber)"
check "Tìm nhóm theo tên (không phân biệt hoa-thường)" True \
  "$([ "$(call GET '/api/admin/notification-groups?search=e2e64' | jq_ data.totalItems)" -ge 1 ] && echo True || echo False)"
check "Lọc kind=Role(2) → 4"              4 "$(call GET '/api/admin/notification-groups?kind=2' | jq_ data.totalItems)"

# ── I. Xoá nhóm: lịch sử phải sống sót ───────────────────────────────────────────────────────
section "I. Xoá nhóm — lịch sử phải sống sót"
check "Xoá nhóm thường → 200"             200 "$(code DELETE "/api/admin/notification-groups/$GID")"
check "  nhóm xoá MỀM (còn dòng)"         1 "$(PSQL "SELECT count(*) FROM notification_groups WHERE id='$GID' AND is_deleted" | head -1)"
check "  thành viên xoá mềm theo"         0 "$(PSQL "SELECT count(*) FROM notification_group_members WHERE group_id='$GID' AND NOT is_deleted" | head -1)"
check "  GET chi tiết → 404"              404 "$(code GET "/api/admin/notification-groups/$GID")"
check "Lần gửi VẪN CÒN"                   1 "$(PSQL "SELECT count(*) FROM notification_batches WHERE id='$BID'" | head -1)"
check "Thông báo đã giao VẪN CÒN"         4 "$(PSQL "SELECT count(*) FROM notifications WHERE batch_id='$BID'" | head -1)"
D2=$(call GET "/api/admin/notifications/batches/$BID")
check "Nhóm ĐÃ XOÁ vẫn hiện KÈM TÊN trong lịch sử" True \
  "$(echo "$D2" | python3 -c 'import sys,json;print(any((t["groupName"] or "").startswith("E2E64") for t in json.load(sys.stdin)["data"]["targets"]))')"

# ── J. Lớp chống trùng thứ hai: unique index ở DB ────────────────────────────────────────────
section "J. Lớp chống trùng thứ hai (unique index)"
UID_=$(PSQL "SELECT id FROM account_read_models LIMIT 1" | head -1)
B2=$(PSQL "INSERT INTO notification_batches (id,type,title,body,channels,source,status,recipient_count,notification_count,created_at,is_deleted)
           VALUES (gen_random_uuid(),99,'E2E64-lop2','x','{4}',2,2,1,1,now(),false) RETURNING id" | head -1)
PSQL "INSERT INTO notifications (id,user_id,batch_id,type,channel,status,title,body,created_at,is_deleted,dispatch_attempt_count)
      VALUES (gen_random_uuid(),'$UID_','$B2',99,4,1,'E2E64-lop2','x',now(),false,0)" >/dev/null
DUP=$(docker exec "$PG" psql -U postgres -d "$DB" -c "
INSERT INTO notifications (id,user_id,batch_id,type,channel,status,title,body,created_at,is_deleted,dispatch_attempt_count)
VALUES (gen_random_uuid(),'$UID_','$B2',99,4,1,'E2E64-lop2','x',now(),false,0)" 2>&1)
check "DB CHẶN dòng trùng (batch,user,channel)" True \
  "$(echo "$DUP" | grep -q 'ux_notifications_batch_user_channel' && echo True || echo False)"
PSQL "INSERT INTO notifications (id,user_id,batch_id,type,channel,status,title,body,created_at,is_deleted,dispatch_attempt_count)
      VALUES (gen_random_uuid(),'$UID_','$B2',99,2,1,'E2E64-lop2','x',now(),false,0)" >/dev/null
check "Cùng người KHÁC kênh → cho phép"   2 "$(PSQL "SELECT count(*) FROM notifications WHERE batch_id='$B2'" | head -1)"
# Dọn NGAY scaffolding của mục này. Batch trên được dựng tay bằng SQL với notification_count=1 rồi
# chèn 2 dòng, nên nếu để lại thì phép kiểm bất biến ở mục Z (count phải khớp số dòng thật) sẽ báo
# hỏng vì dữ liệu giả của chính test — che mất lỗi thật nếu có.
PSQL "DELETE FROM notifications WHERE batch_id='$B2'" >/dev/null
PSQL "DELETE FROM notification_batches WHERE id='$B2'" >/dev/null

# ── K. Tạo trùng tên đồng thời ───────────────────────────────────────────────────────────────
section "K. Tạo trùng tên đồng thời (race)"
for i in 1 2 3 4 5; do
  curl -s -o "$WORK/race$i.json" -X POST "$BASE/api/admin/notification-groups" \
    -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' \
    --data-binary '{"name":"E2E64-race"}' &
done
wait
check "Chỉ ĐÚNG 1 nhóm được tạo" 1 \
  "$(PSQL "SELECT count(*) FROM notification_groups WHERE normalized_name='E2E64-RACE' AND NOT is_deleted" | head -1)"
check "Không request nào trả 500" 0 \
  "$(for i in 1 2 3 4 5; do jq_ statusCode < "$WORK/race$i.json"; done | grep -c 500)"

# ── Z. Bất biến DB toàn cục ──────────────────────────────────────────────────────────────────
section "Z. Bất biến DB toàn cục"
check "Nhận trùng trong cùng lần gửi"     0 "$(PSQL "SELECT count(*) FROM (SELECT batch_id,user_id,channel FROM notifications WHERE batch_id IS NOT NULL GROUP BY 1,2,3 HAVING count(*)>1) t" | head -1)"
check "Batch đã phát mà không sinh dòng"  0 "$(PSQL "SELECT count(*) FROM notification_batches b WHERE b.status=2 AND NOT EXISTS (SELECT 1 FROM notifications n WHERE n.batch_id=b.id)" | head -1)"
check "Nhóm Role thiếu role_filter"       0 "$(PSQL "SELECT count(*) FROM notification_groups WHERE kind=2 AND role_filter IS NULL" | head -1)"
check "Nhóm Static lại có role_filter"    0 "$(PSQL "SELECT count(*) FROM notification_groups WHERE kind=1 AND role_filter IS NOT NULL" | head -1)"
check "Trùng tên trong nhóm chưa xoá"     0 "$(PSQL "SELECT count(*) FROM (SELECT normalized_name FROM notification_groups WHERE NOT is_deleted GROUP BY 1 HAVING count(*)>1) t" | head -1)"
check "Một người 2 dòng trong cùng nhóm"  0 "$(PSQL "SELECT count(*) FROM (SELECT group_id,user_id FROM notification_group_members WHERE NOT is_deleted GROUP BY 1,2 HAVING count(*)>1) t" | head -1)"
check "Target vi phạm CHECK hình dạng"    0 "$(PSQL "SELECT count(*) FROM notification_batch_targets WHERE NOT ((target_kind=1 AND group_id IS NOT NULL AND user_id IS NULL) OR (target_kind=2 AND user_id IS NOT NULL AND group_id IS NULL))" | head -1)"
check "Notification trỏ tới batch không tồn tại" 0 "$(PSQL "SELECT count(*) FROM notifications n WHERE n.batch_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM notification_batches b WHERE b.id=n.batch_id)" | head -1)"
check "recipient_count lệch số người thật" 0 "$(PSQL "SELECT count(*) FROM notification_batches b WHERE b.status=2 AND b.recipient_count <> (SELECT count(DISTINCT user_id) FROM notifications n WHERE n.batch_id=b.id)" | head -1)"
check "notification_count lệch số dòng thật" 0 "$(PSQL "SELECT count(*) FROM notification_batches b WHERE b.status=2 AND b.notification_count <> (SELECT count(*) FROM notifications n WHERE n.batch_id=b.id)" | head -1)"

# ── Y. Không rò rỉ dữ liệu ───────────────────────────────────────────────────────────────────
# Chạy hàm dọn NGAY tại đây (bẫy EXIT vẫn giữ để phòng thoát sớm — dọn hai lần vô hại) rồi so với
# ảnh chụp nền. Không có phép kiểm này thì mọi thứ script quên dọn sẽ tích luỹ âm thầm và chỉ lộ
# ra ở lần chạy sau, dưới dạng một phép kiểm khác hẳn bị hỏng.
section "Y. Không rò rỉ dữ liệu"
cleanup
check "Bảng nghiệp vụ trở về đúng nền" "$BASELINE_BUSINESS" "$(snapshot_business)"

# Bảng audit CÓ tăng và điều đó là ĐÚNG: notification_audit_logs là append-only (trigger chặn
# DELETE), vết audit ghi lại đúng những thao tác đã thực sự xảy ra. Không lờ đi, mà báo ra con số.
AFTER_AUDIT=$(snapshot_audit)
printf '  \033[36mℹ\033[0m %-62s %s → %s\n' \
  "Bảng audit tăng (append-only, cố ý KHÔNG dọn)" "$BASELINE_AUDIT" "$AFTER_AUDIT"

# ── Tổng kết ─────────────────────────────────────────────────────────────────────────────────
printf '\n\033[1m════ Tổng: %d đạt · %d hỏng ════\033[0m\n' "$PASS" "$FAIL"
if [ "$FAIL" -gt 0 ]; then
  printf 'Các phép kiểm hỏng:\n'
  for n in "${FAILED_NAMES[@]}"; do printf '  · %s\n' "$n"; done
  exit 1
fi
printf 'Bảng nghiệp vụ đã về đúng nền; dữ liệu sẵn có không bị đụng tới.\n'
