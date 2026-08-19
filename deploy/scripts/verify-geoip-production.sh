#!/usr/bin/env bash
set -Eeuo pipefail

manifest="${1:?rendered Helm manifest path is required}"
expected_host_path="${2:-/opt/solar-platform/geoip/GeoLite2-City.mmdb}"

[[ -r "${manifest}" ]] || {
  printf 'rendered Helm manifest is not readable: %s\n' "${manifest}" >&2
  exit 1
}

work_dir="$(mktemp -d)"
audit_manifest="${work_dir}/auditaggregatorservice.yaml"
cleanup() {
  rm -rf -- "${work_dir}"
}
trap cleanup EXIT

# Keep only the documents emitted by the AuditAggregatorService template. This
# prevents an unrelated workload from satisfying one of the required checks.
awk '
  /^---$/ {
    capture = 0
    next
  }

  /^# Source: / {
    capture = index($0, "solar-battery/templates/services/auditaggregatorservice.yaml") > 0
  }

  capture { print }
' "${manifest}" > "${audit_manifest}"

[[ -s "${audit_manifest}" ]] || {
  printf 'rendered manifest does not contain AuditAggregatorService resources\n' >&2
  exit 1
}

require_pattern() {
  local description="$1"
  local pattern="$2"

  grep -Eq -- "${pattern}" "${audit_manifest}" || {
    printf 'AuditAggregatorService GeoIP contract is missing %s\n' "${description}" >&2
    exit 1
  }
}

require_env_value() {
  local key="$1"
  local expected="$2"

  awk -v key="${key}" -v expected="${expected}" '
    {
      line = $0
      sub(/^[[:space:]]+/, "", line)
    }

    line == "- name: " key {
      if (getline <= 0) next
      line = $0
      sub(/^[[:space:]]+/, "", line)
      if (line == "value: \"" expected "\"") found = 1
    }

    END { exit(found ? 0 : 1) }
  ' "${audit_manifest}" || {
    printf 'AuditAggregatorService environment %s does not equal %s\n' \
      "${key}" "${expected}" >&2
    exit 1
  }
}

require_pattern 'the Deployment' '^[[:space:]]*kind:[[:space:]]+Deployment[[:space:]]*$'
require_pattern 'GeoIp__DbPath' '^[[:space:]]*-[[:space:]]+name:[[:space:]]+GeoIp__DbPath[[:space:]]*$'
require_pattern 'GeoIp__Required' '^[[:space:]]*-[[:space:]]+name:[[:space:]]+GeoIp__Required[[:space:]]*$'
require_pattern 'the geoip-database mount' '^[[:space:]]*-[[:space:]]+name:[[:space:]]+geoip-database[[:space:]]*$'
require_pattern 'a read-only mount' '^[[:space:]]*readOnly:[[:space:]]+true[[:space:]]*$'
require_pattern 'hostPath type File' '^[[:space:]]*type:[[:space:]]+File[[:space:]]*$'
require_env_value 'GeoIp__DbPath' '/app/geoip/GeoLite2-City.mmdb'
require_env_value 'GeoIp__Required' 'true'

grep -F "path: \"${expected_host_path}\"" "${audit_manifest}" >/dev/null || {
  printf 'AuditAggregatorService hostPath does not equal %s\n' "${expected_host_path}" >&2
  exit 1
}

mount_count="$(grep -Ec '^[[:space:]]*-[[:space:]]+name:[[:space:]]+geoip-database[[:space:]]*$' "${audit_manifest}")"
[[ "${mount_count}" -eq 2 ]] || {
  printf 'expected exactly one GeoIP volumeMount and one volume; found %s declarations\n' \
    "${mount_count}" >&2
  exit 1
}

printf 'AuditAggregatorService production GeoIP manifest contract is valid.\n'
