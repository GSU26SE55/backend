#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# Kiểm chứng tầng hạ tầng của notification sau Sprint 6.2 + 6.3:
#   · 7 background worker có thật sự khởi động không (đọc log container)
#   · Migration đã áp đủ, bảng mới tồn tại, seed template phủ đủ type
#   · Metric Prometheus mới có được expose không
#   · Cấu hình timing NOTI3-05 ↔ NOTI3-02 có hợp lệ không
#
# Đây là phần curl KHÔNG kiểm được: worker chạy nền, không có endpoint nào gọi tới.
# ─────────────────────────────────────────────────────────────────────────────
set -uo pipefail

EV="${EV:?Cần đặt biến EV = thư mục evidence}"
NOTI_C="${NOTI_C:-solar-notificationservice}"
PG_C="${PG_C:-solar-postgres}"

PASS=0; FAIL=0
SUM="$EV/_infra-summary.md"
: > "$SUM"

ok()  { PASS=$((PASS+1)); printf '  ✅ %s\n' "$1"; printf '| ✅ PASS | %s | %s |\n' "$1" "${2:-}" >> "$SUM"; }
bad() { FAIL=$((FAIL+1)); printf '  ❌ %s — %s\n' "$1" "${2:-}"; printf '| ❌ FAIL | %s | %s |\n' "$1" "${2:-}" >> "$SUM"; }

{
  echo "# Kiểm chứng hạ tầng notification"
  echo ""
  echo "- Thời điểm: $(date '+%Y-%m-%d %H:%M:%S %Z')"
  echo ""
  echo "| KQ | Hạng mục | Bằng chứng |"
  echo "|----|----------|------------|"
} >> "$SUM"

# ═════════════════════════════════════════════════════════════════════════
echo ""
echo "── A. 7 background worker có khởi động không ─────────────────────────"
LOG="$EV/04-workers/notificationservice.log"
mkdir -p "$EV/04-workers"
docker logs "$NOTI_C" --since 30m > "$LOG" 2>&1

# Log của service là JSON structured, tiếng Việt bị escape thành \uXXXX
# ("bật" → "b\u1EADt"). grep trên chuỗi thô KHÔNG khớp được — phải parse JSON rồi
# đọc trường Message đã giải mã. Script này trích dòng khởi động của từng worker
# theo Category (tên class, thuần ASCII) và in ra tham số thật đang chạy.
python3 - "$LOG" > "$EV/04-workers/worker-startup.txt" 2>&1 <<'PYEOF'
import json, sys

WATCH = {
    "NotificationDispatchBackgroundService":       "Dispatch (NOTI-01)",
    "ExpoReceiptReconcileBackgroundService":       "ExpoReceipt (NOTI3-02)",
    "NotificationFallbackBackgroundService":       "Fallback (NOTI3-05)",
    "NotificationRetentionBackgroundService":      "Retention (NOTI3-11)",
    "NotificationDigestBackgroundService":         "Digest (NOTI-12)",
    "NotificationDlqMonitorBackgroundService":     "DlqMonitor (NOTI3-08)",
    "NotificationAuditOutboxRelayBackgroundService":"AuditOutboxRelay (#AUDIT-34)",
}

found = {}
for line in open(sys.argv[1], encoding="utf-8", errors="replace"):
    line = line.strip()
    if not line.startswith("{"):
        continue
    try:
        rec = json.loads(line)
    except Exception:
        continue
    cat = (rec.get("Category") or "").split(".")[-1]
    if cat in WATCH and cat not in found:
        msg = (rec.get("Message") or "").replace("\n", " ")
        # Chỉ lấy dòng mô tả cấu hình lúc khởi động, bỏ log nghiệp vụ thường kỳ
        if any(k in msg for k in ("bật", "started", "TẮT", "Started", "bị tắt")):
            found[cat] = msg

for cls, label in WATCH.items():
    if cls in found:
        print(f"OK|{label}|{found[cls][:160]}")
    else:
        print(f"MISSING|{label}|không thấy dòng khởi động của {cls}")
PYEOF

while IFS='|' read -r st label detail; do
  [ "$st" = "OK" ] && ok "Worker $label khởi động" "$detail" || bad "Worker $label khởi động" "$detail"
done < "$EV/04-workers/worker-startup.txt"

# NOTI3-05 ↔ NOTI3-02: cấu hình sai sẽ in LogError. Không có dòng đó = cấu hình đúng.
if grep -q "CẤU HÌNH SAI" "$LOG"; then
  bad "Ràng buộc timing Fallback ↔ ExpoReceipt" "$(grep -m1 -oE 'CẤU HÌNH SAI.{0,150}' "$LOG")"
else
  ok "Ràng buộc timing Fallback ↔ ExpoReceipt hợp lệ" "không có cảnh báo CẤU HÌNH SAI ⇒ timeout ≥ ngưỡng an toàn"
fi

# Lỗi nghiêm trọng lúc khởi động
if grep -qE "Unhandled exception|CRITICAL|FATAL" "$LOG"; then
  bad "Log khởi động sạch" "$(grep -m1 -oE '(Unhandled exception|CRITICAL|FATAL).{0,120}' "$LOG")"
else
  ok "Log khởi động không có exception/CRITICAL" ""
fi

# ═════════════════════════════════════════════════════════════════════════
echo ""
echo "── B. Schema + dữ liệu (migration Sprint 6.2/6.3) ────────────────────"
mkdir -p "$EV/05-db"

q() { docker exec "$PG_C" psql -U postgres -d notification_db -t -A -c "$1" 2>&1 | tr -d '\r'; }

TABLES=$(q "SELECT table_name FROM information_schema.tables WHERE table_schema='public' ORDER BY 1;")
echo "$TABLES" > "$EV/05-db/tables.txt"

for t in push_receipts notification_category_preferences notifications notification_templates device_tokens notification_preferences; do
  if grep -qx "$t" "$EV/05-db/tables.txt"; then ok "Bảng \`$t\` tồn tại" ""; else bad "Bảng \`$t\`" "không thấy sau migration"; fi
done

# NOTI3-02 — cột mới của Notification
COLS=$(q "SELECT column_name FROM information_schema.columns WHERE table_name='notifications' ORDER BY 1;")
echo "$COLS" > "$EV/05-db/notifications-columns.txt"
for c in dispatch_attempt_count next_attempt_at; do
  grep -qx "$c" "$EV/05-db/notifications-columns.txt" \
    && ok "Cột \`notifications.$c\` (Sprint 6.2 NOTI-01)" "" \
    || bad "Cột \`notifications.$c\`" "thiếu"
done

# NOTI3-12 — cột version + partial unique index
q "SELECT column_name FROM information_schema.columns WHERE table_name='notification_templates' AND column_name='version';" | grep -q version \
  && ok "Cột \`notification_templates.version\` (NOTI3-12b)" "" || bad "cột version" "thiếu"

IDX=$(q "SELECT indexname FROM pg_indexes WHERE tablename='notification_templates' ORDER BY 1;")
echo "$IDX" > "$EV/05-db/template-indexes.txt"
grep -q "ux_notification_templates_active_per_key" "$EV/05-db/template-indexes.txt" \
  && ok "Partial unique index 1-bản-active-mỗi-bộ-ba" "$(grep ux_ "$EV/05-db/template-indexes.txt" | tr '\n' ' ')" \
  || bad "partial unique index" "$(cat "$EV/05-db/template-indexes.txt" | tr '\n' ' ')"

# NOTI3-12a — seed đủ 32 type
SEED=$(q "SELECT COUNT(DISTINCT type) FROM notification_templates WHERE is_deleted=false;")
TOTAL=$(q "SELECT COUNT(*) FROM notification_templates WHERE is_deleted=false;")
LOCALES=$(q "SELECT DISTINCT locale FROM notification_templates WHERE is_deleted=false ORDER BY 1;" | tr '\n' ' ')
{ echo "type khác nhau: $SEED"; echo "tổng bản ghi: $TOTAL"; echo "locale: $LOCALES"; } > "$EV/05-db/template-seed.txt"
[ "${SEED:-0}" -ge 30 ] 2>/dev/null \
  && ok "Seed template phủ $SEED type (trước sprint: 5)" "tổng $TOTAL bản ghi · locale: $LOCALES" \
  || bad "Seed template" "chỉ $SEED type"

echo "$LOCALES" | grep -q "en-US" \
  && ok "Có locale \`en-US\` (NOTI3-12e)" "$LOCALES" || bad "locale en-US" "chỉ có: $LOCALES"

# NOTI3-12e — cột preferred_locale ở read-model account
q "SELECT column_name FROM information_schema.columns WHERE table_name='account_read_models' AND column_name='preferred_locale';" | grep -q preferred_locale \
  && ok "Cột \`account_read_models.preferred_locale\`" "dispatcher chọn ngôn ngữ theo người nhận" \
  || bad "cột preferred_locale" "thiếu"

# Migration đã áp hết chưa
q "SELECT \"MigrationId\" FROM \"__EFMigrationsHistory\" ORDER BY 1;" > "$EV/05-db/migrations-applied.txt"
for m in AddNotificationDispatchRetryColumns AddPushReceiptAndDeliveryStatus AddNotificationCategoryPreference AddTemplateVersioningAndAccountLocale; do
  grep -q "$m" "$EV/05-db/migrations-applied.txt" \
    && ok "Migration \`$m\` đã áp" "" || bad "Migration \`$m\`" "chưa áp"
done

# EmailService KHÔNG được có DB (đã gỡ NOTI3-03)
if docker exec "$PG_C" psql -U postgres -lqt 2>/dev/null | cut -d'|' -f1 | grep -qw "email_db"; then
  bad "EmailService không còn database" "email_db VẪN TỒN TẠI — đáng lẽ đã gỡ cùng NOTI3-03"
else
  ok "Không tồn tại \`email_db\`" "EmailService trở lại stateless đúng như quyết định huỷ NOTI3-03"
fi

# ═════════════════════════════════════════════════════════════════════════
echo ""
echo "── C. Metric Prometheus (NOTI3-07) ───────────────────────────────────"
mkdir -p "$EV/06-metrics"
curl -s "http://localhost:4008/metrics" -o "$EV/06-metrics/notification-metrics.txt" 2>/dev/null

if [ -s "$EV/06-metrics/notification-metrics.txt" ]; then
  ok "Endpoint /metrics trả dữ liệu" "$(wc -l < "$EV/06-metrics/notification-metrics.txt" | tr -d ' ') dòng"
  for m in notification_sent_total notification_failed_total notification_pending_total \
           notification_dlq_size notification_rate_limited_total notification_fallback_total \
           expo_push_receipt_total expo_push_token_deactivated_total \
           notification_delivery_latency_seconds notification_retry_total notification_deferred_total; do
    grep -q "^# HELP $m" "$EV/06-metrics/notification-metrics.txt" \
      && ok "Metric \`$m\`" "" || bad "Metric \`$m\`" "không thấy trong /metrics"
  done
  # 3 metric của NOTI3-03 phải BIẾN MẤT
  for m in email_deliverability_event_total email_suppressed_total email_suppression_list_size; do
    grep -q "$m" "$EV/06-metrics/notification-metrics.txt" \
      && bad "Metric \`$m\` đã gỡ" "VẪN CÒN — chưa gỡ sạch NOTI3-03" \
      || ok "Metric \`$m\` đã gỡ khỏi /metrics" ""
  done
else
  bad "Endpoint /metrics" "không lấy được dữ liệu từ localhost:4008/metrics"
fi

# ═════════════════════════════════════════════════════════════════════════
{
  echo ""
  echo "**Tổng hạ tầng: $PASS PASS · $FAIL FAIL**"
} >> "$SUM"
echo ""
echo "═══════════════════════════════════════════════"
echo "  HẠ TẦNG: $PASS PASS · $FAIL FAIL"
echo "═══════════════════════════════════════════════"
[ "$FAIL" -eq 0 ]
