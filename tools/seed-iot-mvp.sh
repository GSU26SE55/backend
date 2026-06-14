#!/usr/bin/env bash
# Sprint IoT-2 #IoT2-02 (S1-BE-01) — seed BatteryService DB cho MVP IoT.
#
# Seeds (idempotent — chạy nhiều lần OK):
#   - 1× Site: "Solar Farm Long An" (Long An, VN)
#   - 4× BatteryAsset: BAT-2026-001..004 (LiFePO4 + NMC)
#   - 3× ThresholdConfig: voltage 11–14V, temp -10..60°C, SOC 20–100%
#
# Cách dùng:
#   ASPNETCORE_ENVIRONMENT=Development ./tools/seed-iot-mvp.sh
#
# Yêu cầu:
#   - DB battery_service_dev tồn tại + TimescaleDB extension
#   - .env / appsettings có ConnectionStrings:BatteryDb
#
# Cơ chế:
#   BatteryService tự seed khi startup (Program.cs gọi BatteryDataSeeder.SeedAsync).
#   Script này chỉ start API ở chế độ migrate+seed rồi shutdown ngay.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
API_DIR="$REPO_ROOT/services/BatteryService/src/BatteryService.Api"

if [ ! -d "$API_DIR" ]; then
  echo "❌ Không tìm thấy $API_DIR" >&2
  exit 1
fi

echo "🌱 Seeding BatteryService (BAT-2026-001..004 + threshold + site)..."
echo "   API_DIR=$API_DIR"

# SEED_AND_EXIT=1 báo Program.cs migrate + seed xong thì exit (không bind port).
# Nếu Program.cs chưa hỗ trợ env này → mặc định API sẽ start; script kill sau 20s.
cd "$API_DIR"

if SEED_AND_EXIT=1 timeout 60 dotnet run --no-launch-profile --no-build 2>&1 | tee /tmp/seed-iot-mvp.log; then
  echo "✅ Seed hoàn tất."
else
  STATUS=$?
  # exit 124 từ timeout → coi như success (API đã start xong sau seed)
  if [ "$STATUS" -eq 124 ]; then
    echo "✅ Seed hoàn tất (API timeout sau seed — bình thường)."
  else
    echo "❌ Seed thất bại (exit $STATUS). Xem /tmp/seed-iot-mvp.log" >&2
    exit "$STATUS"
  fi
fi

echo ""
echo "📋 Verify bằng psql:"
echo "   psql \$BATTERY_DB_URL -c \"SELECT serial_number FROM battery_assets WHERE serial_number LIKE 'BAT-2026-%' ORDER BY serial_number;\""
