#!/usr/bin/env bash
# GH-730 / GH-731 / GH-732 — cổng chặn dependency có lỗ hổng.
#
# Vì sao cần: cả ba issue trên đều là "package cũ dính CVE", và cả ba đều nằm im rất lâu vì
# KHÔNG có gì tự động soi. `dotnet restore` có in NU1902/NU1903 nhưng chỉ là warning — build
# vẫn xanh nên không ai thấy. Script này biến nó thành lỗi.
#
# Mặc định chặn severity High + Critical (Moderate chỉ cảnh báo) — đủ nghiêm để không bị lờ,
# đủ rộng để không chặn CI vì một Moderate chưa có bản vá.
#   AUDIT_FAIL_ON=moderate   → chặn cả Moderate
#   SKIP_AUDIT=1             → bỏ qua (dùng khi offline, KHÔNG dùng trong CI)

set -uo pipefail

if [ "${SKIP_AUDIT:-0}" = "1" ]; then
  echo "SKIP_AUDIT=1 → bỏ qua quét dependency."
  exit 0
fi

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$REPO_ROOT"

FAIL_ON="${AUDIT_FAIL_ON:-high}"
case "$FAIL_ON" in
  moderate) PATTERN='Moderate|High|Critical' ;;
  high)     PATTERN='High|Critical' ;;
  critical) PATTERN='Critical' ;;
  *) echo "AUDIT_FAIL_ON không hợp lệ: $FAIL_ON (moderate|high|critical)"; exit 64 ;;
esac

SLN="${SLN:-SolarBatteryMaintainance.slnx}"
OUT="$(mktemp)"
trap 'rm -f "$OUT"' EXIT

echo "Quét dependency có lỗ hổng (kể cả transitive), chặn từ mức: $FAIL_ON"

# --include-transitive là bắt buộc: cả 3 issue nói trên đều là package TRANSITIVE
# (Npgsql qua EF provider, System.IO.Packaging qua ClosedXML, MessagePack qua SignalR.Redis).
if ! dotnet list "$SLN" package --vulnerable --include-transitive > "$OUT" 2>&1; then
  echo "FAIL: 'dotnet list package --vulnerable' chạy lỗi:"
  cat "$OUT"
  exit 1
fi

# ── Miễn trừ có lý do ────────────────────────────────────────────────────────
# CHỈ thêm vào đây khi advisory KHÔNG có bản vá và rủi ro thực tế bằng 0.
# Mỗi dòng phải ghi: vì sao không vá được + vì sao chấp nhận được.
#
#   SQLitePCLRaw.lib.e_sqlite3 (GHSA-2m69-gcr7-jv3q, High)
#     - Không vá được: advisory ghi "affected <= 2.1.11, patched: None" — nhà cung cấp CHƯA
#       phát hành bản vá nào. Nâng version không giải quyết được.
#     - Rủi ro bằng 0 ở đây: chỉ đến từ Microsoft.EntityFrameworkCore.Sqlite trong HAI project
#       TEST (BatteryService.UnitTests, TicketService.IntegrationTests). Không service nào
#       chạy SQLite ở runtime — production dùng PostgreSQL.
#     - Gỡ miễn trừ này ngay khi upstream có bản vá.
EXEMPT_PACKAGES='SQLitePCLRaw\.lib\.e_sqlite3|SQLitePCLRaw\.lib\.e_sqlite3\.android'

# Dòng advisory có dạng:  > PackageName   1.2.3   High   https://github.com/advisories/...
HITS="$(grep -E '^\s+> ' "$OUT" | grep -E "\s($PATTERN)\s" | grep -vE "^\s+> ($EXEMPT_PACKAGES)\s" || true)"

EXEMPTED="$(grep -E '^\s+> ' "$OUT" | grep -E "\s($PATTERN)\s" | grep -E "^\s+> ($EXEMPT_PACKAGES)\s" || true)"
if [ -n "$EXEMPTED" ]; then
  echo
  echo "MIỄN TRỪ (không có bản vá upstream, chỉ dùng trong test — xem chú thích trong script):"
  echo "$EXEMPTED" | sed 's/^/   /' | sort -u
fi

if [ -n "$HITS" ]; then
  echo
  echo "FAIL: có dependency dính lỗ hổng mức $FAIL_ON trở lên:"
  echo "$HITS" | sed 's/^/   /' | sort -u
  echo
  echo "Cách xử lý:"
  echo "  1. Truy nguồn: package trực tiếp nào kéo nó vào (đọc obj/project.assets.json)."
  echo "  2. Ưu tiên nâng package TRỰC TIẾP tới bản kéo theo dependency đã vá."
  echo "  3. Nếu package trực tiếp ghim cứng version cũ, thêm PackageReference trực tiếp tới"
  echo "     bản đã vá (NuGet lấy version cao nhất) — kèm comment giải thích, đừng để người sau xoá."
  exit 1
fi

MODERATE="$(grep -E '^\s+> ' "$OUT" | grep -E '\sModerate\s' || true)"
if [ -n "$MODERATE" ]; then
  echo
  echo "CẢNH BÁO: còn advisory mức Moderate (không chặn CI ở mức '$FAIL_ON'):"
  echo "$MODERATE" | sed 's/^/   /' | sort -u
fi

echo "OK: không có dependency dính lỗ hổng mức $FAIL_ON trở lên."
