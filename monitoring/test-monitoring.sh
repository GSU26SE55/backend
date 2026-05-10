#!/usr/bin/env bash
# =====================================================================
# Test toàn bộ monitoring stack — Solar Battery Maintenance
# =====================================================================
# Cách dùng: ./monitoring/test-monitoring.sh
# Yêu cầu: docker compose stack đang chạy.
# =====================================================================

set -u
PASS=0
FAIL=0
SKIP=0

# Màu sắc
G='\033[0;32m'   # green
R='\033[0;31m'   # red
Y='\033[0;33m'   # yellow
B='\033[0;34m'   # blue
N='\033[0m'      # reset

section() { echo -e "\n${B}═══ $1 ═══${N}"; }

# Test wrapper — in PASS/FAIL theo expected vs actual.
check() {
  local name=$1 expected=$2 actual=$3
  if [[ "$actual" == *"$expected"* ]]; then
    echo -e "  ${G}✓${N} $name"
    PASS=$((PASS+1))
  else
    echo -e "  ${R}✗${N} $name  ${R}expected=${expected}  got=${actual:0:80}${N}"
    FAIL=$((FAIL+1))
  fi
}

# ─────────────────────────────────────────────────────────────────────
section "1. Container status"
# ─────────────────────────────────────────────────────────────────────
for c in solar-prometheus solar-grafana solar-alertmanager solar-loki solar-promtail \
         solar-node-exporter solar-cadvisor solar-postgres-exporter solar-redis-exporter \
         solar-authservice solar-emailservice solar-smsservice solar-filestorageservice solar-apigateway; do
  status=$(docker inspect -f '{{.State.Running}}' "$c" 2>/dev/null || echo "missing")
  check "$c running" "true" "$status"
done

# ─────────────────────────────────────────────────────────────────────
section "2. HTTP endpoints alive"
# ─────────────────────────────────────────────────────────────────────
endpoints=(
  "Prometheus       | http://localhost:9094/-/healthy           | Prometheus Server is Healthy"
  "Grafana          | http://localhost:3001/api/health          | ok"
  "Alertmanager     | http://localhost:9095/-/healthy           | OK"
  "Loki             | http://localhost:3100/ready               | ready"
  "Promtail         | http://localhost:9080/ready               | Ready"
  "cAdvisor         | http://localhost:8081/healthz             | ok"
)
for line in "${endpoints[@]}"; do
  IFS='|' read -r name url expected <<< "$line"
  name=$(echo "$name" | xargs); url=$(echo "$url" | xargs); expected=$(echo "$expected" | xargs)
  body=$(curl -s --max-time 5 "$url" 2>&1 | head -c 400)
  check "$name" "$expected" "$body"
done

# Exporters: full body (>10K), grep cụ thể metric thay vì check 400 chars đầu.
exporters=(
  "node-exporter    | http://localhost:9100/metrics             | node_exporter_build_info"
  "postgres-export  | http://localhost:9187/metrics             | pg_up"
  "redis-exporter   | http://localhost:9121/metrics             | redis_up"
)
for line in "${exporters[@]}"; do
  IFS='|' read -r name url expected <<< "$line"
  name=$(echo "$name" | xargs); url=$(echo "$url" | xargs); expected=$(echo "$expected" | xargs)
  if curl -s --max-time 5 "$url" 2>&1 | grep -q "^$expected"; then
    echo -e "  ${G}✓${N} $name"
    PASS=$((PASS+1))
  else
    echo -e "  ${R}✗${N} $name  metric '$expected' not found"
    FAIL=$((FAIL+1))
  fi
done

# ─────────────────────────────────────────────────────────────────────
section "3. Service /metrics endpoints (in Docker network)"
# ─────────────────────────────────────────────────────────────────────
# Test through host port (qua docker-compose port mapping)
ports=(
  "authservice:4002"
  "emailservice:4003"
  "smsservice:4004"
  "filestorageservice:4005"
  "apigateway:4001"
)
for entry in "${ports[@]}"; do
  IFS=':' read -r name port <<< "$entry"
  body=$(curl -s --max-time 5 "http://localhost:$port/metrics" 2>&1)
  # Check 2 metric: process_start_time_seconds (luôn có) + http_request_duration_seconds (sau khi có request).
  # http_requests_received_total chỉ xuất hiện sau Inc lần đầu — emailservice/smsservice consumer không nhận HTTP.
  if echo "$body" | grep -q "^process_start_time_seconds"; then
    has_http=$(echo "$body" | grep -q "^http_request_duration_seconds_count" && echo "yes" || echo "no")
    echo -e "  ${G}✓${N} $name /metrics endpoint OK (http traffic seen: $has_http)"
    PASS=$((PASS+1))
  else
    echo -e "  ${R}✗${N} $name /metrics endpoint not exposing prometheus-net metrics"
    FAIL=$((FAIL+1))
  fi
done

# ─────────────────────────────────────────────────────────────────────
section "4. Prometheus targets UP"
# ─────────────────────────────────────────────────────────────────────
targets_json=$(curl -s "http://localhost:9094/api/v1/targets?state=active" 2>&1)
for job in prometheus apigateway authservice emailservice smsservice filestorageservice \
           node-exporter cadvisor postgres-exporter redis-exporter rabbitmq minio; do
  health=$(echo "$targets_json" | python3 -c "
import sys, json
data = json.load(sys.stdin)
targets = [t for t in data.get('data', {}).get('activeTargets', []) if t['labels'].get('job') == '$job']
print(targets[0]['health'] if targets else 'missing')
" 2>/dev/null)
  check "Prometheus job=$job" "up" "$health"
done

# ─────────────────────────────────────────────────────────────────────
section "5. Custom application metrics (AppMetrics) flowing"
# ─────────────────────────────────────────────────────────────────────
metrics=(
  "outbox_messages_pending_count"
  "outbox_messages_processed_total"
  "outbox_messages_failures_total"
  "outbox_messages_skipped_total"
  "inbox_messages_processed_total"
  "inbox_messages_skipped_duplicate_total"
  "idempotency_key_replay_hits_total"
  "idempotency_key_conflicts_total"
  "idempotency_key_reservations_total"
)
for m in "${metrics[@]}"; do
  result=$(curl -s "http://localhost:9094/api/v1/query?query=$m" 2>&1 \
    | python3 -c "import sys,json; d=json.load(sys.stdin); print(d.get('status','?'))" 2>/dev/null)
  check "metric $m queryable" "success" "$result"
done

# ─────────────────────────────────────────────────────────────────────
section "6. Prometheus alert rules loaded"
# ─────────────────────────────────────────────────────────────────────
rules=$(curl -s "http://localhost:9094/api/v1/rules" 2>&1)
rule_count=$(echo "$rules" | python3 -c "
import sys, json
d = json.load(sys.stdin)
groups = d.get('data', {}).get('groups', [])
print(sum(len(g.get('rules', [])) for g in groups))
" 2>/dev/null)
check "10 alert rules loaded" "10" "$rule_count"

for rule in ServiceDown HighErrorRate OutboxBacklog OutboxPublishFailures OutboxPoisonMessages \
            HighInboxDuplicateRate IdempotencyConflictSpike HighLatencyP95 \
            ContainerMemoryHigh PostgresConnectionsHigh; do
  found=$(echo "$rules" | python3 -c "
import sys, json
d = json.load(sys.stdin)
for g in d.get('data', {}).get('groups', []):
    for r in g.get('rules', []):
        if r.get('name') == '$rule':
            print('found'); sys.exit()
print('missing')
" 2>/dev/null)
  check "alert rule $rule" "found" "$found"
done

# ─────────────────────────────────────────────────────────────────────
section "7. Grafana datasources connected"
# ─────────────────────────────────────────────────────────────────────
# Lookup uid động (Grafana auto-generate, không phải tên literal)
prom_uid=$(curl -s -u admin:admin "http://localhost:3001/api/datasources/name/Prometheus" 2>&1 \
  | python3 -c "import sys,json; d=json.load(sys.stdin); print(d.get('uid','?'))" 2>/dev/null)
loki_uid=$(curl -s -u admin:admin "http://localhost:3001/api/datasources/name/Loki" 2>&1 \
  | python3 -c "import sys,json; d=json.load(sys.stdin); print(d.get('uid','?'))" 2>/dev/null)

prom_health=$(curl -s -u admin:admin "http://localhost:3001/api/datasources/proxy/uid/$prom_uid/api/v1/query?query=up" 2>&1 \
  | python3 -c "import sys,json; d=json.load(sys.stdin); print(d.get('status','?'))" 2>/dev/null)
check "Grafana → Prometheus (uid=$prom_uid)" "success" "$prom_health"

loki_health=$(curl -s -u admin:admin "http://localhost:3001/api/datasources/proxy/uid/$loki_uid/loki/api/v1/labels" 2>&1 \
  | python3 -c "import sys,json; d=json.load(sys.stdin); print(d.get('status','?'))" 2>/dev/null)
check "Grafana → Loki (uid=$loki_uid)" "success" "$loki_health"

# ─────────────────────────────────────────────────────────────────────
section "8. Grafana dashboards provisioned"
# ─────────────────────────────────────────────────────────────────────
for uid in services-overview messaging-reliability infrastructure logs-overview; do
  status=$(curl -s -u admin:admin "http://localhost:3001/api/dashboards/uid/$uid" 2>&1 \
    | python3 -c "
import sys, json
d = json.load(sys.stdin)
print('found' if d.get('dashboard') else 'missing')
" 2>/dev/null)
  check "dashboard $uid" "found" "$status"
done

# ─────────────────────────────────────────────────────────────────────
section "9. Loki receiving logs from all 5 services"
# ─────────────────────────────────────────────────────────────────────
end_ns=$(($(date +%s) * 1000000000))
start_ns=$(($(date +%s) - 900))   # 15 phút trước
start_ns=$((start_ns * 1000000000))

for svc in apigateway authservice emailservice smsservice filestorageservice; do
  count=$(curl -s -G "http://localhost:3100/loki/api/v1/query" \
    --data-urlencode "query=sum(count_over_time({container=\"solar-${svc}\"}[15m]))" \
    --data-urlencode "time=$end_ns" 2>&1 \
    | python3 -c "
import sys, json
d = json.load(sys.stdin)
r = d.get('data', {}).get('result', [])
print(int(float(r[0]['value'][1])) if r else 0)
" 2>/dev/null)
  if [[ "$count" =~ ^[0-9]+$ ]] && [[ $count -gt 0 ]]; then
    echo -e "  ${G}✓${N} solar-$svc shipped $count log lines"
    PASS=$((PASS+1))
  else
    echo -e "  ${Y}⚠${N}  solar-$svc shipped 0 lines (có thể service idle — chưa hẳn lỗi)"
    SKIP=$((SKIP+1))
  fi
done

# ─────────────────────────────────────────────────────────────────────
section "10. Loki label promotion (level field)"
# ─────────────────────────────────────────────────────────────────────
levels=$(curl -s "http://localhost:3100/loki/api/v1/label/level/values" 2>&1 \
  | python3 -c "import sys,json; d=json.load(sys.stdin); print(','.join(d.get('data',[])))" 2>/dev/null)
check "label 'level' has values" "Information" "$levels"
check "label 'level' has Warning" "Warning" "$levels"

# ─────────────────────────────────────────────────────────────────────
section "11. Alertmanager config loaded"
# ─────────────────────────────────────────────────────────────────────
am=$(curl -s "http://localhost:9095/api/v2/status" 2>&1 \
  | python3 -c "import sys,json; d=json.load(sys.stdin); print(d.get('cluster',{}).get('status','?'))" 2>/dev/null)
check "Alertmanager cluster ready" "ready" "$am"

# ─────────────────────────────────────────────────────────────────────
section "Summary"
# ─────────────────────────────────────────────────────────────────────
echo -e "\n${G}PASS: $PASS${N}    ${R}FAIL: $FAIL${N}    ${Y}SKIP: $SKIP${N}\n"
[[ $FAIL -eq 0 ]] && exit 0 || exit 1
