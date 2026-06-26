#!/usr/bin/env bash
# Sprint 7 #119 — End-to-end smoke test qua ApiGateway.
#
# Phủ các scenario (mức smoke — verify endpoint sống + trả schema đúng, KHÔNG drive full lifecycle):
#   1. Health tất cả service qua gateway
#   2. Golden path        : admin login → list batteries → list tickets
#   3. Reports (#114)      : battery + ticket reports trả JSON; export XLSX trả đúng content-type
#   4. SLA breach          : /api/reports/sla-by-priority có đủ P1/P2/P3
#   5. Reopen              : /api/reports/top-reopen-issues phản hồi
#   6. Incident lifecycle  : /api/reports/environmental-incidents phản hồi
#   7. Saga                : /api/reports/saga-failed-rate (Admin) + saga admin endpoint reachable
#
# Mỗi check in PASS/FAIL; cuối cùng exit !=0 nếu có FAIL. KHÔNG sửa dữ liệu (read-only smoke).
#
# Env (default): GATEWAY_URL=http://localhost:4001, ADMIN_EMAIL/ADMIN_PASSWORD (đọc .env.Docker).

set -uo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
GATEWAY_URL="${GATEWAY_URL:-http://localhost:4001}"
ENV_FILE="${ENV_FILE:-$REPO_ROOT/.env.Docker}"

read_env() { [ -f "$ENV_FILE" ] && (grep -E "^$1=" "$ENV_FILE" | head -1 | cut -d= -f2-) || true; }
ADMIN_EMAIL="${ADMIN_EMAIL:-$(read_env ADMIN_EMAIL)}"
ADMIN_PASSWORD="${ADMIN_PASSWORD:-$(read_env ADMIN_PASSWORD)}"

PASS=0; FAIL=0
pass() { echo "  ✅ $*"; PASS=$((PASS+1)); }
miss() { echo "  ❌ $*"; FAIL=$((FAIL+1)); }
section() { echo ""; echo "▶ $*"; }

# check_status <name> <method> <path> <expected_status> [auth] [extra curl args...]
check_status() {
  local name="$1" method="$2" path="$3" expected="$4"; shift 4
  local code
  code="$(curl -s -o /dev/null -w '%{http_code}' -X "$method" "$GATEWAY_URL$path" "$@" 2>/dev/null || echo 000)"
  if [ "$code" = "$expected" ]; then pass "$name ($code)"; else miss "$name (expected $expected, got $code)"; fi
}

section "1. Health các service qua gateway"
check_status "gateway /health"        GET /health                 200
check_status "battery health"          GET /api/battery/health     200
check_status "ticket health"           GET /api/ticket/health      200

section "2. Golden path — admin login"
TOKEN=""
if [ -n "$ADMIN_EMAIL" ] && [ -n "$ADMIN_PASSWORD" ]; then
  TOKEN="$(curl -fsS -X POST "$GATEWAY_URL/api/auth/login" -H 'Content-Type: application/json' \
    -d "{\"email\":\"$ADMIN_EMAIL\",\"password\":\"$ADMIN_PASSWORD\"}" 2>/dev/null \
    | grep -oE '"accessToken"[: ]*"[^"]+"' | head -1 | cut -d'"' -f4 || true)"
fi
if [ -n "$TOKEN" ]; then pass "admin login → token"; else miss "admin login (không lấy được token)"; fi
AUTH=(-H "Authorization: Bearer $TOKEN")

check_status "list battery-assets" GET "/api/battery-assets?pageNumber=1&pageSize=5" 200 "${AUTH[@]}"
check_status "list tickets"        GET "/api/admin/tickets?pageNumber=1&pageSize=5"  200 "${AUTH[@]}"

section "3. Reports (#114) — JSON"
check_status "battery-health-by-type" GET /api/reports/battery-health-by-type 200 "${AUTH[@]}"
check_status "category-breakdown"      GET /api/reports/category-breakdown      200 "${AUTH[@]}"

section "3b. Reports — export XLSX (content-type)"
CT="$(curl -s -o /dev/null -w '%{content_type}' "$GATEWAY_URL/api/reports/battery-health-by-type?format=xlsx" "${AUTH[@]}" 2>/dev/null || echo '')"
case "$CT" in
  *spreadsheetml.sheet*) pass "xlsx export content-type ($CT)";;
  *) miss "xlsx export content-type (got '$CT')";;
esac

section "4. SLA breach — sla-by-priority có P1/P2/P3"
BODY="$(curl -fsS "$GATEWAY_URL/api/reports/sla-by-priority" "${AUTH[@]}" 2>/dev/null || echo '')"
if echo "$BODY" | grep -q "P1Critical" && echo "$BODY" | grep -q "P3Normal"; then
  pass "sla-by-priority có đủ priority"
else miss "sla-by-priority thiếu priority (body: ${BODY:0:80})"; fi

section "5. Reopen — top-reopen-issues"
check_status "top-reopen-issues" GET /api/reports/top-reopen-issues 200 "${AUTH[@]}"

section "6. Incident lifecycle — environmental-incidents"
check_status "environmental-incidents report" GET /api/reports/environmental-incidents 200 "${AUTH[@]}"

section "7. Saga — saga-failed-rate (Admin) + saga admin endpoint"
check_status "saga-failed-rate report" GET /api/reports/saga-failed-rate 200 "${AUTH[@]}"
check_status "saga admin endpoint"      GET /api/admin/sagas/alert-ticket  200 "${AUTH[@]}"

echo ""
echo "════════════════════════════════════════"
echo "  E2E smoke: $PASS passed, $FAIL failed"
echo "════════════════════════════════════════"
[ "$FAIL" -eq 0 ] || exit 1
