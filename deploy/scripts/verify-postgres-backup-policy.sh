#!/usr/bin/env bash
set -Eeuo pipefail

rendered_manifest="${1:?rendered Helm manifest is required}"

[[ -r "${rendered_manifest}" ]] || {
  printf 'rendered Helm manifest is not readable: %s\n' "${rendered_manifest}" >&2
  exit 1
}

backup_cronjob="$(mktemp)"
trap 'rm -f "${backup_cronjob}"' EXIT

awk '
  /^---$/ {
    if (capture) exit
    next
  }

  /^# Source: / {
    capture = index($0, "solar-battery/templates/infra/postgres-backup.yaml") > 0
  }

  capture { print }
' "${rendered_manifest}" > "${backup_cronjob}"

[[ -s "${backup_cronjob}" ]] || {
  printf 'rendered PostgreSQL backup CronJob is missing\n' >&2
  exit 1
}

# These are literal shell expressions that must survive Helm rendering.
# shellcheck disable=SC2016
for expected in \
  'kind: CronJob' \
  'name: postgres-backup' \
  'activeDeadlineSeconds: 1800' \
  'name: PG_DUMP_LOCK_WAIT_TIMEOUT' \
  'value: "60s"' \
  'name: PG_DUMP_TIMEOUT_SECONDS' \
  'value: "600"' \
  'name: PG_DUMP_MAX_ATTEMPTS' \
  'value: "3"' \
  'name: PG_DUMP_RETRY_DELAY_SECONDS' \
  'value: "15"' \
  'timeout -k 15 "${PG_DUMP_TIMEOUT_SECONDS}"' \
  'pg_dump --verbose --format=custom --compress=6' \
  '--lock-wait-timeout="${PG_DUMP_LOCK_WAIT_TIMEOUT}"' \
  'pg_restore --list "${archive}"' \
  'while [ "${attempt}" -le "${PG_DUMP_MAX_ATTEMPTS}" ]'
do
  grep -Fq -- "${expected}" "${backup_cronjob}" || {
    printf 'PostgreSQL backup policy is missing: %s\n' "${expected}" >&2
    exit 1
  }
done

printf 'POSTGRES_BACKUP_POLICY_OK\n'
