#!/usr/bin/env bash
# Sprint 7 #119 — Full seed orchestrator cho demo end-to-end.
#
# Mỗi service tự seed khi startup (Program.cs gọi *DataSeeder.SeedAsync):
#   - AuthService      : admin (ADMIN_EMAIL/ADMIN_PASSWORD) + sample accounts + roles + login attempts
#   - BatteryService   : sites + battery assets + threshold configs
#                        + EnvironmentDataSeeder: ambient readings + 1 historical incident
#   - TicketService    : sample tickets + 2 Saga seed row (Sprint 5B carryover)
#   - NotificationService: notification templates
#
# Script này: đảm bảo stack đã chạy → chờ healthy → verify seed → in demo credentials.
# Với --up: tự `docker compose up -d --build` trước.
#
# Idempotent — chạy nhiều lần OK (seeder check tồn tại; docker up idempotent).
#
# Cách dùng:
#   ./tools/seed.sh            # giả định stack đang chạy, chờ + verify
#   ./tools/seed.sh --up       # bring up stack trước rồi seed
#
# Env (đều có default):
#   GATEWAY_URL   (default http://localhost:4001)
#   ENV_FILE      (default <repo>/.env.Docker)
#   ADMIN_EMAIL   (đọc từ ENV_FILE nếu không set)
#   ADMIN_PASSWORD(đọc từ ENV_FILE nếu không set)
#   WAIT_TIMEOUT  (default 180 giây chờ gateway healthy)

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

GATEWAY_URL="${GATEWAY_URL:-http://localhost:4001}"
ENV_FILE="${ENV_FILE:-$REPO_ROOT/.env.Docker}"
WAIT_TIMEOUT="${WAIT_TIMEOUT:-180}"

# Đọc ADMIN_EMAIL/ADMIN_PASSWORD từ ENV_FILE nếu chưa set ngoài môi trường.
read_env() {
  local key="$1"
  if [ -f "$ENV_FILE" ]; then
    grep -E "^${key}=" "$ENV_FILE" | head -1 | cut -d= -f2- || true
  fi
}
ADMIN_EMAIL="${ADMIN_EMAIL:-$(read_env ADMIN_EMAIL)}"
ADMIN_PASSWORD="${ADMIN_PASSWORD:-$(read_env ADMIN_PASSWORD)}"

log()  { echo "🌱 $*"; }
ok()   { echo "✅ $*"; }
warn() { echo "⚠️  $*" >&2; }
fail() { echo "❌ $*" >&2; exit 1; }

if [ "${1:-}" = "--up" ]; then
  log "docker compose up -d --build (env-file=$ENV_FILE)..."
  ( cd "$REPO_ROOT" && docker compose --env-file "$ENV_FILE" up -d --build )
fi

log "Chờ gateway healthy tại $GATEWAY_URL/health (timeout ${WAIT_TIMEOUT}s)..."
deadline=$(( $(date +%s) + WAIT_TIMEOUT ))
until curl -fsS "$GATEWAY_URL/health" >/dev/null 2>&1; do
  if [ "$(date +%s)" -ge "$deadline" ]; then
    fail "Gateway chưa healthy sau ${WAIT_TIMEOUT}s. Chạy lại với --up hoặc kiểm tra docker compose ps."
  fi
  sleep 3
done
ok "Gateway healthy. Mọi service đã auto-seed khi startup."

# Verify seed bằng admin login + đếm dữ liệu chính.
if [ -n "$ADMIN_EMAIL" ] && [ -n "$ADMIN_PASSWORD" ]; then
  log "Verify admin login ($ADMIN_EMAIL)..."
  TOKEN="$(curl -fsS -X POST "$GATEWAY_URL/api/auth/login" \
    -H 'Content-Type: application/json' \
    -d "{\"email\":\"$ADMIN_EMAIL\",\"password\":\"$ADMIN_PASSWORD\"}" 2>/dev/null \
    | grep -oE '"accessToken"[: ]*"[^"]+"' | head -1 | cut -d'"' -f4 || true)"

  if [ -n "$TOKEN" ]; then
    ok "Admin login OK — seed AuthService hợp lệ."
    AUTH_HDR="Authorization: Bearer $TOKEN"
    BAT_COUNT="$(curl -fsS -H "$AUTH_HDR" "$GATEWAY_URL/api/battery-assets?pageNumber=1&pageSize=1" 2>/dev/null | grep -oE '"totalItems"[: ]*[0-9]+' | grep -oE '[0-9]+' | head -1 || echo '?')"
    log "BatteryAssets seeded (totalItems): ${BAT_COUNT}"
  else
    warn "Admin login không trả token — kiểm tra ADMIN_EMAIL/ADMIN_PASSWORD trong $ENV_FILE."
  fi
else
  warn "Thiếu ADMIN_EMAIL/ADMIN_PASSWORD — bỏ qua verify login (set env hoặc điền $ENV_FILE)."
fi

echo ""
ok "Seed hoàn tất."
echo "📋 Demo credentials:"
echo "   Admin: ${ADMIN_EMAIL:-<ADMIN_EMAIL>} / ${ADMIN_PASSWORD:-<ADMIN_PASSWORD>}"
echo "   Gateway: $GATEWAY_URL  |  Swagger: $GATEWAY_URL/swagger"
echo ""
echo "▶️  Chạy E2E smoke test: ./tools/e2e-smoke.sh"
