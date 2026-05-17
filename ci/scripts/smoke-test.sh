#!/usr/bin/env bash
# Smoke test after deploy. Verifies the public gateway health endpoint.
# Usage: ./ci/scripts/smoke-test.sh https://api.staging.example.com

set -euo pipefail

HOST="${1:-https://api.staging.example.com}"
MAX_RETRY="${MAX_RETRY:-10}"
SLEEP="${SMOKE_SLEEP_SECONDS:-10}"

for i in $(seq 1 "$MAX_RETRY"); do
  echo "[smoke #$i] curl ${HOST}/health"
  if curl -fsSk --max-time 10 "${HOST}/health" > /dev/null 2>&1; then
    echo "OK - gateway health endpoint is reachable"
    exit 0
  fi

  sleep "$SLEEP"
done

echo "FAIL - gateway health endpoint did not respond after ${MAX_RETRY} retries"
exit 1
