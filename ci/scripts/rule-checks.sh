#!/usr/bin/env bash
# Project rule checks — mirror Jenkinsfile stage 5.
# Chặn các anti-pattern BE:
#   1. await UpdateAsync/DeleteAsync (void method)
#   2. await GetAllAsync (sync, trả IQueryable)
#   3. Entity mới trong Domain/Entities/ phải extend AuditableEntity
#   4. Sprint 5B #233 ADR-017 — Energy/CO2 scope creep guard
#
# Note — additional async/await rules enforced ở STAGE 3 (dotnet build), KHÔNG cần check ở đây:
#   AUTH-81 (2026-06-19): Microsoft.VisualStudio.Threading.Analyzers via
#   services/AuthService/Directory.Build.props + .editorconfig severity=error:
#     - VSTHRD002 (.Result, .Wait(), .GetAwaiter().GetResult())
#     - VSTHRD100 (async void)
#     - VSTHRD110 (unobserved task)
#     - CA2200 (throw ex; phải dùng throw;)
#   → Build sẽ fail nếu code mới vi phạm, KHÔNG cần grep diff ở stage này.
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

# ---------------------------------------------------------------------
# Sprint audit #AUDIT-04 — audit convention bans. Đây là GATE hard-fail của CI,
# mirror build-time Roslyn analyzer 'Microsoft.CodeAnalysis.BannedApiAnalyzers'
# (root Directory.Build.props + eng/audit/BannedSymbols.txt, opt-in IDE/local qua
# -p:EnableAuditBannedApis=true). Diff-based: chỉ chặn code MỚI (dòng '+'), KHÔNG
# fail code cũ. Production only — loại trừ tests/Migrations; rule Console loại trừ
# Program.cs (startup bootstrap trước khi ILogger sẵn sàng).
# ---------------------------------------------------------------------

# Danh sách file .cs production thay đổi (loại file bị xóa).
CHANGED_CS="$(git diff "${BASE_REF}...HEAD" --name-only --diff-filter=d -- '*.cs' 2>/dev/null \
            || git diff 'HEAD~1...HEAD' --name-only --diff-filter=d -- '*.cs' 2>/dev/null \
            || true)"

# Added lines ('+', bỏ header '+++') của 1 file giữa BASE_REF..HEAD.
added_lines() {
  git diff "${BASE_REF}...HEAD" -- "$1" 2>/dev/null \
    || git diff 'HEAD~1...HEAD' -- "$1" 2>/dev/null \
    || true
}

DT_HITS=""; CONSOLE_HITS=""; EVENTID_HITS=""
for f in $CHANGED_CS; do
  # Production only.
  case "$f" in
    */tests/*|*Tests.cs|*Test.cs|*/Migrations/*) continue;;
  esac
  ADDED="$(added_lines "$f" | grep -E '^\+' | grep -vE '^\+\+\+' || true)"
  [ -z "$ADDED" ] && continue

  # Rule 5 / AUDIT001 — DateTime.Now / DateTime.Today / DateTimeOffset.Now.
  hit="$(echo "$ADDED" | grep -E '\b(DateTime|DateTimeOffset)\.(Now|Today)\b' || true)"
  [ -n "$hit" ] && DT_HITS="${DT_HITS}${f}:
${hit}
"

  # Rule 7 / AUDIT002 — Guid.NewGuid() gán cho eventId.
  hit="$(echo "$ADDED" | grep -iE 'eventId[[:space:]]*=[[:space:]]*Guid\.NewGuid[[:space:]]*\(' || true)"
  [ -n "$hit" ] && EVENTID_HITS="${EVENTID_HITS}${f}:
${hit}
"

  # Rule 6 / AUDIT003 — Console.Write* (trừ Program.cs).
  case "$f" in *Program.cs) ;; *)
    hit="$(echo "$ADDED" | grep -E '\bConsole\.(Write|WriteLine|Error|Out)\b' || true)"
    [ -n "$hit" ] && CONSOLE_HITS="${CONSOLE_HITS}${f}:
${hit}
" ;;
  esac
done

if [ -n "$DT_HITS" ]; then
  echo "FAIL: AUDIT001 — dùng UtcNow thay cho DateTime.Now/Today/DateTimeOffset.Now (audit timestamp phải UTC):"
  printf '%s' "$DT_HITS"
  FAILED=1
else
  echo "PASS: AUDIT001 no new DateTime.Now/Today"
fi

if [ -n "$EVENTID_HITS" ]; then
  echo "FAIL: AUDIT002 — dùng AuditEventId.New() thay cho Guid.NewGuid() khi tạo event_id:"
  printf '%s' "$EVENTID_HITS"
  FAILED=1
else
  echo "PASS: AUDIT002 no new Guid.NewGuid() for event_id"
fi

if [ -n "$CONSOLE_HITS" ]; then
  echo "FAIL: AUDIT003 — dùng ILogger thay cho Console.Write* trong production code (trừ Program.cs):"
  printf '%s' "$CONSOLE_HITS"
  FAILED=1
else
  echo "PASS: AUDIT003 no new Console.Write* (outside Program.cs)"
fi

exit "$FAILED"
