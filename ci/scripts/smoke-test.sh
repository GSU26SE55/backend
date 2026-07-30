#!/usr/bin/env bash
# Smoke test after deploy.
# Verifies the public gateway health endpoint + Sprint 5B Saga health endpoint
# + Sprint IoT-1 #252 IoT routes registered correctly in YARP.
# Usage: ./ci/scripts/smoke-test.sh https://api.staging.example.com

set -euo pipefail

HOST="${1:-https://api.staging.example.com}"
MAX_RETRY="${MAX_RETRY:-10}"
SLEEP="${SMOKE_SLEEP_SECONDS:-10}"

# Endpoints — gateway phải trả 200 OK cho tất cả.
# /health                       — ApiGateway root health
# /api/ticket/health            — TicketService health (Sprint 3)
# /api/ticket/health/saga       — Sprint 5B #239 Saga health (Healthy/Warning/Degraded)
ENDPOINTS=(
  "/health"
  "/api/ticket/health"
  "/api/ticket/health/saga"
)

# Sprint IoT-1/IoT-2 — IoT routes yêu cầu auth. Smoke chỉ verify route exist:
# 200 (route public — không có ở IoT), 401/403 (auth check chạy — OK), 404 (FAIL, route chưa map).
IOT_ROUTES=(
  "/api/iot-devices/firmware-check?currentVersion=0.0.0"
  "/api/admin/iot-devices"
  "/api/admin/iot-firmware-releases"
  # Sprint IoT-2 #IoT2-32 — calibration CRUD (Staff/Admin) + expiring filter (Manager).
  "/api/iot-devices/calibrations-expiring?within=30"
  # Sprint 5B #237 — Admin saga reprocess endpoint.
  "/api/admin/sagas/alert-ticket"
)

# NotificationService — endpoint đều có [Authorize] nên smoke chỉ verify route map:
# 401 = OK (route tồn tại, auth handler chạy), 404 = FAIL (gateway chưa map / service chết).
#
# `/api/notification-preferences` từng bị loại khỏi danh sách vì controller chưa tồn tại.
# Sprint 6.3 NOTI3-10 đã tạo PreferencesController (kèm ma trận theo nhóm), đo thực tế qua
# gateway trả 401 → đưa vào danh sách.
NOTIFICATION_ROUTES=(
  "/api/notifications"
  "/api/notification-preferences"
  "/api/notification-preferences/matrix"
  "/api/admin/notification-templates"
)

# Endpoint CÔNG KHAI (không [Authorize]) — không bao giờ trả 401, nên cần ngưỡng riêng.
#
# `/api/notification-unsubscribe` là nơi Gmail/Yahoo POST thẳng vào khi người nhận bấm
# "Hủy đăng ký" (List-Unsubscribe-Post, Sprint 6.3 NOTI3-15). Route này hỏng thì nút hủy
# chết lặng và tên miền bị hạ uy tín gửi — nên đáng canh trong smoke. Không kèm token hợp lệ
# thì đúng chuẩn phải là 400: route sống, token sai.
PUBLIC_ROUTES=(
  "/api/notification-unsubscribe"
)

# Sprint SMS — SmsService gateway endpoints.
# /api/sms-gateway/heartbeat       — Flutter device heartbeat (auth GatewayApiKey scheme)
# /api/admin/sms-gateway/devices   — Admin list device (auth JWT Bearer + role Admin)
# /hubs/sms-gateway/negotiate      — SignalR Hub negotiate (auth GatewayApiKey, smoke check route registered)
# Tất cả đều yêu cầu auth → 401 = OK (route mapped), 404 = FAIL (gateway misconfig).
SMS_ROUTES=(
  "/api/sms-gateway/heartbeat"
  "/api/admin/sms-gateway/devices"
  "/hubs/sms-gateway/negotiate"
)

probe_endpoint() {
  local path="$1"
  curl -fsSk --max-time 10 "${HOST}${path}" > /dev/null 2>&1
}

# Probe IoT route — chấp nhận 200/401/403 (route đã register + auth handler chạy).
# Reject 404/5xx (gateway misconfig hoặc service crash).
probe_iot_route() {
  local path="$1"
  local code
  code=$(curl -sk --max-time 10 -o /dev/null -w '%{http_code}' "${HOST}${path}" 2>/dev/null || echo "000")
  case "$code" in
    # 405 cũng là bằng chứng route TỒN TẠI: gateway đã định tuyến tới service, service chỉ từ
    # chối HTTP method. Probe này dùng GET cho mọi đường dẫn, nên endpoint chỉ nhận POST
    # (vd /api/sms-gateway/heartbeat) tất yếu trả 405 — coi đó là hỏng thì stage smoke luôn đỏ,
    # kéo theo `post { failure }` rollback bản helm vừa deploy dù hệ thống hoàn toàn bình thường.
    # Chỉ 404 (chưa map) và 5xx (service chết) mới là hỏng thật.
    200|401|403|405) return 0 ;;
    *)
      echo "  -> ${HOST}${path} returned HTTP $code (expected 200/401/403/405)"
      return 1
      ;;
  esac
}

# Probe route công khai — chấp nhận 200 (thành công) và 400 (route sống, tham số sai).
# Reject 404 (chưa map) và 5xx (service hỏng) — đúng hai thứ smoke cần bắt.
probe_public_route() {
  local path="$1"
  local code
  code=$(curl -sk --max-time 10 -o /dev/null -w '%{http_code}' "${HOST}${path}" 2>/dev/null || echo "000")
  case "$code" in
    200|400) return 0 ;;
    *)
      echo "  -> ${HOST}${path} returned HTTP $code (expected 200/400)"
      return 1
      ;;
  esac
}

for i in $(seq 1 "$MAX_RETRY"); do
  all_ok=1
  for ep in "${ENDPOINTS[@]}"; do
    echo "[smoke #$i] curl ${HOST}${ep}"
    if ! probe_endpoint "$ep"; then
      all_ok=0
      break
    fi
  done

  if [ "$all_ok" -eq 1 ]; then
    for ep in "${IOT_ROUTES[@]}"; do
      echo "[smoke #$i] iot ${HOST}${ep}"
      if ! probe_iot_route "$ep"; then
        all_ok=0
        break
      fi
    done
  fi

  if [ "$all_ok" -eq 1 ]; then
    for ep in "${NOTIFICATION_ROUTES[@]}"; do
      echo "[smoke #$i] notification ${HOST}${ep}"
      # Reuse probe_iot_route — semantics giống nhau (chấp nhận 200/401/403, reject 404/5xx).
      if ! probe_iot_route "$ep"; then
        all_ok=0
        break
      fi
    done
  fi

  if [ "$all_ok" -eq 1 ]; then
    for ep in "${SMS_ROUTES[@]}"; do
      echo "[smoke #$i] sms ${HOST}${ep}"
      # Reuse probe_iot_route — semantics giống nhau (chấp nhận 200/401/403, reject 404/5xx).
      if ! probe_iot_route "$ep"; then
        all_ok=0
        break
      fi
    done
  fi

  if [ "$all_ok" -eq 1 ]; then
    for ep in "${PUBLIC_ROUTES[@]}"; do
      echo "[smoke #$i] public ${HOST}${ep}"
      if ! probe_public_route "$ep"; then
        all_ok=0
        break
      fi
    done
  fi

  if [ "$all_ok" -eq 1 ]; then
    echo "OK - all ${#ENDPOINTS[@]} health + ${#IOT_ROUTES[@]} IoT + ${#NOTIFICATION_ROUTES[@]} notification + ${#SMS_ROUTES[@]} sms + ${#PUBLIC_ROUTES[@]} public routes reachable"
    exit 0
  fi

  sleep "$SLEEP"
done

echo "FAIL - one or more endpoints did not respond after ${MAX_RETRY} retries"
exit 1
