#!/usr/bin/env bash
# ==================================================================
# Sprint 4 — S4-INF-02 (#37): Sinh CA tự ký + server cert cho Mosquitto MQTT/TLS.
#
# Output (đặt vào infra/mqtt/mosquitto/certs/):
#   ca.crt          — CA cert (ESP32 cần copy vào data/ca_cert.pem cho LittleFS)
#   ca.key          — CA private key (giữ kín, KHÔNG commit)
#   server.crt      — broker server cert (SAN bao gồm localhost + IP)
#   server.key      — broker server private key
#   server.csr      — CSR trung gian (xóa được sau khi sign)
#
# Cách dùng:
#   ./infra/mqtt/scripts/gen-certs.sh                              # default CN=localhost
#   ./infra/mqtt/scripts/gen-certs.sh mosquitto                    # CN=mosquitto (docker network)
#   MQTT_SAN_IP=10.0.0.10 ./infra/mqtt/scripts/gen-certs.sh        # thêm IP vào SAN
#   ./infra/mqtt/scripts/gen-certs.sh --force                      # ghi đè cert cũ
#
# Sau khi chạy:
#   1. (tự động) script sinh luôn `mosquitto/config/conf.d/tls.conf` khai listener 8883.
#      KHÔNG còn bước uncomment tay trong mosquitto.conf.
#   2. `docker compose restart mosquitto`.
#   3. Test: mosquitto_pub -h localhost -p 8883 --cafile ca.crt -t test -m ok
#   4. ESP32: copy ca.crt → firmware-esp32/data/ca_cert.pem
#            pio run -t uploadfs    (upload LittleFS data/)
#
# Note (security):
#   - CA này là self-signed → CHỈ DÙNG cho dev/pilot, KHÔNG production.
#   - Production: dùng Let's Encrypt hoặc PKI doanh nghiệp.
#   - ca.key + server.key đặt mode 0600. .gitignore loại trừ `certs/*.key`.
# ==================================================================

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
CERTS_DIR="$(cd "$SCRIPT_DIR/../mosquitto/certs" && pwd)"

DAYS_CA=3650            # CA hiệu lực 10 năm
DAYS_SERVER=825         # Server cert ≤ 825 ngày theo CAB Forum
KEY_BITS=2048

# Parse args
FORCE=0
CN="${1:-localhost}"
for arg in "$@"; do
  case "$arg" in
    --force) FORCE=1 ;;
    --help|-h)
      grep '^#' "$0" | sed 's/^# \{0,1\}//'
      exit 0
      ;;
    --*)
      echo "[gen-certs] Unknown flag: $arg" >&2
      exit 1
      ;;
  esac
done

# SAN extra IP (mặc định 127.0.0.1)
SAN_IP_EXTRA="${MQTT_SAN_IP:-}"

CA_CRT="$CERTS_DIR/ca.crt"
CA_KEY="$CERTS_DIR/ca.key"
SRV_CRT="$CERTS_DIR/server.crt"
SRV_KEY="$CERTS_DIR/server.key"
SRV_CSR="$CERTS_DIR/server.csr"
CFG_CA="$CERTS_DIR/ca.cnf"
CFG_SRV="$CERTS_DIR/server.cnf"
SRV_EXT="$CERTS_DIR/server.ext"

# Check existing
if [[ -f "$SRV_CRT" && "$FORCE" -eq 0 ]]; then
  echo "[gen-certs] cert đã tồn tại — dùng --force để regen."
  echo "[gen-certs] Path: $SRV_CRT"
  openssl x509 -in "$SRV_CRT" -noout -subject -dates -ext subjectAltName 2>/dev/null || true
  exit 0
fi

# Pre-flight
if ! command -v openssl >/dev/null 2>&1; then
  echo "[gen-certs] ERROR: openssl không có trong PATH" >&2
  exit 1
fi

mkdir -p "$CERTS_DIR"

echo "[gen-certs] CN=$CN  SAN_IP_EXTRA=${SAN_IP_EXTRA:-(none)}  Output: $CERTS_DIR"

# --- 1) CA cnf ---
# v3_ca extensions BẮT BUỘC để openssl/mbedTLS chấp nhận đây là CA hợp lệ.
# Thiếu basicConstraints=CA:TRUE → "invalid CA certificate" (verify err 79).
# Thiếu keyUsage keyCertSign → "unsuitable certificate purpose" (verify err 26).
cat > "$CFG_CA" <<EOF
[req]
distinguished_name = req_distinguished_name
x509_extensions    = v3_ca
prompt             = no
[req_distinguished_name]
C  = VN
ST = Hanoi
L  = Hanoi
O  = GSU26SE55 IoT Dev CA
CN = GSU26SE55 IoT Root CA
[v3_ca]
basicConstraints       = critical, CA:TRUE
keyUsage               = critical, keyCertSign, cRLSign
subjectKeyIdentifier   = hash
EOF

# --- 2) Server cnf ---
SAN_LINES="DNS.1 = $CN
DNS.2 = localhost
DNS.3 = mosquitto
DNS.4 = iot-mosquitto
IP.1  = 127.0.0.1"
if [[ -n "$SAN_IP_EXTRA" ]]; then
  SAN_LINES="$SAN_LINES
IP.2  = $SAN_IP_EXTRA"
fi

cat > "$CFG_SRV" <<EOF
[req]
distinguished_name = req_distinguished_name
req_extensions     = v3_req
prompt             = no
[req_distinguished_name]
C  = VN
ST = Hanoi
L  = Hanoi
O  = GSU26SE55 IoT Dev
CN = $CN
[v3_req]
subjectAltName = @alt_names
[alt_names]
$SAN_LINES
EOF

cat > "$SRV_EXT" <<EOF
authorityKeyIdentifier = keyid,issuer
basicConstraints       = CA:FALSE
keyUsage               = digitalSignature, keyEncipherment
extendedKeyUsage       = serverAuth
subjectAltName         = @alt_names
[alt_names]
$SAN_LINES
EOF

# --- 3) Sinh CA ---
echo "[gen-certs] [1/4] generate CA key + cert..."
openssl genrsa -out "$CA_KEY" "$KEY_BITS" 2>/dev/null
openssl req -x509 -new -nodes -key "$CA_KEY" -sha256 -days "$DAYS_CA" \
  -config "$CFG_CA" -out "$CA_CRT"

# --- 4) Sinh server key + CSR ---
echo "[gen-certs] [2/4] generate server key + CSR..."
openssl genrsa -out "$SRV_KEY" "$KEY_BITS" 2>/dev/null
openssl req -new -key "$SRV_KEY" -out "$SRV_CSR" -config "$CFG_SRV"

# --- 5) CA sign server CSR ---
echo "[gen-certs] [3/4] CA sign server CSR..."
openssl x509 -req -in "$SRV_CSR" -CA "$CA_CRT" -CAkey "$CA_KEY" -CAcreateserial \
  -out "$SRV_CRT" -days "$DAYS_SERVER" -sha256 -extfile "$SRV_EXT"

# --- 6) Permissions + cleanup ---
echo "[gen-certs] [4/4] set permissions + cleanup..."
chmod 0600 "$CA_KEY" "$SRV_KEY"
chmod 0644 "$CA_CRT" "$SRV_CRT"
# Defense-in-depth: cnf chứa DN info — không phải secret nhưng restrict cho consistency.
chmod 0600 "$CFG_CA" "$CFG_SRV"
# Cleanup: giữ ca.cnf/server.cnf cho regen, xóa csr + ext + srl trung gian
rm -f "$SRV_CSR" "$SRV_EXT" "$CERTS_DIR/ca.srl"

# --- 6b) Sinh conf.d/tls.conf (2026-07-31) ---
# Trước đây bước này là "uncomment tay block listener 8883 trong mosquitto.conf" → hoặc bị quên
# (TLS không bao giờ bật), hoặc làm khi chưa có cert (broker fail to start). Nay listener TLS được
# sinh RA CÙNG cert, nên hai thứ không bao giờ lệch nhau.
#
# Lưu ý Mosquitto 2.0 (đã kiểm chứng bằng broker thật 31/07/2026):
#   - `password_file` và `acl_file` là option TOÀN CỤC. Khai lại trong conf.d sẽ làm broker CHẾT với
#     "Error: Duplicate password_file value in configuration." ⇒ tls.conf KHÔNG được lặp 2 dòng này;
#     chúng đã áp cho mọi listener, kể cả 8883.
#   - `allow_anonymous` thì per-listener, nên VẪN phải khai trong tls.conf — thiếu nó là cổng 8883
#     cho vào tự do trong khi 1883 vẫn siết.
CONF_D="$(cd "$SCRIPT_DIR/../mosquitto/config" && pwd)/conf.d"
mkdir -p "$CONF_D"
cat > "$CONF_D/tls.conf" <<EOF
# SINH TỰ ĐỘNG bởi scripts/gen-certs.sh — đừng sửa tay, chạy lại script để cập nhật.
# Xoá file này (hoặc xoá certs/) là tắt TLS, broker vẫn chạy với cổng 1883.
listener 8883 0.0.0.0
cafile /mosquitto/certs/ca.crt
certfile /mosquitto/certs/server.crt
keyfile /mosquitto/certs/server.key
require_certificate false
tls_version tlsv1.2

# allow_anonymous là per-listener ⇒ BẮT BUỘC khai cho 8883.
# KHÔNG khai password_file/acl_file ở đây: chúng toàn cục, lặp lại là broker fail to start.
allow_anonymous false
EOF
chmod 0644 "$CONF_D/tls.conf"

echo ""
# --- 7) Expiry awareness output ---
SRV_EXPIRY=$(openssl x509 -in "$SRV_CRT" -noout -enddate | sed 's/notAfter=//')
CA_EXPIRY=$(openssl x509 -in "$CA_CRT" -noout -enddate | sed 's/notAfter=//')

echo "=============================================================="
echo "[gen-certs] ✓ Done"
echo ""
echo "  CA cert:     $CA_CRT      (expires: $CA_EXPIRY)"
echo "  Server cert: $SRV_CRT  (expires: $SRV_EXPIRY)"
echo ""
echo "  ⚠ Expiry impact:"
echo "    - Server cert hết hạn → tất cả device fail TLS handshake với 'cert expired'."
echo "    - CA cert hết hạn → toàn bộ cert chain invalid (10 năm nữa)."
echo "    - Set reminder regen server cert ≥ 30 ngày trước notAfter ($SRV_EXPIRY)."
echo "    - Regen server cert KHÔNG cần update CA — devices giữ ca_cert.pem cũ vẫn verify."
echo "    - Regen CA → PHẢI OTA push CA mới xuống mọi device (Sprint 7)."
echo ""
echo "  ⛔ KEY HANDLING — KHÔNG được:"
echo "    × Commit *.key vào git (đã gitignore certs/*.key)"
echo "    × Copy server.key hoặc ca.key vào firmware-esp32/data/ — chỉ copy ca.crt"
echo "    × Email/Slack/Paste keys — secret leak."
echo "    × Chạy gen-certs.sh bằng sudo/root — file sẽ owned by root, broker container không đọc được."
echo ""
echo "  Next steps:"
echo "    1. (đã tự động) conf.d/tls.conf vừa được sinh — KHÔNG cần sửa mosquitto.conf nữa"
echo "    2. docker compose restart mosquitto"
echo "    3. Test TLS:"
echo "         mosquitto_pub -h $CN -p 8883 --cafile $CA_CRT \\"
echo "           -u backend-bridge -P \"\$MQTT_PASSWORD\" \\"
echo "           -t solar/test/status -m online"
echo "    4. Copy CA PUBLIC cert → ESP32 LittleFS (KHÔNG copy *.key):"
echo "         cp $CA_CRT firmware-esp32/data/ca_cert.pem"
echo "         pio run -t uploadfs"
echo "=============================================================="
echo ""
openssl x509 -in "$SRV_CRT" -noout -subject -dates -ext subjectAltName
