#!/usr/bin/env bash
# =====================================================================
# E2E tích hợp AI ↔ BE — phần mà tools/e2e-smoke.sh KHÔNG phủ.
#
# e2e-smoke.sh kiểm gateway/auth/report/SLA/saga và không có một dòng nào
# về predict/prescribe/soh/anomaly. Nên "L3 xanh" của loop-engine hoàn toàn
# không nói gì về tích hợp AI. Script này lấp đúng khoảng đó.
#
# Kiểm CẢ HAI CHIỀU:
#   BE → AI : BE gọi sang AI (predict, prescribe, verify, 2 loại feedback)
#   AI → BE : dữ liệu AI hiện ra qua API của BE (SOH, classification, alert)
#
# Chạy:  bash tools/e2e-ai-integration.sh
# Cần:   stack đang chạy (docker compose up -d), ADMIN_* trong .env.Docker
# =====================================================================
set -uo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
GATEWAY="${GATEWAY_URL:-http://localhost:4001}"
AI_HTTP="${AI_HTTP_URL:-http://localhost:4015}"
ENV_FILE="${ENV_FILE:-$REPO_ROOT/.env.Docker}"

read_env() { [ -f "$ENV_FILE" ] && (grep -E "^$1=" "$ENV_FILE" | head -1 | cut -d= -f2-) || true; }
ADMIN_EMAIL="${ADMIN_EMAIL:-$(read_env ADMIN_EMAIL)}"
ADMIN_PASSWORD="${ADMIN_PASSWORD:-$(read_env ADMIN_PASSWORD)}"

PASS=0; FAIL=0
pass() { echo "  ✅ $*"; PASS=$((PASS+1)); }
miss() { echo "  ❌ $*"; FAIL=$((FAIL+1)); }
section() { echo ""; echo "▶ $*"; }

# Đọc một field lồng nhau theo đường dẫn chấm: "data.tokens.accessToken", "data.items.0.id".
# Truyền đường dẫn qua argv chứ KHÔNG nội suy vào mã Python — đường dẫn có dấu nháy sẽ phá
# vỡ chuỗi literal và hàm trả rỗng một cách im lặng.
jqp() {
  python3 -c '
import sys, json
try:
    d = json.load(sys.stdin)
    for k in sys.argv[1].split("."):
        if not k:
            continue
        d = d[int(k)] if k.isdigit() else d[k]
    print(d if d is not None else "")
except Exception:
    print("")
' "$1" 2>/dev/null || echo ""
}

# ── Đăng nhập ────────────────────────────────────────────────────────
section "0. Đăng nhập admin qua gateway"
TOKEN="$(curl -fsS -X POST "$GATEWAY/api/auth/login" -H 'Content-Type: application/json' \
  -d "{\"email\":\"$ADMIN_EMAIL\",\"password\":\"$ADMIN_PASSWORD\"}" 2>/dev/null \
  | jqp data.tokens.accessToken)"
if [ -n "$TOKEN" ]; then pass "admin login → token"; else miss "admin login"; echo "Không có token — dừng."; exit 1; fi
AUTH=(-H "Authorization: Bearer $TOKEN")

# ── CHIỀU AI → BE ────────────────────────────────────────────────────
# Dữ liệu do AI sinh có ra tới API của BE không, và có ĐỦ field không.
section "1. AI → BE · SOH prediction lộ ra qua API BE"
# batteryAssetId là BẮT BUỘC: handler lọc `p.BatteryAssetId == request.BatteryAssetId`, nên
# gọi thiếu tham số sẽ so với Guid.Empty và trả list RỖNG kèm 200 — trông như "API hỏng"
# trong khi thực ra là gọi sai. Lấy asset thật trước.
ASSET_JSON="$(curl -fsS "${AUTH[@]}" "$GATEWAY/api/battery-assets?pageNumber=1&pageSize=50" 2>/dev/null)"
ASSET_IDS="$(echo "$ASSET_JSON" | python3 -c '
import sys, json
try:
    print(" ".join(i["id"] for i in json.load(sys.stdin)["data"]["items"]))
except Exception:
    print("")
' 2>/dev/null)"
[ -n "$ASSET_IDS" ] && pass "lấy được $(echo $ASSET_IDS | wc -w | tr -d " ") battery asset" || miss "không lấy được asset nào"

# KHÔNG lấy bừa asset đầu tiên: không phải pin nào cũng có prediction — pin thiếu
# cycle_count bị job bỏ qua có chủ ý (bộ LFP từ chối payload 4 cột). Duyệt tìm pin CÓ
# dữ liệu; chỉ khi KHÔNG pin nào có thì mới là hỏng thật.
ASSET_ID=""; SOH_JSON=""; SOH_COUNT=0
for a in $ASSET_IDS; do
  J="$(curl -fsS "${AUTH[@]}" "$GATEWAY/api/v1/soh-predictions?batteryAssetId=$a&pageNumber=1&pageSize=5" 2>/dev/null)"
  N="$(echo "$J" | jqp data.totalItems)"
  if [ -n "$N" ] && [ "$N" -gt 0 ] 2>/dev/null; then
    ASSET_ID="$a"; SOH_JSON="$J"; SOH_COUNT="$N"; break
  fi
done

if [ -n "$ASSET_ID" ]; then
  pass "GET /api/v1/soh-predictions (200) — asset $ASSET_ID có $SOH_COUNT prediction"
else
  miss "KHÔNG pin nào có prediction — job nền chưa chạy hoặc AI đang đứt"
fi

# 12 field D2/D4 phải có mặt. Thiếu một field ở đây nghĩa là bridge lại đang vứt
# dữ liệu AI — đúng lỗi mà D1/D2 sinh ra để chặn.
for f in healthStage stageConfidence isBorderline sohStd rulCyclesEstimate aiPriority \
         riskLevel actionCode sohTrend degradationRatePerCycle cyclesToMaintenance isTemperatureOod; do
  if echo "$SOH_JSON" | grep -q "\"$f\""; then pass "  field $f"; else miss "  field $f THIẾU trong DTO"; fi
done

section "2. AI → BE · Anomaly classification lộ ra qua API BE"
CLS_JSON="$(curl -fsS "${AUTH[@]}" "$GATEWAY/api/v1/anomaly-classifications?batteryAssetId=$ASSET_ID&pageNumber=1&pageSize=5" 2>/dev/null)"
if [ -n "$CLS_JSON" ]; then pass "GET /api/v1/anomaly-classifications (200)"; else miss "GET /api/v1/anomaly-classifications"; fi
CLS_ID="$(echo "$CLS_JSON" | jqp data.items.0.id)"
[ -n "$CLS_ID" ] && pass "có classification để test ($CLS_ID)" || miss "không có classification nào"

section "3. AI → BE · Alert do AI sinh"
AL_JSON="$(curl -fsS "${AUTH[@]}" "$GATEWAY/api/alerts?pageNumber=1&pageSize=5" 2>/dev/null)"
[ -n "$AL_JSON" ] && pass "GET /api/alerts (200)" || miss "GET /api/alerts"
ALERT_ID="$(echo "$AL_JSON" | jqp data.items.0.id)"
[ -n "$ALERT_ID" ] && pass "có alert để test ($ALERT_ID)" || miss "không có alert nào"

# ── CHIỀU BE → AI ────────────────────────────────────────────────────
section "4. BE → AI · Phản hồi PHÂN LOẠI (F4) — round-trip qua AI store"
BEFORE="$(curl -fsS -X POST "$AI_HTTP/predict/feedback" -H 'Content-Type: application/json' \
  -d '{"battery_id":"__probe__","classification":"Normal","verdict":"correct"}' 2>/dev/null | jqp total)"
if [ -n "$CLS_ID" ]; then
  CODE="$(curl -s -o /dev/null -w '%{http_code}' -X POST "$GATEWAY/api/v1/anomaly-classifications/$CLS_ID/feedback" \
    "${AUTH[@]}" -H 'Content-Type: application/json' -d '{"feedback":1}')"
  [ "$CODE" = "200" ] && pass "POST classification feedback qua BE ($CODE)" || miss "POST classification feedback ($CODE)"
  AFTER="$(curl -fsS -X POST "$AI_HTTP/predict/feedback" -H 'Content-Type: application/json' \
    -d '{"battery_id":"__probe__","classification":"Normal","verdict":"correct"}' 2>/dev/null | jqp total)"
  # BE ghi 1 bản + 2 probe của chính script ⇒ chênh lệch phải là 2.
  # Chênh 1 nghĩa là BE KHÔNG gửi được sang AI (vòng học đứt, im lặng).
  if [ -n "$BEFORE" ] && [ -n "$AFTER" ] && [ "$((AFTER - BEFORE))" -ge 2 ]; then
    pass "AI nhận được phản hồi từ BE (store $BEFORE → $AFTER)"
  else
    miss "AI KHÔNG nhận được phản hồi từ BE (store $BEFORE → $AFTER, chênh <2)"
  fi
else
  miss "bỏ qua — không có classification"
fi

section "5. BE → AI · Kê prescription thủ công (C7) — phải KÊ ĐƯỢC, không chỉ 'thông'"
# Alert lấy ở §3 có thể thuộc pin thiếu số đo ⇒ trả 409, chỉ chứng minh đường đi thông chứ
# KHÔNG chứng minh AI kê được đơn. Tìm alert thuộc ĐÚNG pin đã có prediction (tức đủ dữ liệu).
RICH_ALERT="$(curl -fsS "${AUTH[@]}" "$GATEWAY/api/alerts?batteryAssetId=$ASSET_ID&pageNumber=1&pageSize=1" 2>/dev/null | jqp data.items.0.id)"
[ -z "$RICH_ALERT" ] && RICH_ALERT="$ALERT_ID"

PRESC_JSON="$(curl -fsS -X POST "$GATEWAY/api/alerts/$RICH_ALERT/ai-prescription?agentic=false" "${AUTH[@]}" 2>/dev/null)"
PRESC_ID="$(echo "$PRESC_JSON" | jqp data.prescriptionId)"
STEPS="$(echo "$PRESC_JSON" | jqp data.actionSteps.0)"
if [ -n "$STEPS" ]; then
  pass "AI kê được đơn qua BE (bước đầu: ${STEPS:0:52}…)"
else
  miss "AI KHÔNG kê được đơn — response: $(echo "$PRESC_JSON" | head -c 120)"
fi
# Ba field C2/C3/C8 từng bị bridge vứt — phải có mặt trong response của BE.
for f in escalationConditions blocked cached; do
  echo "$PRESC_JSON" | grep -q "\"$f\"" && pass "  field $f (C2/C3/C8)" || miss "  field $f THIẾU"
done
[ -n "$PRESC_ID" ] && pass "có prescriptionId ($PRESC_ID)" \
  || miss "prescriptionId rỗng — vòng phản hồi đứt tại ranh giới bridge"

section "6. BE → AI · Phản hồi PRESCRIPTION (C9) — round-trip bằng id vừa nhận"
if [ -n "$PRESC_ID" ]; then
  # Dùng ĐÚNG id AI vừa cấp ⇒ phải 200. Trả 410 nghĩa là AI không nhận ra id của chính nó
  # (store lệch giữa hai container), 503 nghĩa là không gọi tới được.
  CODE="$(curl -s -o /dev/null -w '%{http_code}' -X POST "$GATEWAY/api/alerts/$RICH_ALERT/prescription-feedback" \
    "${AUTH[@]}" -H 'Content-Type: application/json' -d '{"status":"accepted"}')"
  case "$CODE" in
    200) pass "POST prescription-feedback (200 — AI ghi nhận phản hồi)";;
    410) miss "AI không nhận ra prescriptionId nó vừa cấp (410) — store lệch giữa 2 container";;
    503) miss "AI không kết nối được (503)";;
    *)   miss "POST prescription-feedback trả $CODE";;
  esac
else
  miss "bỏ qua — không có prescriptionId"
fi

section "6b. BE → AI · SOH chuỗi dài (PredictLong) qua BE"
LONG_JSON="$(curl -s "${AUTH[@]}" "$GATEWAY/api/v1/soh-predictions/long?batteryAssetId=$ASSET_ID&limit=300")"
LONG_SOH="$(echo "$LONG_JSON" | jqp data.sohPercent)"
LONG_VER="$(echo "$LONG_JSON" | jqp data.modelVersion)"
LONG_SEQ="$(echo "$LONG_JSON" | jqp data.seqLen)"
if [ -n "$LONG_SOH" ]; then
  pass "GET /soh-predictions/long → soh=$LONG_SOH seq=$LONG_SEQ model=$LONG_VER"
else
  miss "PredictLong qua BE thất bại: $(echo "$LONG_JSON" | head -c 140)"
fi
# Model long phải là bộ trọng số RIÊNG. Trùng version với Predict thường nghĩa là gọi
# nhầm đường — hai con số SOH sẽ bị so sánh với nhau trong khi chúng không so được.
if [ -n "$LONG_VER" ] && [ "$LONG_VER" != "1.6" ]; then
  pass "  dùng đúng bộ trọng số LONG ($LONG_VER ≠ 1.6)"
else
  miss "  modelVersion=$LONG_VER — nghi gọi nhầm sang model window=30"
fi

section "6c. BE → AI · Dự đoán hàng loạt (PredictStream) qua BE"
BATCH_JSON="$(curl -s "${AUTH[@]}" "$GATEWAY/api/v1/soh-predictions/batch?limit=10")"
B_REQ="$(echo "$BATCH_JSON" | jqp data.requestedCount)"
B_DONE="$(echo "$BATCH_JSON" | jqp data.isComplete)"
B_N="$(echo "$BATCH_JSON" | python3 -c 'import sys,json
try: print(len(json.load(sys.stdin)["data"]["items"]))
except Exception: print("")' 2>/dev/null)"
if [ -n "$B_N" ] && [ "$B_N" -gt 0 ] 2>/dev/null; then
  pass "GET /soh-predictions/batch → $B_N/$B_REQ pin được chấm trong 1 kết nối"
else
  miss "PredictStream qua BE thất bại: $(echo "$BATCH_JSON" | head -c 140)"
fi
# isComplete là field an toàn: thiếu nó thì FE không phân biệt được "pin sạch" với
# "pin chưa được chấm vì stream đứt".
echo "$BATCH_JSON" | grep -q '"isComplete"' && pass "  có cờ isComplete (=$B_DONE)" \
  || miss "  THIẾU isComplete — FE sẽ đọc pin chưa chấm thành pin bình thường"

section "6d. BE → AI · VerifyTicket qua TicketService (cần role Manager)"
# Endpoint re-verify là [Authorize(Roles="Manager")] — token Admin ở trên sẽ nhận 403.
# Tài khoản bootstrap vận hành nằm trong AuthDataSeeder; giữ ở biến để có thể
# override khi chạy trên môi trường khác.
MGR_EMAIL="${MGR_EMAIL:-manager@solars.io.vn}"
MGR_PASSWORD="${MGR_PASSWORD:-Password123@}"
MGR_TOKEN="$(curl -fsS -X POST "$GATEWAY/api/auth/login" -H 'Content-Type: application/json' \
  -d "{\"email\":\"$MGR_EMAIL\",\"password\":\"$MGR_PASSWORD\"}" 2>/dev/null | jqp data.tokens.accessToken)"
if [ -n "$MGR_TOKEN" ]; then pass "manager login → token"; else miss "manager login thất bại"; fi

if [ -n "$MGR_TOKEN" ]; then
  # Ba ràng buộc của TicketReVerifyCommandHandler, phải khớp cả ba mới ra 200:
  #   origin = 1 (ManualByCustomer)  — AI verify chỉ dành cho ticket khách tự khai
  #   ai_verify_status ∈ {1,4}       — đã có verdict thì chặn (guard idempotent)
  #   status ∉ {10,11,12}            — ticket đã đóng thì không sửa được
  # Bỏ sót ràng buộc nào cũng ra 4xx và bị đọc nhầm thành "tích hợp hỏng".
  TK="$(docker exec solar-postgres psql -U postgres -d ticket_db -tAc \
    "SELECT id FROM tickets WHERE origin=1 AND ai_verify_status IN (1,4) \
     AND status NOT IN (10,11,12) AND NOT is_deleted LIMIT 1;" 2>/dev/null | tr -d ' ')"

  # Sau lần chạy đầu, mọi ticket hợp lệ đều đã có verdict ⇒ guard idempotent chặn và bài
  # test sẽ rơi sang nhánh dự phòng, tức KHÔNG còn gọi AI lần nào nữa. Đưa MỘT ticket về
  # lại Pending để mỗi lượt chạy đều đi đúng đường thật. An toàn: re-verify ngay sau đó
  # ghi verdict lại, nên trạng thái cuối cùng không đổi.
  if [ -z "$TK" ] && [ "${E2E_RESET_TICKET:-1}" = "1" ]; then
    # head -1: psql in thêm dòng "UPDATE 1" sau giá trị RETURNING. Không cắt thì biến id
    # dính cả dòng đó và mọi lệnh sau nhận URL rác — curl trả 000, trông y như AI sập.
    TK="$(docker exec solar-postgres psql -U postgres -d ticket_db -tAc \
      "UPDATE tickets SET ai_verify_status=1, ai_verify_score=NULL, ai_verify_reason=NULL \
       WHERE id=(SELECT id FROM tickets WHERE origin=1 AND status NOT IN (10,11,12) \
                 AND NOT is_deleted ORDER BY created_at LIMIT 1) RETURNING id;" 2>/dev/null \
      | head -1 | tr -d ' \r')"
    [ -n "$TK" ] && pass "đã đưa 1 ticket về Pending để chạy đúng đường thật"
  fi

  if [ -n "$TK" ]; then
    CODE="$(curl -s -o /dev/null -w '%{http_code}' -m 90 -X POST "$GATEWAY/api/admin/tickets/$TK/re-verify" \
      -H "Authorization: Bearer $MGR_TOKEN")"
    [ "$CODE" = "200" ] && pass "POST /api/admin/tickets/{id}/re-verify ($CODE)" \
      || miss "re-verify trả $CODE"
    V="$(docker exec solar-postgres psql -U postgres -d ticket_db -tAc \
      "SELECT ai_verify_status || '|' || coalesce(ai_verify_score::text,'-') FROM tickets WHERE id='$TK';" 2>/dev/null | tr -d ' ')"
    case "$V" in
      2\|*|3\|*) pass "AI đã chấm ticket vừa gọi (status|score = $V)";;
      *) miss "ticket vẫn chưa có verdict AI (status|score = $V)";;
    esac
  else
    # Không còn ticket đủ điều kiện là chuyện BÌNH THƯỜNG sau khi đã verify hết — không
    # phải lỗi. Nhưng vẫn phải chứng minh tính năng CHẠY ĐƯỢC, nên kiểm bằng chứng cũ:
    # phải tồn tại ticket manual đã mang verdict thật (2/3) kèm score.
    DONE="$(docker exec solar-postgres psql -U postgres -d ticket_db -tAc \
      "SELECT count(*) FROM tickets WHERE origin=1 AND ai_verify_status IN (2,3) \
       AND ai_verify_score IS NOT NULL AND NOT is_deleted;" 2>/dev/null | tr -d ' ')"
    if [ -n "$DONE" ] && [ "$DONE" -gt 0 ] 2>/dev/null; then
      pass "không còn ticket chờ verify; $DONE ticket đã mang verdict AI thật"
    else
      miss "không có ticket nào đủ điều kiện VÀ cũng không ticket nào từng được AI chấm"
    fi
  fi
fi

section "7. BE → AI · Job nền vẫn đang gọi AI"
TICK="$(docker logs solar-batteryservice --since 20m 2>&1 | grep -o '"Message":"SohPrediction tick: predicted=[0-9]*[^"]*"' | tail -1)"
if echo "$TICK" | grep -qE 'predicted=[1-9]'; then
  pass "job nền có prediction: $(echo "$TICK" | grep -o 'predicted=[0-9]*')"
else
  miss "job nền KHÔNG sinh prediction nào trong 20 phút"
fi
# Fallback sang HTTP KHÔNG mặc nhiên là lỗi: khi container AI vừa restart, BatteryService
# fallback trong vài giây đầu là cơ chế chạy ĐÚNG. Bản trước quét "có fallback trong 20 phút"
# nên cứ rebuild AI xong là bài test đỏ — báo động giả, và tệ hơn là nó dạy người đọc bỏ qua
# mục này. So mốc thời gian thay vì chỉ đếm sự xuất hiện.
AI_START="$(docker inspect solar-ai-module-grpc --format '{{.State.StartedAt}}' 2>/dev/null | cut -c1-19)"
LAST_FB="$(docker logs solar-batteryservice --since 30m 2>&1 \
  | grep -o '"Timestamp":"[^"]*"[^}]*falling back to HTTP' \
  | sed 's/.*Timestamp":"\([^"]*\)".*/\1/' | tail -1 | cut -c1-19)"

if [ -z "$LAST_FB" ]; then
  pass "gRPC primary hoạt động (không có fallback nào)"
elif [ -n "$AI_START" ] && [ "$LAST_FB" \< "$(date -u -j -v+90S -f '%Y-%m-%dT%H:%M:%S' "$AI_START" '+%Y-%m-%dT%H:%M:%S' 2>/dev/null || echo 9999)" ]; then
  # Fallback nằm trong 90 giây sau khi AI khởi động lại ⇒ đúng thiết kế.
  pass "fallback cuối ($LAST_FB) nằm trong cửa sổ AI restart ($AI_START) — hành vi đúng"
else
  miss "gRPC fail NGOÀI cửa sổ restart: fallback cuối $LAST_FB, AI khởi động $AI_START"
fi

section "7b. AI nội bộ · Hai container PHẢI dùng chung store (chống lỗi store tách đôi)"
# Vì sao có mục này: lỗi "store tách đôi" từng chỉ lộ ra khi TRÙNG thời điểm — BE fallback
# gRPC→HTTP đúng lúc giữa kê đơn và gửi phản hồi. Bắt lỗi bằng may thì lần sau sẽ lọt.
# Ở đây ép đúng kịch bản đó một cách TẤT ĐỊNH: ghi qua container HTTP, đọc qua container gRPC.
#
# Hỏng cách này KHÔNG có triệu chứng ở phía người dùng: UI vẫn báo gửi phản hồi thành công,
# chỉ là AI không bao giờ nhận được, và nửa số ca "accepted" vô hình với nửa còn lại.

# (a) prescription_history — id do container HTTP cấp, container gRPC phải nhận ra
XPID="$(python3 -c "
import json,urllib.request
rows=[[3.9-0.01*i,-1.5,24.0,float(i*10)] for i in range(30)]
d=json.dumps({'battery_id':'__xstore__','readings':rows,'enrich':True}).encode()
try:
    r=urllib.request.urlopen(urllib.request.Request('$AI_HTTP/prescribe/',data=d,
        headers={'Content-Type':'application/json'}),timeout=180)
    print(json.load(r).get('prescription_id',''))
except Exception:
    print('')" 2>/dev/null)"

if [ -z "$XPID" ]; then
  miss "không lấy được prescription_id từ container HTTP"
else
  XR="$(docker exec solar-ai-module-http python -c "
import grpc
from src.grpc_gen import ai_service_pb2 as pb, ai_service_pb2_grpc as pbg
st=pbg.AiServiceStub(grpc.insecure_channel('ai-module-grpc:50051'))
try:
    st.SubmitFeedback(pb.SubmitFeedbackRequest(prescription_id='$XPID',status='accepted'),timeout=30)
    print('SHARED')
except grpc.RpcError as e:
    print(e.code().name)" 2>/dev/null | tail -1)"
  [ "$XR" = "SHARED" ] \
    && pass "prescription_history dùng chung (HTTP ghi → gRPC đọc được)" \
    || miss "prescription_history TÁCH ĐÔI: gRPC trả $XR cho id do HTTP cấp — phản hồi kỹ thuật viên sẽ mất trắng"
fi

# (b) classification_feedback — bộ đếm phải liên tục giữa hai container
CF_H="$(curl -fsS -X POST "$AI_HTTP/predict/feedback" -H 'Content-Type: application/json' \
  -d '{"battery_id":"__xstore__","classification":"Normal","verdict":"correct"}' 2>/dev/null | jqp total)"
CF_G="$(docker exec solar-ai-module-http python -c "
import grpc
from src.grpc_gen import ai_service_pb2 as pb, ai_service_pb2_grpc as pbg
st=pbg.AiServiceStub(grpc.insecure_channel('ai-module-grpc:50051'))
print(st.SubmitClassificationFeedback(pb.ClassificationFeedbackRequest(
    battery_id='__xstore__',classification='Normal',verdict='correct'),timeout=20).total)" 2>/dev/null | tail -1)"
if [ -n "$CF_H" ] && [ -n "$CF_G" ] && [ "$CF_G" -eq "$((CF_H + 1))" ] 2>/dev/null; then
  pass "classification_feedback dùng chung (HTTP=$CF_H → gRPC=$CF_G, liên tục)"
else
  miss "classification_feedback TÁCH ĐÔI: HTTP=$CF_H nhưng gRPC=$CF_G (phải = HTTP+1)"
fi

section "8. BE → AI · Health client đọc được soc_mode"
HJ="$(curl -fsS "$AI_HTTP/health" 2>/dev/null)"
for f in lfp_loaded soc_mode lfp_soc_mode long_model_version prescription_metrics; do
  echo "$HJ" | grep -q "\"$f\"" && pass "  health.$f" || miss "  health.$f THIẾU"
done

echo ""
echo "════════════════════════════════════════"
echo "  E2E AI↔BE: $PASS passed, $FAIL failed"
echo "════════════════════════════════════════"
[ "$FAIL" -eq 0 ] || exit 1
