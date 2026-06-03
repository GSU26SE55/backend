#!/usr/bin/env bash
# Chạy tests, đo coverage, fail nếu line coverage < THRESHOLD%.
set -euo pipefail

THRESHOLD="${THRESHOLD:-80}"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SERVICE_DIR="$SCRIPT_DIR/.."
REPORTGEN="${REPORTGEN:-$HOME/.dotnet/tools/reportgenerator}"

cd "$SERVICE_DIR"
rm -rf TestResults
dotnet test --collect:"XPlat Code Coverage" --settings coverage.runsettings --results-directory ./TestResults

"$REPORTGEN" \
    -reports:"TestResults/**/coverage.cobertura.xml" \
    -targetdir:"TestResults/Report" \
    -reporttypes:"TextSummary;Html" >/dev/null

LINE_COV=$(grep -E "^\s*Line coverage:" TestResults/Report/Summary.txt | awk '{print $3}' | tr -d '%' | tr ',' '.')
echo "Line coverage: ${LINE_COV}% (threshold: ${THRESHOLD}%)"

if awk "BEGIN { exit !(${LINE_COV} >= ${THRESHOLD}) }"; then
    echo "PASS: coverage ${LINE_COV}% >= ${THRESHOLD}%"
else
    echo "FAIL: coverage ${LINE_COV}% < ${THRESHOLD}%"
    exit 1
fi
