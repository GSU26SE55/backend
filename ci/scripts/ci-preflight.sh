#!/usr/bin/env bash
# CI Preflight — mirror Jenkinsfile stage 0.
# Check tool versions (git, dotnet ≥ 9). Trivy optional cho local (chỉ warn).
#
# Env:
#   REQUIRE_TRIVY=1   → fail nếu thiếu trivy (Jenkins set =1; local default =0)

set -euo pipefail

REQUIRE_TRIVY="${REQUIRE_TRIVY:-0}"

for tool in git dotnet; do
  command -v "$tool" >/dev/null 2>&1 || {
    echo "FAIL: missing required CI tool: $tool"
    exit 1
  }
done

if ! command -v trivy >/dev/null 2>&1; then
  if [ "$REQUIRE_TRIVY" = "1" ]; then
    echo "FAIL: missing required CI tool: trivy"
    exit 1
  fi
  echo "WARN: trivy not installed — security scan sẽ bị skip (set REQUIRE_TRIVY=1 để bắt buộc)."
fi

DOTNET_VERSION="$(dotnet --version)"
DOTNET_MAJOR="${DOTNET_VERSION%%.*}"
if [ "$DOTNET_MAJOR" -lt 9 ]; then
  echo "FAIL: SolarBatteryMaintainance.slnx yêu cầu .NET SDK 9.x trở lên. Current: ${DOTNET_VERSION}"
  exit 1
fi

echo "CI preflight OK. dotnet=${DOTNET_VERSION}"
