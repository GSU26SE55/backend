#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# Publish một integration event thẳng vào RabbitMQ theo đúng khuôn MassTransit.
#
# Dùng để test đường production thật (event → consumer → notification) mà không
# phải dựng dữ liệu nghiệp vụ đầy đủ ở service phát sinh.
#
#   publish-event.sh <TênEvent> '<json message>'
#
# Ví dụ:
#   publish-event.sh TicketCreatedEvent '{"ticketId":"...","code":"TK-1","customerId":"...","priority":"P1"}'
#
# MassTransit nhận diện kiểu message qua trường `messageType` (urn:message:...),
# KHÔNG qua tên exchange — nên envelope phải đúng, không chỉ routing đúng.
# ─────────────────────────────────────────────────────────────────────────────
set -euo pipefail

EVENT="${1:?Cần tên event, vd TicketCreatedEvent}"
BODY="${2:?Cần JSON message}"
NS="${NS:-SharedContracts.Events}"
# ⚠️ Máy dev chạy HAI RabbitMQ:
#     iot-rabbitmq   → 5672  / mgmt 15672  (dự án KHÁC)
#     solar-rabbitmq → 5673  / mgmt 15673  (dự án NÀY)
# Mặc định phải là 15673. Trỏ nhầm 15672 thì publish trả "không routed" mà không có lỗi rõ ràng.
MQ="${MQ:-http://localhost:15673}"
USER="${MQ_USER:-guest}"
PASS="${MQ_PASS:-guest}"

EXCHANGE="${NS}:${EVENT}"

ENVELOPE=$(python3 - "$EXCHANGE" "$NS" "$EVENT" "$BODY" <<'PY'
import json, sys, uuid, datetime
exchange, ns, event, body = sys.argv[1], sys.argv[2], sys.argv[3], sys.argv[4]
env = {
    "messageId":      str(uuid.uuid4()),
    "conversationId": str(uuid.uuid4()),
    "sourceAddress":      f"rabbitmq://rabbitmq/e2e-test",
    "destinationAddress": f"rabbitmq://rabbitmq/{exchange}",
    "messageType": [f"urn:message:{ns}:{event}"],
    "message": json.loads(body),
    "sentTime": datetime.datetime.now(datetime.timezone.utc).isoformat(),
    "headers": {},
}
print(json.dumps(env))
PY
)

PAYLOAD=$(python3 - "$EXCHANGE" "$ENVELOPE" <<'PY'
import json, sys
exchange, envelope = sys.argv[1], sys.argv[2]
print(json.dumps({
    "vhost": "/",
    "name": exchange,
    "properties": {
        # MassTransit chỉ deserialize khi content-type đúng khuôn của nó.
        "content_type": "application/vnd.masstransit+json",
        "delivery_mode": 2,
    },
    "routing_key": "",
    "payload": envelope,
    "payload_encoding": "string",
}))
PY
)

RESP=$(curl -su "$USER:$PASS" -H 'Content-Type: application/json' \
  -X POST "$MQ/api/exchanges/%2F/$(python3 -c "import urllib.parse,sys; print(urllib.parse.quote(sys.argv[1], safe=''))" "$EXCHANGE")/publish" \
  -d "$PAYLOAD" 2>/dev/null)

echo "$RESP" | python3 -c "
import json,sys
try:
    d=json.load(sys.stdin)
except Exception:
    print('  ⚠️  Phản hồi không phải JSON'); sys.exit(1)
if d.get('routed'):
    print('  ✅ Đã publish $EVENT — routed tới consumer')
else:
    print('  ⚠️  Publish nhưng KHÔNG routed (không consumer nào bind exchange $EXCHANGE)')
    sys.exit(1)
"
