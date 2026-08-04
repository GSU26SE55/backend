#!/usr/bin/env bash
# Sprint IoT-1 (#253) — Bootstrap Mosquitto credentials lần đầu trước khi `docker compose up mosquitto`.
#
# Tác dụng:
#   1. Xoá directory `passwd` nếu Docker đã auto-tạo do bind mount fail trước đó.
#   2. Generate `passwd` file thật với user backend-bridge.
#   3. Set permission 0700 (Mosquitto 2.0 từ chối load nếu world-readable).
#
# Cách dùng:
#   ./infra/mqtt/bootstrap.sh                                     # password ngẫu nhiên
#   ./infra/mqtt/bootstrap.sh "my-strong-password"                # password chỉ định
#   ./infra/mqtt/bootstrap.sh --reset                             # xoá hết + tạo lại
#
# Output: in plaintext password ra stdout → COPY ngay vào .env / .env.Docker (Mqtt__Password=...).

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PASSWD_PATH="$SCRIPT_DIR/passwd"
USERNAME="${MQTT_USERNAME:-backend-bridge}"

# --- Parse args ---
RESET=0
PASSWORD=""
for arg in "$@"; do
  case "$arg" in
    --reset) RESET=1 ;;
    --help|-h)
      grep '^#' "$0" | sed 's/^# \{0,1\}//'
      exit 0
      ;;
    *) PASSWORD="$arg" ;;
  esac
done

# --- Step 1: Cleanup ---
if [[ -d "$PASSWD_PATH" ]]; then
  echo "[bootstrap] '$PASSWD_PATH' là directory (Docker auto-tạo) — xoá..."
  rm -rf "$PASSWD_PATH"
fi

if [[ -f "$PASSWD_PATH" && "$RESET" -eq 1 ]]; then
  echo "[bootstrap] --reset: xoá passwd cũ..."
  rm -f "$PASSWD_PATH"
fi

if [[ -f "$PASSWD_PATH" && "$RESET" -eq 0 ]]; then
  echo "[bootstrap] passwd file đã tồn tại — dùng --reset để regen."
  echo "[bootstrap] User hiện có: $(grep -c '^' "$PASSWD_PATH") entries"
  exit 0
fi

# --- Step 2: Generate password nếu chưa cung cấp ---
if [[ -z "$PASSWORD" ]]; then
  PASSWORD="$(openssl rand -base64 32 | tr -d '/+=' | head -c 32)"
  echo "[bootstrap] Password ngẫu nhiên đã sinh."
fi

# --- Step 3: Gen passwd file qua Mosquitto container ---
touch "$PASSWD_PATH"
docker run --rm \
  -v "$SCRIPT_DIR:/c" \
  eclipse-mosquitto:2.0 \
  mosquitto_passwd -b /c/passwd "$USERNAME" "$PASSWORD"

# --- Step 4: Permission ---
# Dev: 0644 vì Mosquitto trong container chạy uid 1883, không match host uid → cần world-readable.
# File chỉ chứa HASH (PBKDF2), không phải plaintext password — chấp nhận được cho dev.
# Mosquitto 2.0 sẽ warn "world readable" nhưng vẫn load.
#
# Production: dùng named volume + init container hoặc proper uid mapping → set 0600.
chmod 0644 "$PASSWD_PATH"

# --- Output ---
echo ""
echo "=============================================================="
echo "[bootstrap] ✓ Đã tạo passwd file"
echo "  Path:     $PASSWD_PATH"
echo "  Username: $USERNAME"
echo "  Password: $PASSWORD"
echo ""
echo "  PASTE password vào .env / .env.Docker:"
echo "    Mqtt__Password=$PASSWORD"
echo ""
echo "  Sau đó: docker compose up -d mosquitto"
echo "=============================================================="
