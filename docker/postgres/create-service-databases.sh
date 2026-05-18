#!/bin/sh
set -eu

POSTGRES_HOST="${POSTGRES_HOST:-postgres}"
POSTGRES_PORT="${POSTGRES_PORT:-5432}"
POSTGRES_DB="${POSTGRES_DB:-postgres}"
POSTGRES_USER="${POSTGRES_USER:-postgres}"

if [ -z "${POSTGRES_PASSWORD:-}" ]; then
  echo "POSTGRES_PASSWORD is required" >&2
  exit 1
fi

export PGPASSWORD="$POSTGRES_PASSWORD"

create_database() {
  db_name="$1"

  if [ -z "$db_name" ]; then
    return 0
  fi

  echo "Ensuring database exists: $db_name"
  psql \
    --host="$POSTGRES_HOST" \
    --port="$POSTGRES_PORT" \
    --username="$POSTGRES_USER" \
    --dbname="$POSTGRES_DB" \
    --set=db="$db_name" \
    --set=owner="$POSTGRES_USER" \
    --set=ON_ERROR_STOP=1 <<'SQL'
SELECT format('CREATE DATABASE %I OWNER %I', :'db', :'owner')
WHERE NOT EXISTS (
    SELECT 1
    FROM pg_database
    WHERE datname = :'db'
)\gexec
SQL
}

create_database "${AUTH_DB_NAME:-auth_db}"
create_database "${FILE_STORAGE_DB_NAME:-file_storage_db}"
create_database "${BATTERY_DB_NAME:-battery_db}"
create_database "${TICKET_DB_NAME:-ticket_db}"

if [ -n "${SERVICE_DATABASES:-}" ]; then
  normalized_service_databases="$(printf '%s' "$SERVICE_DATABASES" | tr ',' ' ')"
  for service_database in $normalized_service_databases; do
    create_database "$service_database"
  done
fi
