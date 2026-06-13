#!/usr/bin/env bash
# Project rule checks — mirror Jenkinsfile stage 5.
# Chặn các anti-pattern BE:
#   1. await UpdateAsync/DeleteAsync (void method)
#   2. await GetAllAsync (sync, trả IQueryable)
#   3. Entity mới trong Domain/Entities/ phải extend AuditableEntity
#   4. Sprint 5B #233 ADR-017 — Energy/CO2 scope creep guard
#
# Env:
#   BASE_REF  → ref so sánh (default: origin/dev)
#
# Cách dùng:
#   ./ci/scripts/rule-checks.sh                  # diff vs origin/dev
#   BASE_REF=origin/main ./ci/scripts/rule-checks.sh

set -euo pipefail

BASE_REF="${BASE_REF:-origin/dev}"

# Fetch best-effort — local có thể offline, Jenkins luôn có remote
git fetch origin "${BASE_REF#origin/}" 2>/dev/null || true

# Lấy diff: ưu tiên so với BASE_REF; fallback HEAD~1 (lúc detached / shallow)
DIFF="$(git diff "${BASE_REF}...HEAD" -- '*.cs' 2>/dev/null \
     || git diff 'HEAD~1...HEAD' -- '*.cs' 2>/dev/null \
     || echo "")"

FAILED=0

# Rule 1: await UpdateAsync / DeleteAsync
if echo "$DIFF" | grep -E '^\+.*await\s+\w+(\.\w+)*\.(UpdateAsync|DeleteAsync)\s*\(' >/dev/null; then
  echo "FAIL: UpdateAsync/DeleteAsync là VOID — không được await."
  echo "$DIFF" | grep -nE '^\+.*await\s+\w+(\.\w+)*\.(UpdateAsync|DeleteAsync)\s*\(' || true
  FAILED=1
else
  echo "PASS: no await on void UpdateAsync/DeleteAsync"
fi

# Rule 2: await GetAllAsync trực tiếp (không qua chain LINQ async terminator).
#   - SAI:  var x = await uow.Y.GetAllAsync();              ← await IQueryable trực tiếp
#   - SAI:  return await uow.Y.GetAllAsync();
#   - ĐÚNG: var x = await uow.Y.GetAllAsync().FirstOrDefaultAsync(...);
#   - ĐÚNG: var x = await uow.Y.GetAllAsync()
#                .Where(...)
#                .ToListAsync();
# Regex chỉ flag pattern statement-end ngay sau `GetAllAsync()` (`;` hoặc `)` hết arg list).
# Chain `.FirstOrDefaultAsync` / `.ToListAsync` / `.AnyAsync` được pass vì await thực sự awaits Task<T>.
if echo "$DIFF" | grep -E '^\+.*await\s+\w+(\.\w+)*\.GetAllAsync\s*\(\s*\)\s*(;|\)\s*[,;])' >/dev/null; then
  echo "FAIL: GetAllAsync trả IQueryable (SYNC) — không được await trực tiếp."
  echo "$DIFF" | grep -nE '^\+.*await\s+\w+(\.\w+)*\.GetAllAsync\s*\(\s*\)\s*(;|\)\s*[,;])' || true
  FAILED=1
else
  echo "PASS: no await on GetAllAsync (standalone)"
fi

# Rule 3: entity mới phải extend AuditableEntity
NEW_ENTITIES="$(git diff "${BASE_REF}...HEAD" --name-only --diff-filter=A 2>/dev/null \
              | grep -E 'Domain/Entities/.*\.cs$' || true)"

ENTITY_FAILED=0
for file in $NEW_ENTITIES; do
  [ -f "$file" ] || continue
  # Bỏ qua abstract / enum / interface — chỉ check class cụ thể
  if grep -qE '^(\s*public\s+)?(abstract|enum|interface)' "$file"; then
    continue
  fi
  # Bỏ qua hypertable / append-only entity (TimescaleDB) — không có Id/UpdatedAt/IsDeleted
  # vì partition theo time + retention auto-drop chunks. Pattern: file có comment "hypertable"
  # hoặc "append-only" hoặc "không AuditableEntity".
  if grep -qiE 'hypertable|append-only|không AuditableEntity' "$file"; then
    continue
  fi
  if ! grep -qE 'class\s+\w+\s*:\s*(\w+\s*,\s*)*AuditableEntity' "$file"; then
    echo "FAIL: $file phải extend AuditableEntity"
    ENTITY_FAILED=1
  fi
done

if [ "$ENTITY_FAILED" -eq 0 ]; then
  echo "PASS: new domain entities extend AuditableEntity"
else
  FAILED=1
fi

# Rule 4: Sprint 5B #233 ADR-017 — Energy/CO2 scope creep guard.
# Mirror pre-commit hook `energy-co2-scope-guard` trong .pre-commit-config.yaml.
# Block tokens: EnergySession, EnergyDailySummary, BatteryCycleLog, SiteEnergySummary,
#               ElectricityRate, CarbonEmissionFactor, CapacityKw, kWh, CO2*.
SCOPE_HITS="$(grep -rInE 'EnergySession|EnergyDailySummary|BatteryCycleLog|SiteEnergySummary|ElectricityRate|CarbonEmissionFactor|CapacityKw|kWh|CO2' \
              services/BatteryService/src shared/src 2>/dev/null \
              | grep -vE '/(bin|obj|Migrations)/' || true)"

if [ -n "$SCOPE_HITS" ]; then
  echo "FAIL: Energy/CO2 scope creep detected (ADR-017). Vi phạm:"
  echo "$SCOPE_HITS"
  echo
  echo "Fix: xóa tokens trên, hoặc cập nhật ADR-017 (docs/adr/0017-remove-energy-co2-analytics.md) nếu thay đổi scope."
  FAILED=1
else
  echo "PASS: no Energy/CO2 scope creep (ADR-017)"
fi

exit "$FAILED"
