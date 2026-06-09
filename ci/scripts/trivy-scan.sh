#!/usr/bin/env bash
# Trivy filesystem security scan — mirror Jenkinsfile stage 6.
# Fail nếu có CVE CRITICAL có fix.
#
# Env:
#   TRIVY_CACHE_DIR  → cache dir (default: .trivy-cache)
#   SKIP_TRIVY=1     → skip hoàn toàn (in warning, exit 0)

set -euo pipefail

if [ "${SKIP_TRIVY:-0}" = "1" ]; then
  echo "SKIP: trivy scan bị skip qua SKIP_TRIVY=1"
  exit 0
fi

if ! command -v trivy >/dev/null 2>&1; then
  echo "WARN: trivy chưa cài — skip scan. Cài qua: brew install aquasecurity/trivy/trivy"
  exit 0
fi

TRIVY_CACHE_DIR="${TRIVY_CACHE_DIR:-.trivy-cache}"
mkdir -p "$TRIVY_CACHE_DIR"

trivy fs \
  --cache-dir "$TRIVY_CACHE_DIR" \
  --quiet \
  --scanners vuln \
  --severity CRITICAL \
  --exit-code 1 \
  --ignore-unfixed \
  --skip-dirs .git \
  --skip-dirs TestResults \
  --skip-dirs .trivy-cache \
  .
