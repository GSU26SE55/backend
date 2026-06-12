#!/usr/bin/env bash
# Smoke test after deploy.
# Verifies the public gateway health endpoint + Sprint 5B Saga health endpoint.
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

probe_endpoint() {
  local path="$1"
  curl -fsSk --max-time 10 "${HOST}${path}" > /dev/null 2>&1
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
    echo "OK - all ${#ENDPOINTS[@]} endpoints reachable (gateway + ticket + saga)"
    exit 0
  fi

  sleep "$SLEEP"
done

echo "FAIL - one or more endpoints did not respond after ${MAX_RETRY} retries"
exit 1
