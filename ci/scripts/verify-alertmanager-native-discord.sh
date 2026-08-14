#!/usr/bin/env bash
set -Eeuo pipefail

rendered_manifest="${1:?rendered Helm manifest is required}"

[[ -r "${rendered_manifest}" ]] || {
  printf 'rendered Helm manifest is not readable: %s\n' "${rendered_manifest}" >&2
  exit 1
}

if grep -Fq 'alertmanager-discord' "${rendered_manifest}"; then
  printf 'legacy alertmanager-discord relay is still present in the Helm render\n' >&2
  exit 1
fi

encoded_config="$(
  sed -n 's/^  alertmanager[.]yaml: "\([A-Za-z0-9+\/=]*\)"$/\1/p' \
    "${rendered_manifest}" |
    head -n 1
)"

[[ -n "${encoded_config}" ]] || {
  printf 'rendered Alertmanager configuration Secret is missing\n' >&2
  exit 1
}

temporary_directory="$(mktemp -d)"
trap 'rm -rf "${temporary_directory}"' EXIT
decoded_config="${temporary_directory}/alertmanager.yaml"
alertmanager_resource="${temporary_directory}/alertmanager-resource.yaml"
printf '%s' "${encoded_config}" | base64 --decode > "${decoded_config}"

awk '
  /^---$/ {
    if (capture) {
      exit
    }
    next
  }
  /^kind: Alertmanager$/ {
    capture = 1
  }
  capture {
    print
  }
' "${rendered_manifest}" > "${alertmanager_resource}"

[[ -s "${alertmanager_resource}" ]] || {
  printf 'rendered Alertmanager custom resource is missing\n' >&2
  exit 1
}

for expected in \
  'discord_configs:' \
  'webhook_url_file: /etc/alertmanager/secrets/solar-secrets/DISCORD_WEBHOOK'
do
  grep -Fq "${expected}" "${decoded_config}" || {
    printf 'native Alertmanager Discord contract is missing: %s\n' "${expected}" >&2
    exit 1
  }
done

grep -Eq '^  secrets:$' "${alertmanager_resource}" || {
  printf 'Alertmanager Secret mounts are missing from the rendered manifest\n' >&2
  exit 1
}

grep -Eq '^    - solar-secrets$' "${alertmanager_resource}" || {
  printf 'solar-secrets is not mounted into Alertmanager\n' >&2
  exit 1
}

printf 'ALERTMANAGER_NATIVE_DISCORD_OK\n'
