#!/usr/bin/env bash
# Smoke test sau deploy — verify gateway sống + 1 request thành công.
# Dùng: ./ci/scripts/smoke-test.sh https://api.staging.example.com

set -euo pipefail
HOST="${1:-https://api.staging.example.com}"
MAX_RETRY=10
SLEEP=10

for i in $(seq 1 $MAX_RETRY); do
  echo "[smoke #$i] curl ${HOST}/metrics"
  if curl -fsS --max-time 10 "${HOST}/metrics" > /dev/null 2>&1; then
    echo "OK — gateway sống và serve /metrics"
    exit 0
  fi
  sleep $SLEEP
done

echo "FAIL — sau ${MAX_RETRY} lần retry vẫn không response"
exit 1
