#!/usr/bin/env bash
# Project rule checks — mirror Jenkinsfile stage 5.
# Chặn các anti-pattern BE:
#   1. await UpdateAsync/DeleteAsync (void method)
#   2. await GetAllAsync (sync, trả IQueryable)
#   3. Entity mới trong Domain/Entities/ phải extend AuditableEntity
#   4. Sprint 5B #233 ADR-017 — Energy/CO2 scope creep guard
#   5. #CHAT-04 — hardcode regex bắt @username trong TicketService handler (phải dùng IMentionParser)
#   6. #CHAT-04 — inline render HTML string trong TicketService handler (phải dùng IMarkdownRenderer)
#   7. #CHAT-04 — truy cập trực tiếp _dbContext.<property> trong TicketService handler (phải qua _unitOfWork)
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

# Diff riêng cho TicketService handler — scope cho rule 5/6/7 (#CHAT-04).
HANDLER_PATH='services/TicketService/src/TicketService.Application/CQRS/Handler'
HANDLER_DIFF="$(git diff "${BASE_REF}...HEAD" -- "${HANDLER_PATH}/**/*.cs" 2>/dev/null \
     || git diff 'HEAD~1...HEAD' -- "${HANDLER_PATH}/**/*.cs" 2>/dev/null \
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

# Rule 5: #CHAT-04 — cấm hardcode regex bắt @username trong TicketService handler.
# Phải dùng IMentionParser (Phase sau) thay vì tự viết Regex match "@...".
if echo "$HANDLER_DIFF" | grep -E '^\+.*(Regex\.(IsMatch|Match|Matches)|new\s+Regex\s*\()' | grep -F '@' >/dev/null; then
  echo "FAIL: Hardcode regex bắt @username trong handler — phải dùng IMentionParser."
  echo "$HANDLER_DIFF" | grep -nE '^\+.*(Regex\.(IsMatch|Match|Matches)|new\s+Regex\s*\()' | grep -F '@' || true
  FAILED=1
else
  echo "PASS: no hardcoded @username regex in TicketService handler"
fi

# Rule 6: #CHAT-04 — cấm inline render HTML string trong TicketService handler.
# Phải dùng IMarkdownRenderer (Phase sau) thay vì nhúng string HTML trực tiếp.
if echo "$HANDLER_DIFF" | grep -E '^\+.*"[^"]*<[a-zA-Z][^>]*>' >/dev/null; then
  echo "FAIL: Inline render HTML string trong handler — phải dùng IMarkdownRenderer."
  echo "$HANDLER_DIFF" | grep -nE '^\+.*"[^"]*<[a-zA-Z][^>]*>' || true
  FAILED=1
else
  echo "PASS: no inline HTML render in TicketService handler"
fi

# Rule 7: #CHAT-04 — cấm truy cập trực tiếp _dbContext.<property bất kỳ> trong TicketService handler.
# Phải qua _unitOfWork (IUnitOfWork) — generic, không hardcode tên entity TicketComments/TicketChats.
if echo "$HANDLER_DIFF" | grep -E '^\+.*_dbContext\.\w+' >/dev/null; then
  echo "FAIL: Truy cập trực tiếp _dbContext trong handler — phải qua _unitOfWork."
  echo "$HANDLER_DIFF" | grep -nE '^\+.*_dbContext\.\w+' || true
  FAILED=1
else
  echo "PASS: no direct _dbContext access in TicketService handler"
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

# ---------------------------------------------------------------------------
# Rule 8: hai bản alert rules phải trùng khít.
#
# docker-compose mount `monitoring/prometheus/alert-rules.yml`; Helm chỉ đọc được file nằm
# TRONG thư mục chart nên phải giữ bản sao `deploy/helm/solar-battery/files/alert-rules.yml`.
# Hai bản này từng trôi rất xa nhau — Helm chỉ có 26/40 alert, thiếu trọn 3 nhóm auth-security,
# chat_hub_alerts, notification-delivery. Cảnh báo chạy ở docker nhưng không tồn tại trên K8s,
# và không có gì báo cho ai biết.
#
# Đây là luật chạy trên TOÀN CÂY (không theo diff): lệch là đỏ, bất kể ai gây ra.
# ---------------------------------------------------------------------------
ALERT_SRC="monitoring/prometheus/alert-rules.yml"
ALERT_HELM="deploy/helm/solar-battery/files/alert-rules.yml"

if [ ! -f "$ALERT_SRC" ] || [ ! -f "$ALERT_HELM" ]; then
  echo "FAIL: thiếu file alert rules ($ALERT_SRC hoặc $ALERT_HELM)."
  FAILED=1
elif diff -q "$ALERT_SRC" "$ALERT_HELM" >/dev/null 2>&1; then
  echo "PASS: alert rules đồng bộ giữa compose và Helm ($(grep -c '^[[:space:]]*- alert:' "$ALERT_SRC") alert)"
else
  echo "FAIL: $ALERT_SRC và $ALERT_HELM đã lệch nhau — alert sẽ chạy ở docker mà không có trên K8s:"
  diff "$ALERT_SRC" "$ALERT_HELM" || true
  echo "Fix: make sync-alert-rules"
  FAILED=1
fi

# ---------------------------------------------------------------------------
# Rule 9: template Helm đặt tên `_*` mà không phải partial.
#
# Helm bỏ qua MỌI file trong templates/ bắt đầu bằng dấu gạch dưới — nó coi đó là partial
# (chỗ chứa `define`). File manifest lỡ đặt tên như vậy sẽ không bao giờ render, không lỗi,
# không cảnh báo. Repo này đã dính hai lần:
#   · _servicemonitor.yaml → K8s không scrape bất kỳ service ứng dụng nào
#   · _autoscaling.yaml    → HPA/PDB chưa từng được tạo
# Cả hai đều không chứa `define` — dấu hiệu nhận biết chắc chắn.
# ---------------------------------------------------------------------------
HELM_TPL_DIR="deploy/helm/solar-battery/templates"
UNDERSCORE_HITS=""
if [ -d "$HELM_TPL_DIR" ]; then
  while IFS= read -r f; do
    case "$f" in *.tpl) continue;; esac          # *.tpl là partial theo quy ước, bỏ qua
    if ! grep -qE '\{\{-?[[:space:]]*define' "$f"; then
      UNDERSCORE_HITS="${UNDERSCORE_HITS}  $f
"
    fi
  done < <(find "$HELM_TPL_DIR" -type f -name '_*' 2>/dev/null)
fi

if [ -n "$UNDERSCORE_HITS" ]; then
  echo "FAIL: template Helm tên '_*' nhưng không có block define — Helm sẽ BỎ QUA, không render:"
  printf '%s' "$UNDERSCORE_HITS"
  echo "Fix: đổi tên bỏ dấu gạch dưới, hoặc chuyển thành partial thật (.tpl + define)."
  FAILED=1
else
  echo "PASS: không có template Helm bị bỏ qua vì đặt tên '_*'"
fi

# ---------------------------------------------------------------------------
# RULE 9 — Tên class consumer PHẢI duy nhất trên toàn repo (GH-1073)
#
# MassTransit đặt tên queue từ TÊN CLASS consumer (bỏ hậu tố "Consumer"), KHÔNG kèm
# namespace, vì repo không cấu hình EndpointNameFormatter riêng. Hai service đặt trùng
# tên class ⇒ CHUNG một queue trong RabbitMQ ⇒ mô hình competing-consumer: mỗi message
# chỉ tới ĐÚNG MỘT service, luân phiên.
#
# Đây là lỗi mất dữ liệu và HOÀN TOÀN IM LẶNG — không exception, không log, không test
# nào bắt. Repo đã dính 6 nhóm cùng lúc (đo trên RabbitMQ đang chạy ngày 05/08/2026):
#   · AuditReplayRequestedConsumer   ← 6 service, mà thiết kế là fanout "mọi service đều
#     nhận rồi tự lọc ServiceName" ⇒ ~83% lệnh replay rơi vào service sai và bị bỏ qua
#   · AccountActivatedConsumer       ← BatteryService + NotificationService
#   · BatteryAnomalyDetectedConsumer · BatteryCascadeRiskHighConsumer
#   · EnvironmentalIncidentDetectedConsumer · EnvironmentalIncidentResolvedConsumer
#
# Lệ đặt tên của repo (đã có sẵn từ trước, chỉ là bị bỏ sót):
#   · TicketService  → tiền tố service:  TicketAccountActivatedConsumer
#   · Khi cùng service có 2 consumer cho 1 event → hậu tố mô tả việc:
#     AccountActivatedSyncConsumer (sync read-model) vs AccountActivatedWelcomeConsumer
#
# Quét TOÀN BỘ repo chứ không chỉ diff: một class đổi tên ở service A có thể đụng class
# đã tồn tại từ lâu ở service B mà diff không hề chạm tới.
# ---------------------------------------------------------------------------
DUP_CONSUMERS="$(
  grep -rhoE 'class[[:space:]]+[A-Za-z0-9_]+[[:space:]]*:[[:space:]]*(I?Consumer<|[A-Za-z0-9_]*ConsumerBase<)' \
       --include='*.cs' services/ 2>/dev/null \
    | sed -E 's/^class[[:space:]]+([A-Za-z0-9_]+).*/\1/' \
    | grep -vE 'ConsumerBase$' \
    | sort | uniq -d
)"

if [ -n "$DUP_CONSUMERS" ]; then
  echo "FAIL: tên class consumer bị TRÙNG giữa các service ⇒ chung queue RabbitMQ, mất message:"
  for c in $DUP_CONSUMERS; do
    echo "  $c"
    grep -rl "class[[:space:]]\+${c}[[:space:]]*:" --include='*.cs' services/ 2>/dev/null | sed 's/^/      /'
  done
  echo "Fix: đổi tên MỘT bên theo lệ sẵn có — tiền tố service (TicketXxxConsumer) hoặc"
  echo "     hậu tố mô tả việc (XxxSyncConsumer / XxxWelcomeConsumer)."
  echo "Lưu ý: đổi tên class = đổi tên queue. Queue cũ còn lại trên broker phải dọn tay khi deploy."
  FAILED=1
else
  echo "PASS: không có tên class consumer trùng giữa các service"
fi

# ---------------------------------------------------------------------------
# Rule 10: production WireGuard gate must work under the unprivileged Jenkins
# deploy account. `wg show` requires CAP_NET_ADMIN and previously caused a
# healthy tunnel to be reported as missing. End-to-end HTTPS and gRPC probes to
# the peer /32 are the fail-closed proof and must remain bounded by timeouts.
# ---------------------------------------------------------------------------
AI_PREFLIGHT="deploy/scripts/preflight-production.sh"
AI_PREFLIGHT_FAILED=0
# These are intentionally literal shell fragments from the preflight source.
# shellcheck disable=SC2016
AI_HTTPS_RESOLVE_CONTRACT='--resolve "${ai_host}:443:${AI_WIREGUARD_IPV4}"'
# shellcheck disable=SC2016
AI_GRPC_TARGET_CONTRACT='"${AI_WIREGUARD_IPV4}:443"'

if grep -Fq 'latest-handshakes' "${AI_PREFLIGHT}"; then
  echo "FAIL: ${AI_PREFLIGHT} must not query privileged WireGuard handshake state."
  AI_PREFLIGHT_FAILED=1
fi
if ! grep -Fq -- "${AI_HTTPS_RESOLVE_CONTRACT}" "${AI_PREFLIGHT}"; then
  echo "FAIL: ${AI_PREFLIGHT} must force AI HTTPS through the WireGuard peer while preserving TLS SNI."
  AI_PREFLIGHT_FAILED=1
fi
if ! grep -Fq -- '--connect-timeout 5 --max-time 10' "${AI_PREFLIGHT}"; then
  echo "FAIL: ${AI_PREFLIGHT} WireGuard HTTPS probe must have explicit timeouts."
  AI_PREFLIGHT_FAILED=1
fi
if [ "$(grep -Fc "${AI_GRPC_TARGET_CONTRACT}" "${AI_PREFLIGHT}")" -lt 2 ]; then
  echo "FAIL: ${AI_PREFLIGHT} must retain both standard and application gRPC probes over WireGuard."
  AI_PREFLIGHT_FAILED=1
fi

if [ "${AI_PREFLIGHT_FAILED}" -eq 0 ]; then
  echo "PASS: AI WireGuard preflight is unprivileged, bounded and end-to-end"
else
  FAILED=1
fi

# ---------------------------------------------------------------------------
# Rule 11: Prometheus discovery must inspect spec.selector and ignore the
# operator-managed headless service. The client service selects pods with
# app.kubernetes.io/name=prometheus but does not carry that metadata label, so
# a kubectl `-l` prefilter silently removes it before jq can inspect it.
# ---------------------------------------------------------------------------
PROMETHEUS_DEPLOY="deploy/scripts/deploy-production.sh"
PROMETHEUS_SELECTOR="deploy/scripts/select-prometheus-service.jq"
PROMETHEUS_SELECTOR_FIXTURE='{
  "items": [
    {
      "metadata": {
        "name": "monitoring-prometheus",
        "labels": {"app": "kube-prometheus-stack-prometheus"}
      },
      "spec": {
        "type": "ClusterIP",
        "clusterIP": "10.43.52.54",
        "ports": [{"port": 9090}, {"port": 8080}],
        "selector": {
          "app.kubernetes.io/name": "prometheus",
          "operator.prometheus.io/name": "monitoring-prometheus"
        }
      }
    },
    {
      "metadata": {"name": "prometheus-operated"},
      "spec": {
        "type": "ClusterIP",
        "clusterIP": "None",
        "ports": [{"port": 9090}],
        "selector": {"app.kubernetes.io/name": "prometheus"}
      }
    },
    {
      "metadata": {
        "name": "unrelated-metrics",
        "labels": {"app.kubernetes.io/name": "unrelated"}
      },
      "spec": {
        "type": "ClusterIP",
        "clusterIP": "10.43.52.99",
        "ports": [{"port": 9090}],
        "selector": {"app.kubernetes.io/name": "unrelated"}
      }
    }
  ]
}'

PROMETHEUS_SELECTED="$(printf '%s' "${PROMETHEUS_SELECTOR_FIXTURE}" |
  jq -er -f "${PROMETHEUS_SELECTOR}" 2>/dev/null || true)"
PROMETHEUS_MISSING_REJECTED=0
PROMETHEUS_AMBIGUOUS_REJECTED=0

if ! printf '%s' "${PROMETHEUS_SELECTOR_FIXTURE}" |
  jq '{items: [.items[] | select(.metadata.name == "prometheus-operated")]}' |
  jq -er -f "${PROMETHEUS_SELECTOR}" >/dev/null 2>&1; then
  PROMETHEUS_MISSING_REJECTED=1
fi
if ! printf '%s' "${PROMETHEUS_SELECTOR_FIXTURE}" |
  jq '(.items[] | select(.metadata.name == "prometheus-operated") | .spec.clusterIP) = "10.43.52.55"' |
  jq -er -f "${PROMETHEUS_SELECTOR}" >/dev/null 2>&1; then
  PROMETHEUS_AMBIGUOUS_REJECTED=1
fi

if [ ! -f "${PROMETHEUS_SELECTOR}" ]; then
  echo "FAIL: missing Prometheus service selector: ${PROMETHEUS_SELECTOR}"
  FAILED=1
elif grep -Fq -- '-l app.kubernetes.io/name=prometheus' "${PROMETHEUS_DEPLOY}"; then
  echo "FAIL: ${PROMETHEUS_DEPLOY} must not prefilter services by a pod-selector-only metadata label"
  FAILED=1
elif [ "${PROMETHEUS_SELECTED}" = 'monitoring-prometheus' ] &&
  [ "${PROMETHEUS_MISSING_REJECTED}" -eq 1 ] &&
  [ "${PROMETHEUS_AMBIGUOUS_REJECTED}" -eq 1 ]; then
  echo "PASS: Prometheus discovery uses spec.selector and rejects headless, unrelated and ambiguous services"
else
  echo "FAIL: Prometheus discovery does not enforce the production service topology"
  FAILED=1
fi

# ---------------------------------------------------------------------------
# Rule 12: GH-1244 TicketService scheduling configuration must be wired from
# the host contract through preflight/deployment into the rendered Helm
# ConfigMap. Defaults in values-production.yaml alone are insufficient because
# operators edit /opt/solar-platform/config/host.env between releases.
# ---------------------------------------------------------------------------
HOST_ENV_EXAMPLE="deploy/production/host.env.example"
PRODUCTION_PREFLIGHT="deploy/scripts/preflight-production.sh"
PRODUCTION_DEPLOY="deploy/scripts/deploy-production.sh"
PERIODIC_HOST_KEYS="
TICKET_PERIODIC_MAINTENANCE_ENABLED
TICKET_PERIODIC_MAINTENANCE_TIME_ZONE_ID
TICKET_PERIODIC_MAINTENANCE_CYCLE_MONTHS
TICKET_PERIODIC_MAINTENANCE_LEAD_DAYS
TICKET_PERIODIC_MAINTENANCE_OVERDUE_WINDOW_DAYS
TICKET_PERIODIC_MAINTENANCE_REMINDER_TIME
TICKET_PERIODIC_MAINTENANCE_POLL_INTERVAL_SECONDS
TICKET_PERIODIC_MAINTENANCE_BATCH_SIZE
SLA_BUSINESS_HOURS_TIME_ZONE_ID
SLA_BUSINESS_HOURS_START
SLA_BUSINESS_HOURS_END
SLA_BUSINESS_HOURS_WORKING_DAYS_0
SLA_BUSINESS_HOURS_WORKING_DAYS_1
SLA_BUSINESS_HOURS_WORKING_DAYS_2
SLA_BUSINESS_HOURS_WORKING_DAYS_3
SLA_BUSINESS_HOURS_WORKING_DAYS_4
SLA_BUSINESS_HOURS_WORKING_DAYS_5
SLA_BUSINESS_HOURS_WORKING_DAYS_6
"
PERIODIC_CONFIG_KEYS="
Ticket__PeriodicMaintenance__Enabled
Ticket__PeriodicMaintenance__TimeZoneId
Ticket__PeriodicMaintenance__CycleMonths
Ticket__PeriodicMaintenance__LeadDays
Ticket__PeriodicMaintenance__OverdueScheduleWindowDays
Ticket__PeriodicMaintenance__ReminderTime
Ticket__PeriodicMaintenance__PollIntervalSeconds
Ticket__PeriodicMaintenance__BatchSize
SlaBusinessHours__TimeZoneId
SlaBusinessHours__Start
SlaBusinessHours__End
SlaBusinessHours__WorkingDays__0
SlaBusinessHours__WorkingDays__1
SlaBusinessHours__WorkingDays__2
SlaBusinessHours__WorkingDays__3
SlaBusinessHours__WorkingDays__4
SlaBusinessHours__WorkingDays__5
SlaBusinessHours__WorkingDays__6
"
PERIODIC_CONFIG_FAILED=0

for host_key in ${PERIODIC_HOST_KEYS}; do
  if ! grep -Eq "^${host_key}=" "${HOST_ENV_EXAMPLE}"; then
    echo "FAIL: ${HOST_ENV_EXAMPLE} is missing ${host_key}"
    PERIODIC_CONFIG_FAILED=1
  fi
  if ! grep -Fq "read_env ${host_key}" "${PRODUCTION_PREFLIGHT}"; then
    echo "FAIL: ${PRODUCTION_PREFLIGHT} does not read ${host_key}"
    PERIODIC_CONFIG_FAILED=1
  fi
  if ! grep -Fq "read_env ${host_key} \"\${host_env}\"" "${PRODUCTION_DEPLOY}"; then
    echo "FAIL: ${PRODUCTION_DEPLOY} does not read ${host_key} from host.env"
    PERIODIC_CONFIG_FAILED=1
  fi
done

for config_key in ${PERIODIC_CONFIG_KEYS}; do
  if ! grep -Fq "config.${config_key}=" "${PRODUCTION_DEPLOY}"; then
    echo "FAIL: ${PRODUCTION_DEPLOY} does not pass config.${config_key} to Helm"
    PERIODIC_CONFIG_FAILED=1
  fi
done

if [ "${PERIODIC_CONFIG_FAILED}" -eq 0 ]; then
  echo "PASS: GH-1244 host.env, preflight and Helm deployment configuration are wired end-to-end"
else
  FAILED=1
fi

# ---------------------------------------------------------------------------
# Rule 13: A clean R4 needs one explicit bootstrap release to create Loki
# before R3 Alloy can push to it. Bootstrap must be impossible once the Helm
# release exists, and the capacity overlay must win over production defaults.
# ---------------------------------------------------------------------------
BOOTSTRAP_CONTRACT_FAILED=0

if ! grep -Fxq 'PLATFORM_DEPLOYMENT_PHASE=bootstrap' "${HOST_ENV_EXAMPLE}"; then
  echo "FAIL: ${HOST_ENV_EXAMPLE} must declare the one-time bootstrap phase"
  BOOTSTRAP_CONTRACT_FAILED=1
fi
for required_contract in \
  'PLATFORM_DEPLOYMENT_PHASE must be bootstrap or steady' \
  'bootstrap phase is forbidden because Helm release already exists' \
  'steady phase requires an existing Helm release'
do
  if ! grep -Fq "${required_contract}" "${PRODUCTION_PREFLIGHT}"; then
    echo "FAIL: ${PRODUCTION_PREFLIGHT} is missing bootstrap guard: ${required_contract}"
    BOOTSTRAP_CONTRACT_FAILED=1
  fi
done
for required_contract in \
  'monitoring.ai.enabled=false' \
  'monitoring.ai.enabled=true' \
  'wait_for_loki_bridge || exit 1' \
  'verify_ai_observability_targets || exit 1'
do
  if ! grep -Fq "${required_contract}" "${PRODUCTION_DEPLOY}"; then
    echo "FAIL: ${PRODUCTION_DEPLOY} is missing phase behavior: ${required_contract}"
    BOOTSTRAP_CONTRACT_FAILED=1
  fi
done

if ! grep -Fq 'block_retention: {{ .Values.tempo.retention | quote }}' \
  deploy/helm/solar-battery/templates/monitoring/tempo.yaml; then
  echo 'FAIL: Tempo retention value is not wired into the rendered compactor configuration'
  BOOTSTRAP_CONTRACT_FAILED=1
fi

for helm_contract_file in \
  Jenkinsfile \
  deploy/jenkins/production.Jenkinsfile.example \
  "${PRODUCTION_DEPLOY}"
do
  production_values_line="$(
    grep -n -m1 'values-production[.]yaml' "${helm_contract_file}" |
      cut -d: -f1
  )"
  small_values_line="$(
    grep -n -m1 'values-vps-small[.]yaml' "${helm_contract_file}" |
      cut -d: -f1
  )"
  if [ -z "${production_values_line}" ] || [ -z "${small_values_line}" ] ||
    [ "${production_values_line}" -ge "${small_values_line}" ]; then
    echo "FAIL: ${helm_contract_file} must apply values-vps-small.yaml after values-production.yaml"
    BOOTSTRAP_CONTRACT_FAILED=1
  fi
done

if [ "${BOOTSTRAP_CONTRACT_FAILED}" -eq 0 ]; then
  echo "PASS: clean-R4 bootstrap is one-time, steady is fail-closed and the capacity overlay wins"
else
  FAILED=1
fi

# ---------------------------------------------------------------------------
# Rule 14: application images are private. Host-side Cosign verification must
# authenticate with the existing Kubernetes pull Secret without persisting the
# PAT in the deploy user's home directory.
# ---------------------------------------------------------------------------
REGISTRY_VERIFY_FAILED=0

# These are intentionally literal shell fragments from the deploy source.
# shellcheck disable=SC2016
for required_contract in \
  'registry_config="$(mktemp -d /tmp/solar-registry-auth.XXXXXX)"' \
  "-o jsonpath='{.data.\\.dockerconfigjson}'" \
  '.auths["ghcr.io"] | type == "object"' \
  'DOCKER_CONFIG="${registry_config}"' \
  'cleanup_registry_config'
do
  if ! grep -Fq -- "${required_contract}" "${PRODUCTION_DEPLOY}"; then
    echo "FAIL: ${PRODUCTION_DEPLOY} is missing ephemeral GHCR verification contract: ${required_contract}"
    REGISTRY_VERIFY_FAILED=1
  fi
done

if grep -Fq '/home/deploy/.docker/config.json' "${PRODUCTION_DEPLOY}"; then
  echo "FAIL: ${PRODUCTION_DEPLOY} must not persist the GHCR credential in deploy's home directory"
  REGISTRY_VERIFY_FAILED=1
fi

if [ "${REGISTRY_VERIFY_FAILED}" -eq 0 ]; then
  echo "PASS: R4 Cosign verification uses the ephemeral Kubernetes GHCR pull credential"
else
  FAILED=1
fi

exit "$FAILED"
