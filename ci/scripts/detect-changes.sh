#!/usr/bin/env bash
# Detect service nào thay đổi giữa HEAD và origin/main.
# Output: list service name (apigateway authservice ...) — Jenkins skip build các service không đổi.
#
# Cách dùng:
#   CHANGED=$(./ci/scripts/detect-changes.sh)
#   echo $CHANGED   # → "authservice apigateway"
#
# Lưu ý: nếu shared/* hoặc Dockerfile.template đổi → build TẤT CẢ.

set -euo pipefail

BASE="${BASE_REF:-origin/main}"
HEAD="${HEAD_REF:-HEAD}"

# Lấy list path đã đổi
CHANGED_PATHS=$(git diff --name-only "$BASE...$HEAD" 2>/dev/null || git diff --name-only HEAD~1...HEAD)

# Nếu shared library đổi → build all
ALL_SERVICES="apigateway authservice emailservice smsservice filestorageservice batteryservice ticketservice notificationservice"

if echo "$CHANGED_PATHS" | grep -qE "^shared/|^Dockerfile.template|^SolarBatteryMaintainance.slnx"; then
  echo "$ALL_SERVICES"
  exit 0
fi

# Map prefix path → service name
SERVICES=()
echo "$CHANGED_PATHS" | grep -E "^services/ApiGateway"          > /dev/null && SERVICES+=("apigateway")
echo "$CHANGED_PATHS" | grep -E "^services/AuthService"         > /dev/null && SERVICES+=("authservice")
echo "$CHANGED_PATHS" | grep -E "^services/EmailService"        > /dev/null && SERVICES+=("emailservice")
echo "$CHANGED_PATHS" | grep -E "^services/SmsService"          > /dev/null && SERVICES+=("smsservice")
echo "$CHANGED_PATHS" | grep -E "^services/FileStorageService"  > /dev/null && SERVICES+=("filestorageservice")
echo "$CHANGED_PATHS" | grep -E "^services/BatteryService"      > /dev/null && SERVICES+=("batteryservice")
echo "$CHANGED_PATHS" | grep -E "^services/TicketService"       > /dev/null && SERVICES+=("ticketservice")
echo "$CHANGED_PATHS" | grep -E "^services/NotificationService" > /dev/null && SERVICES+=("notificationservice")

# Nếu KHÔNG service nào đổi (chỉ docs, monitoring, etc.) → build all để safe
if [ ${#SERVICES[@]} -eq 0 ]; then
  echo "$ALL_SERVICES"
else
  echo "${SERVICES[@]}"
fi
