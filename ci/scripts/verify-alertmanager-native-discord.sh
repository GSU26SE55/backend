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
alertmanager_config_resource="${temporary_directory}/alertmanager-config-resource.yaml"
printf '%s' "${encoded_config}" | base64 --decode > "${decoded_config}"

extract_named_resource() {
  local expected_kind="$1"
  local expected_name="$2"
  local output_file="$3"

  awk -v expected_kind="${expected_kind}" -v expected_name="${expected_name}" '
    function reset_document() {
      document = ""
      kind_matches = 0
      name_matches = 0
    }

    function flush_document() {
      if (kind_matches && name_matches) {
        printf "%s", document
        found = 1
      }
      reset_document()
    }

    BEGIN { reset_document() }

    /^---$/ {
      flush_document()
      next
    }

    {
      document = document $0 ORS
      if ($0 ~ "^kind:[[:space:]]*" expected_kind "[[:space:]]*$") {
        kind_matches = 1
      }
      if ($0 ~ "^[[:space:]]*name:[[:space:]]*" expected_name "[[:space:]]*$") {
        name_matches = 1
      }
    }

    END {
      flush_document()
      if (!found) exit 1
    }
  ' "${rendered_manifest}" > "${output_file}" || {
    printf 'required rendered resource is missing: %s/%s\n' \
      "${expected_kind}" "${expected_name}" >&2
    exit 1
  }
}

extract_named_resource \
  'Alertmanager' \
  'monitoring-alertmanager' \
  "${alertmanager_resource}"
extract_named_resource \
  'AlertmanagerConfig' \
  'solar-discord' \
  "${alertmanager_config_resource}"

if grep -Eq 'webhook_url(_file)?:' "${decoded_config}"
then
  printf 'base Alertmanager configuration must not contain an unresolved Discord webhook\n' >&2
  exit 1
fi

for expected in 'receiver: "null"' '- name: "null"'
do
  grep -Fq -- "${expected}" "${decoded_config}" || {
    printf 'safe fallback Alertmanager configuration is missing: %s\n' "${expected}" >&2
    exit 1
  }
done

grep -Eq '^  alertmanagerConfiguration:$' "${alertmanager_resource}" || {
  printf 'Alertmanager does not select the global AlertmanagerConfig\n' >&2
  exit 1
}

grep -Eq '^    name: solar-discord$' "${alertmanager_resource}" || {
  printf 'Alertmanager global configuration name is not solar-discord\n' >&2
  exit 1
}

for expected in \
  'discordConfigs:' \
  'apiURL:' \
  'name: solar-secrets' \
  'key: DISCORD_WEBHOOK' \
  'sendResolved: true'
do
  grep -Fq "${expected}" "${alertmanager_config_resource}" || {
    printf 'native Alertmanager Discord SecretKeySelector contract is missing: %s\n' \
      "${expected}" >&2
    exit 1
  }
done

printf 'ALERTMANAGER_NATIVE_DISCORD_OK\n'
