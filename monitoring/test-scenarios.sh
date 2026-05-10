#!/usr/bin/env bash
# =====================================================================
# End-to-end test scenarios — induce activity, verify stack reflects.
# =====================================================================

set -u
G='\033[0;32m'; B='\033[0;34m'; N='\033[0m'
section() { echo -e "\n${B}═══ $1 ═══${N}"; }

# ─────────────────────────────────────────────────────────────────────
section "Scenario 1: HTTP metrics — bắn 100 request rồi query Prometheus"
# ─────────────────────────────────────────────────────────────────────
echo "Sending 100 requests to apigateway → authservice..."
for i in {1..100}; do
  curl -s -o /dev/null http://localhost:4001/auth-service/swagger/index.html
done
sleep 18    # đợi Prometheus scrape (15s interval) + ngấm

echo ""
echo "→ Prometheus query: rate(http_requests_received_total[1m]) by service"
curl -s -G "http://localhost:9094/api/v1/query" \
  --data-urlencode 'query=sum(rate(http_requests_received_total[1m])) by (service)' \
  | python3 -c "
import sys, json
d = json.load(sys.stdin)
for r in d.get('data', {}).get('result', []):
    print(f'  {r[\"metric\"].get(\"service\",\"?\")}: {float(r[\"value\"][1]):.2f} req/sec')"

# ─────────────────────────────────────────────────────────────────────
section "Scenario 2: AppMetrics — outbox pattern khi register user"
# ─────────────────────────────────────────────────────────────────────
EMAIL="test_$(date +%s)@example.com"
echo "Registering user $EMAIL..."
curl -s -X POST http://localhost:4001/api/auth/register \
  -H "Content-Type: application/json" \
  -d "{\"email\":\"$EMAIL\",\"password\":\"Pass123!@#\",\"fullName\":\"Test User\"}" \
  -o /dev/null -w "HTTP %{http_code}\n"

sleep 18
echo ""
echo "→ Prometheus: outbox_messages_processed_total (tính tổng)"
curl -s -G "http://localhost:9094/api/v1/query" \
  --data-urlencode 'query=sum(outbox_messages_processed_total)' \
  | python3 -c "
import sys, json
r = json.load(sys.stdin).get('data', {}).get('result', [])
print(f'  Total outbox messages published: {r[0][\"value\"][1] if r else 0}')"

echo ""
echo "→ Prometheus: outbox_messages_processed_total by event_type"
curl -s -G "http://localhost:9094/api/v1/query" \
  --data-urlencode 'query=outbox_messages_processed_total' \
  | python3 -c "
import sys, json
for r in json.load(sys.stdin).get('data', {}).get('result', []):
    print(f'  {r[\"metric\"].get(\"event_type\",\"?\")}: {r[\"value\"][1]}')"

# ─────────────────────────────────────────────────────────────────────
section "Scenario 3: Idempotency-Key — replay với cùng key"
# ─────────────────────────────────────────────────────────────────────
KEY=$(uuidgen)
EMAIL2="idem_$(date +%s)@example.com"
echo "Sending 3x register với cùng Idempotency-Key=$KEY"
for i in 1 2 3; do
  status=$(curl -s -X POST http://localhost:4001/api/auth/register \
    -H "Content-Type: application/json" \
    -H "Idempotency-Key: $KEY" \
    -d "{\"email\":\"$EMAIL2\",\"password\":\"Pass123!@#\",\"fullName\":\"Idem User\"}" \
    -o /dev/null -w "%{http_code}")
  echo "  Request $i: HTTP $status"
done
sleep 18

echo ""
echo "→ Prometheus: idempotency_key_replay_hits_total + reservations_total"
curl -s -G "http://localhost:9094/api/v1/query" \
  --data-urlencode 'query=idempotency_key_replay_hits_total' \
  | python3 -c "
import sys, json
r = json.load(sys.stdin).get('data', {}).get('result', [])
print(f'  Replay hits: {r[0][\"value\"][1] if r else 0}')"
curl -s -G "http://localhost:9094/api/v1/query" \
  --data-urlencode 'query=idempotency_key_reservations_total' \
  | python3 -c "
import sys, json
r = json.load(sys.stdin).get('data', {}).get('result', [])
print(f'  Reservations:  {r[0][\"value\"][1] if r else 0}')"

# ─────────────────────────────────────────────────────────────────────
section "Scenario 4: Loki — query OutboxRelay logs (verify cross-service trace)"
# ─────────────────────────────────────────────────────────────────────
end_ns=$(($(date +%s) * 1000000000))
start_ns=$((($(date +%s) - 300) * 1000000000))

# Test 1: OutboxRelay activity từ AuthService (background job logs DB query)
out=$(curl -s -G "http://localhost:3100/loki/api/v1/query_range" \
  --data-urlencode 'query={container="solar-authservice"} |~ "outbox_messages"' \
  --data-urlencode "start=$start_ns" --data-urlencode "end=$end_ns" --data-urlencode "limit=200" \
  | python3 -c "
import sys, json
d = json.load(sys.stdin)
total = sum(len(s.get('values', [])) for s in d.get('data', {}).get('result', []))
print(total)" 2>/dev/null)
echo "  AuthService log lines mentioning 'outbox_messages' (last 5min): $out"

# Test 2: HTTP request logs từ ApiGateway (request logging middleware)
gw=$(curl -s -G "http://localhost:3100/loki/api/v1/query_range" \
  --data-urlencode 'query={container="solar-apigateway", level="Information"}' \
  --data-urlencode "start=$start_ns" --data-urlencode "end=$end_ns" --data-urlencode "limit=200" \
  | python3 -c "
import sys, json
d = json.load(sys.stdin)
total = sum(len(s.get('values', [])) for s in d.get('data', {}).get('result', []))
print(total)" 2>/dev/null)
echo "  ApiGateway Information logs (last 5min): $gw"

# Test 3: cross-service log volume aggregation
echo ""
echo "  Tổng log volume cross-service (5 phút):"
curl -s -G "http://localhost:3100/loki/api/v1/query" \
  --data-urlencode 'query=sum by (container) (count_over_time({container=~"solar-(authservice|emailservice|smsservice|apigateway|filestorageservice)"}[5m]))' \
  | python3 -c "
import sys, json
for r in json.load(sys.stdin).get('data', {}).get('result', []):
    print(f'    {r[\"metric\"][\"container\"]:35s} {int(float(r[\"value\"][1])):>6} lines')"

# ─────────────────────────────────────────────────────────────────────
section "Scenario 5: Alert simulator — stop service, đợi alert fire"
# ─────────────────────────────────────────────────────────────────────
echo "Stopping smsservice → alert ServiceDown sẽ fire sau ~1m..."
docker stop solar-smsservice >/dev/null
echo "  Đợi 75s cho alert chuyển trạng thái từ pending → firing..."
sleep 75

echo ""
echo "→ Active alerts:"
curl -s "http://localhost:9094/api/v1/alerts" \
  | python3 -c "
import sys, json
alerts = json.load(sys.stdin).get('data', {}).get('alerts', [])
for a in alerts:
    print(f'  [{a[\"state\"]}] {a[\"labels\"][\"alertname\"]} — {a[\"labels\"].get(\"service\",\"?\")} (since {a[\"activeAt\"]})')"

echo ""
echo "Restarting smsservice..."
docker start solar-smsservice >/dev/null
echo "  Sau ~1 phút alert sẽ tự resolve."

echo -e "\n${G}Done. Mở http://localhost:3001 → Dashboards để xem visualizations.${N}"
