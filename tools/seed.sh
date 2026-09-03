#!/usr/bin/env bash
# Bootstrap verifier for local/Docker environments.
#
# Startup deliberately creates only the records required for the platform to operate:
#   - AuthService: four system roles, permission catalog/default bindings, six operational
#     accounts requested by the project owner, and the profiles required by those accounts.
#   - NotificationService: four immutable role-based recipient groups.
#
# Sites, batteries, thresholds, readings, incidents, alerts, tickets, SLAs, blogs,
# notification templates, notification history and login history are application data. They are
# no longer created by startup and must be created through their public APIs or real workflows.
#
# Usage:
#   ./tools/seed.sh
#   ./tools/seed.sh --up
#
# Environment:
#   GATEWAY_URL    default: http://localhost:4001
#   ENV_FILE       default: <repo>/.env.Docker
#   ADMIN_EMAIL    read from ENV_FILE when not already exported
#   ADMIN_PASSWORD read from ENV_FILE when not already exported
#   WAIT_TIMEOUT   default: 180 seconds

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

GATEWAY_URL="${GATEWAY_URL:-http://localhost:4001}"
ENV_FILE="${ENV_FILE:-$REPO_ROOT/.env.Docker}"
WAIT_TIMEOUT="${WAIT_TIMEOUT:-180}"

read_env() {
  local key="$1"
  if [ -f "$ENV_FILE" ]; then
    grep -E "^${key}=" "$ENV_FILE" | head -1 | cut -d= -f2- || true
  fi
}

ADMIN_EMAIL="${ADMIN_EMAIL:-$(read_env ADMIN_EMAIL)}"
ADMIN_PASSWORD="${ADMIN_PASSWORD:-$(read_env ADMIN_PASSWORD)}"

log()  { printf '🌱 %s\n' "$*"; }
ok()   { printf '✅ %s\n' "$*"; }
warn() { printf '⚠️  %s\n' "$*" >&2; }
fail() { printf '❌ %s\n' "$*" >&2; exit 1; }

if [ "${1:-}" = "--up" ]; then
  log "Starting the Docker stack..."
  (cd "$REPO_ROOT" && docker compose --env-file "$ENV_FILE" up -d --build)
elif [ -n "${1:-}" ]; then
  fail "Unknown argument: $1"
fi

log "Waiting for $GATEWAY_URL/health (timeout ${WAIT_TIMEOUT}s)..."
deadline=$(( $(date +%s) + WAIT_TIMEOUT ))
until curl -fsS "$GATEWAY_URL/health" >/dev/null 2>&1; do
  if [ "$(date +%s)" -ge "$deadline" ]; then
    fail "Gateway is not healthy after ${WAIT_TIMEOUT}s."
  fi
  sleep 3
done
ok "Gateway is healthy; required bootstrap records have been checked by service startup."

if [ -n "$ADMIN_EMAIL" ] && [ -n "$ADMIN_PASSWORD" ]; then
  log "Verifying bootstrap administrator login for $ADMIN_EMAIL..."
  token="$(curl -fsS -X POST "$GATEWAY_URL/api/auth/login" \
    -H 'Content-Type: application/json' \
    --data "{\"email\":\"$ADMIN_EMAIL\",\"password\":\"$ADMIN_PASSWORD\"}" 2>/dev/null \
    | grep -oE '"accessToken"[: ]*"[^"]+"' \
    | head -1 \
    | cut -d'"' -f4 || true)"

  if [ -n "$token" ]; then
    ok "Administrator login succeeded."
  else
    warn "Administrator login did not return a token; verify ADMIN_EMAIL/ADMIN_PASSWORD."
  fi
  unset token
else
  warn "ADMIN_EMAIL or ADMIN_PASSWORD is missing; login verification was skipped."
fi

ok "Bootstrap verification completed. No application/demo records were generated."
printf 'Gateway: %s | Swagger: %s/swagger\n' "$GATEWAY_URL" "$GATEWAY_URL"
