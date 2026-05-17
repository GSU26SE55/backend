#!/usr/bin/env bash
# Migration rollback test cho BatteryService trên TimescaleDB.
# Verify cycle: apply -> rollback từng bước -> re-apply -> rollback toàn bộ -> re-apply.
# Đặc biệt check hypertable metadata sạch sau rollback toàn bộ.

set -euo pipefail

PG_CONTAINER="${PG_CONTAINER:-solar-postgres}"
PG_USER="${PG_USER:-postgres}"
PG_PASS="${PG_PASS:-Password12345@}"
PG_HOST_PORT="${PG_HOST_PORT:-5433}"
TEST_DB="${TEST_DB:-battery_db_rollback_test}"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
INFRA_PROJ="$SCRIPT_DIR/../src/BatteryService.Infrastructure"
API_PROJ="$SCRIPT_DIR/../src/BatteryService.Api"

CONN="Host=localhost;Port=${PG_HOST_PORT};Database=${TEST_DB};Username=${PG_USER};Password=${PG_PASS}"
export ConnectionStrings__BatteryDb="$CONN"

INIT_MIGRATION="InitBatteryDb"
MID_MIGRATION="AddSiteAndBatteryGroup"
LAST_MIGRATION="AddCustomerAccountSyncAndSensorHypertable"

log()  { printf "\n\033[1;34m== %s ==\033[0m\n" "$*"; }
fail() { printf "\n\033[1;31mFAIL: %s\033[0m\n" "$*" >&2; exit 1; }
psql_test() { docker exec "$PG_CONTAINER" psql -U "$PG_USER" -d "$TEST_DB" -At -c "$1"; }
psql_admin() { docker exec "$PG_CONTAINER" psql -U "$PG_USER" -d postgres -c "$1" >/dev/null; }
ef_update() { dotnet ef database update "$@" -p "$INFRA_PROJ" -s "$API_PROJ" --no-build >/dev/null; }

log "Reset DB $TEST_DB"
psql_admin "DROP DATABASE IF EXISTS $TEST_DB;"
psql_admin "CREATE DATABASE $TEST_DB;"

log "Build Infrastructure once"
dotnet build "$INFRA_PROJ" -v quiet >/dev/null
dotnet build "$API_PROJ" -v quiet >/dev/null

log "Step 1: Apply ALL migrations"
ef_update

# Verify hypertable created
HT_COUNT=$(psql_test "SELECT COUNT(*) FROM timescaledb_information.hypertables WHERE hypertable_name = 'sensor_readings';")
[ "$HT_COUNT" = "1" ] || fail "Expected sensor_readings hypertable, got count=$HT_COUNT"

# Verify customer_accounts exists
CA_EXISTS=$(psql_test "SELECT COUNT(*) FROM information_schema.tables WHERE table_name = 'customer_accounts';")
[ "$CA_EXISTS" = "1" ] || fail "Expected customer_accounts table, got count=$CA_EXISTS"
log "OK: hypertable + customer_accounts exist after apply-all"

log "Step 2: Rollback last migration ($LAST_MIGRATION -> $MID_MIGRATION)"
ef_update "$MID_MIGRATION"

CA_EXISTS=$(psql_test "SELECT COUNT(*) FROM information_schema.tables WHERE table_name = 'customer_accounts';")
[ "$CA_EXISTS" = "0" ] || fail "customer_accounts should be dropped after rollback, got count=$CA_EXISTS"

# Hypertable: Down() của migration cuối KHÔNG un-convert, nên vẫn còn — chấp nhận được
HT_COUNT=$(psql_test "SELECT COUNT(*) FROM timescaledb_information.hypertables WHERE hypertable_name = 'sensor_readings';")
[ "$HT_COUNT" = "1" ] || fail "sensor_readings hypertable should persist after partial rollback, got count=$HT_COUNT"
log "OK: customer_accounts dropped, hypertable preserved"

log "Step 3: Re-apply last migration"
ef_update

CA_EXISTS=$(psql_test "SELECT COUNT(*) FROM information_schema.tables WHERE table_name = 'customer_accounts';")
[ "$CA_EXISTS" = "1" ] || fail "customer_accounts should be re-created, got count=$CA_EXISTS"
HT_COUNT=$(psql_test "SELECT COUNT(*) FROM timescaledb_information.hypertables WHERE hypertable_name = 'sensor_readings';")
[ "$HT_COUNT" = "1" ] || fail "hypertable should still exist after re-apply, got count=$HT_COUNT"
log "OK: re-apply preserves both"

log "Step 4: Rollback to AddSiteAndBatteryGroup again, then to InitBatteryDb"
ef_update "$MID_MIGRATION"
ef_update "$INIT_MIGRATION"

SITES_EXISTS=$(psql_test "SELECT COUNT(*) FROM information_schema.tables WHERE table_name IN ('sites', 'battery_groups');")
[ "$SITES_EXISTS" = "0" ] || fail "sites/battery_groups should be dropped, got count=$SITES_EXISTS"
log "OK: sites + battery_groups dropped"

log "Step 5: Full rollback to zero (drops sensor_readings + hypertable cleanup)"
ef_update 0

TABLE_COUNT=$(psql_test "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = 'public' AND table_name IN ('battery_types','battery_assets','sensor_readings','threshold_configs','alerts');")
[ "$TABLE_COUNT" = "0" ] || fail "All BatteryService tables should be dropped, got count=$TABLE_COUNT"

HT_COUNT=$(psql_test "SELECT COUNT(*) FROM timescaledb_information.hypertables WHERE hypertable_name = 'sensor_readings';")
[ "$HT_COUNT" = "0" ] || fail "Hypertable metadata should be auto-cleaned by TimescaleDB after DROP TABLE, got count=$HT_COUNT"
log "OK: full rollback clean, hypertable metadata auto-cleaned by TimescaleDB"

log "Step 6: Final re-apply of all migrations"
ef_update

HT_COUNT=$(psql_test "SELECT COUNT(*) FROM timescaledb_information.hypertables WHERE hypertable_name = 'sensor_readings';")
[ "$HT_COUNT" = "1" ] || fail "Hypertable should be recreated on final apply, got count=$HT_COUNT"

CA_EXISTS=$(psql_test "SELECT COUNT(*) FROM information_schema.tables WHERE table_name = 'customer_accounts';")
[ "$CA_EXISTS" = "1" ] || fail "customer_accounts should be recreated, got count=$CA_EXISTS"

log "Step 7: Cleanup test DB"
psql_admin "DROP DATABASE IF EXISTS $TEST_DB;"

printf "\n\033[1;32mPASS: migration rollback cycle works correctly on TimescaleDB\033[0m\n"
