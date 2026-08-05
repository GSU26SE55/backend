#!/usr/bin/env bash
# E2E cho các đường mà ứng dụng MOBILE gọi — chạy lên stack docker đang chạy.
#
# Emulator Android không dựng được trên máy này (thiếu JDK, chưa có AVD lẫn system image, dự án ở
# managed workflow chưa prebuild), nên phần kiểm chạy được là gọi thật đúng những endpoint mà mã
# mobile gọi, với cùng payload mà mã mobile dựng.
set -uo pipefail

GW="${GW:-http://localhost:4001}"
PASS=0; FAIL=0
ok()   { printf '  \033[32mPASS\033[0m  %s\n' "$1"; PASS=$((PASS+1)); }
bad()  { printf '  \033[31mFAIL\033[0m  %s\n' "$1"; FAIL=$((FAIL+1)); }
note() { printf '  \033[33mGHI\033[0m   %s\n' "$1"; }
head_(){ printf '\n\033[1m== %s\033[0m\n' "$1"; }
expect(){ [ "$2" = "$3" ] && ok "$1 (=$3)" || bad "$1 — mong $2, nhận $3"; }

jget() { python3 -c "import sys,json
try:
    d=json.load(sys.stdin)
    for k in '$1'.split('.'):
        if d is None: break
        d = d.get(k) if isinstance(d, dict) else None
    print('' if d is None else d)
except Exception: print('')"; }

CUS=$(curl -s --max-time 30 -X POST "$GW/api/auth/login" -H 'Content-Type: application/json' \
  -d '{"email":"customer.demo@solarbattery.local","password":"Password123@"}' | jget 'data.tokens.accessToken')

head_ "Đăng nhập Customer (vai của ứng dụng mobile)"
[ -n "$CUS" ] && ok "có token Customer" || { bad "KHÔNG lấy được token"; exit 1; }

auth() { curl -s -o /dev/null -w '%{http_code}' --max-time 30 "$GW$1" -H "Authorization: Bearer $CUS"; }

head_ "Các màn hình chính của mobile"
expect "GET /api/battery-assets/me"          200 "$(auth /api/battery-assets/me)"
expect "GET /api/notifications"              200 "$(auth '/api/notifications?pageNumber=1&pageSize=10')"
expect "GET /api/notifications/unread-count" 200 "$(auth /api/notifications/unread-count)"
expect "GET /api/notification-preferences"   200 "$(auth /api/notification-preferences)"
# Mobile dùng /api/customer/tickets/me (CUSTOMER_LIST), KHÔNG phải /api/tickets/me — đường sau
# không tồn tại và trả 404.
expect "GET /api/customer/tickets/me"        200 "$(auth '/api/customer/tickets/me?PageNumber=1&PageSize=10')"
expect "GET /api/sessions/me"                200 "$(auth /api/sessions/me)"

head_ "GH-792 — trạng thái notification mobile nhận về phải nằm trong enum client biết"
curl -s --max-time 30 "$GW/api/notifications?pageNumber=1&pageSize=50" -H "Authorization: Bearer $CUS" \
 | python3 -c '
import sys,json
# Enum của mobile sau bản sửa: 1..7 (7 = Processing, GH-792).
KNOWN={1,2,3,4,5,6,7}
items=(json.load(sys.stdin).get("data") or {}).get("items") or []
bad=[i["status"] for i in items if i.get("status") not in KNOWN]
if not items:
    print("  \033[33mGHI\033[0m   feed rỗng — không có dòng nào để đối chiếu")
elif bad:
    print(f"  \033[31mFAIL\033[0m  gặp status ngoài enum mobile: {sorted(set(bad))}")
else:
    print(f"  \033[32mPASS\033[0m  {len(items)} dòng, mọi status nằm trong enum mobile biết")'

head_ "GH-788 — mobile luôn có đường lấy file kể cả khi publicUrl null"
python3 -c "
import base64,pathlib
pathlib.Path('/tmp/e2e-mobile.png').write_bytes(base64.b64decode(
 'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg=='))"
UP=$(curl -s --max-time 30 -X POST "$GW/api/files/upload" -H "Authorization: Bearer $CUS" \
  -F "File=@/tmp/e2e-mobile.png;type=image/png" -F "Purpose=2")
FID=$(printf '%s' "$UP" | jget 'data.fileId')
if [ -n "$FID" ]; then
  ok "upload được: $FID"
  # useUploadTicketAttachment.ts:29 dựng `publicUrl ?? DOWNLOAD(fileId)` — đường dự phòng phải sống.
  expect "GET /api/files/{id}/download (đường dự phòng)" 200 "$(auth "/api/files/$FID/download")"
else
  bad "upload thất bại: $(printf '%s' "$UP" | head -c 200)"
fi

head_ "GH-788 — bản vá ghi âm: BE CHẤP NHẬN url dạng đường tải, không đòi publicUrl"
# Đây là phép kiểm quan trọng nhất của bản sửa mobile/FE: trước đây client tự chặn khi publicUrl
# null vì tin rằng thiếu nó thì bước 2 chắc chắn 400. Kiểm thẳng giả định đó.
TICKET=$(curl -s --max-time 30 "$GW/api/customer/tickets/me?PageNumber=1&PageSize=1" -H "Authorization: Bearer $CUS" \
  | python3 -c 'import sys,json
try:
  i=(json.load(sys.stdin).get("data") or {}).get("items") or []; print(i[0]["id"] if i else "")
except Exception: print("")')
if [ -z "$TICKET" ]; then
  note "Customer chưa có ticket nào — bỏ qua phép kiểm ghi âm"
else
  ok "dùng ticket $TICKET"
  # WAV 8-bit mono tối thiểu, hợp lệ với danh sách MIME audio của BE.
  python3 -c "
import struct,pathlib
data=b'\x80'*800
hdr=b'RIFF'+struct.pack('<I',36+len(data))+b'WAVEfmt '+struct.pack('<IHHIIHH',16,1,1,8000,8000,1,8)+b'data'+struct.pack('<I',len(data))
pathlib.Path('/tmp/e2e-voice.wav').write_bytes(hdr+data)"
  AUP=$(curl -s --max-time 30 -X POST "$GW/api/files/upload" -H "Authorization: Bearer $CUS" \
    -F "File=@/tmp/e2e-voice.wav;type=audio/wav" -F "Purpose=2")
  AFID=$(printf '%s' "$AUP" | jget 'data.fileId')
  ASIZE=$(printf '%s' "$AUP" | jget 'data.size')
  if [ -z "$AFID" ]; then
    bad "upload audio thất bại: $(printf '%s' "$AUP" | head -c 200)"
  else
    ok "upload audio được: $AFID ($ASIZE byte)"
    # CHÍNH XÁC payload mà mobile/FE dựng sau bản sửa: url = đường tải, KHÔNG phải publicUrl.
    VOICE=$(curl -s --max-time 30 -X POST "$GW/api/tickets/$TICKET/chats/voice" \
      -H "Authorization: Bearer $CUS" -H 'Content-Type: application/json' \
      -d "{\"fileId\":\"$AFID\",\"fileName\":\"e2e-voice.wav\",\"contentType\":\"audio/wav\",\"sizeBytes\":$ASIZE,\"url\":\"/api/files/$AFID/download\"}")
    VCODE=$(printf '%s' "$VOICE" | jget 'statusCode')
    case "$VCODE" in
      202|200|201) ok "BE nhận url dạng đường tải → $VCODE ⇒ giả định 'thiếu publicUrl là 400' SAI, bản vá đúng" ;;
      *)           bad "BE từ chối → $VCODE : $(printf '%s' "$VOICE" | head -c 250)" ;;
    esac
  fi
fi

printf '\n\033[1mTỔNG: %d PASS · %d FAIL\033[0m\n' "$PASS" "$FAIL"
[ "$FAIL" -eq 0 ]
