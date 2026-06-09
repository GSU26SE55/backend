#!/usr/bin/env bash
# Project rule checks — mirror Jenkinsfile stage 5.
# Chặn các anti-pattern BE:
#   1. await UpdateAsync/DeleteAsync (void method)
#   2. await GetAllAsync (sync, trả IQueryable)
#   3. Entity mới trong Domain/Entities/ phải extend AuditableEntity
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

# Rule 2: await GetAllAsync
if echo "$DIFF" | grep -E '^\+.*await\s+\w+(\.\w+)*\.GetAllAsync\s*\(' >/dev/null; then
  echo "FAIL: GetAllAsync trả IQueryable (SYNC) — không được await."
  echo "$DIFF" | grep -nE '^\+.*await\s+\w+(\.\w+)*\.GetAllAsync\s*\(' || true
  FAILED=1
else
  echo "PASS: no await on GetAllAsync"
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

exit "$FAILED"
