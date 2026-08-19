#!/usr/bin/env bash
set -Eeuo pipefail

root="${SOLAR_PLATFORM_ROOT:-/opt/solar-platform}"
config_file="${GEOIPUPDATE_CONFIG:-${root}/secrets/GeoIP.conf}"
database_path="${GEOIP_DB_PATH:-${root}/geoip/GeoLite2-City.mmdb}"
kubeconfig="${KUBECONFIG:-/home/deploy/.kube/config}"
namespace="${K3S_NAMESPACE:-solar-prod}"
deployment="${GEOIP_DEPLOYMENT:-auditaggregatorservice}"
work_dir="$(mktemp -d)"

cleanup() {
  rm -rf -- "${work_dir}"
}
trap cleanup EXIT

for tool in geoipupdate kubectl install mv cmp stat tail grep; do
  command -v "${tool}" >/dev/null 2>&1 || {
    printf 'required GeoIP synchronization tool is missing: %s\n' "${tool}" >&2
    exit 1
  }
done

[[ -f "${config_file}" && -r "${config_file}" ]] || {
  printf 'GeoIP update configuration is missing or unreadable: %s\n' \
    "${config_file}" >&2
  exit 1
}

# GeoIP.conf contains the MaxMind license key. Reject group/world permissions
# without ever printing the file contents.
config_mode="$(stat -c '%a' "${config_file}")"
group_permissions="${config_mode: -2:1}"
other_permissions="${config_mode: -1}"
(( 10#${group_permissions} == 0 && 10#${other_permissions} == 0 )) || {
  printf 'GeoIP update configuration must not be accessible to group/other (mode is %s): %s\n' \
    "${config_mode}" "${config_file}" >&2
  exit 1
}

grep -Eq '^[[:space:]]*EditionIDs([[:space:]]+[^#[:space:]]+)*[[:space:]]+GeoLite2-City([[:space:]]|$)' \
  "${config_file}" || {
    printf 'GeoIP update configuration must include EditionIDs GeoLite2-City\n' >&2
    exit 1
  }

grep -Eq 'MAXMIND_(ACCOUNT_ID|LICENSE_KEY)|CHANGE_ME|YOUR_' "${config_file}" && {
  printf 'GeoIP update configuration still contains placeholder credentials\n' >&2
  exit 1
}

geoipupdate -f "${config_file}" -d "${work_dir}"
downloaded_database="${work_dir}/GeoLite2-City.mmdb"

[[ -f "${downloaded_database}" && -r "${downloaded_database}" && -s "${downloaded_database}" ]] || {
  printf 'geoipupdate did not produce GeoLite2-City.mmdb\n' >&2
  exit 1
}

downloaded_size="$(stat -c '%s' "${downloaded_database}")"
(( downloaded_size >= 1048576 )) || {
  printf 'downloaded GeoLite2 City database is unexpectedly small: %s bytes\n' \
    "${downloaded_size}" >&2
  exit 1
}

LC_ALL=C grep -aF 'MaxMind.com' \
  < <(tail -c 131072 "${downloaded_database}") >/dev/null || {
    printf 'downloaded file does not contain the MaxMind metadata marker\n' >&2
    exit 1
  }

install -d -o root -g root -m 0755 "$(dirname -- "${database_path}")"

if [[ -f "${database_path}" ]] && cmp -s "${downloaded_database}" "${database_path}"; then
  printf 'GeoLite2 City database is already current.\n'
  exit 0
fi

staged_database="${database_path}.new"
install -o root -g root -m 0644 "${downloaded_database}" "${staged_database}"
mv -f -- "${staged_database}" "${database_path}"

# DatabaseReader keeps the opened database in this process. Restart only after
# the atomic host-file replacement so every new pod opens the new MMDB.
kubectl_args=(
  --kubeconfig="${kubeconfig}"
  --cache-dir="${work_dir}/kubectl-cache"
  -n "${namespace}"
)

if kubectl "${kubectl_args[@]}" \
  get deployment "${deployment}" >/dev/null 2>&1; then
  kubectl "${kubectl_args[@]}" \
    rollout restart "deployment/${deployment}"
  kubectl "${kubectl_args[@]}" \
    rollout status "deployment/${deployment}" --timeout=10m
fi

printf 'GeoLite2 City database synchronized successfully.\n'
