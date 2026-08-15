#!/usr/bin/env bash
set -Eeuo pipefail

rendered_manifest="${1:?rendered Helm manifest is required}"

[[ -r "${rendered_manifest}" ]] || {
  printf 'rendered Helm manifest is not readable: %s\n' "${rendered_manifest}" >&2
  exit 1
}

temporary_directory="$(mktemp -d)"
trap 'rm -rf "${temporary_directory}"' EXIT

extract_source() {
  local source_suffix="$1"
  local output_file="$2"

  awk -v source_suffix="${source_suffix}" '
    /^---$/ {
      if (capture) {
        exit
      }
      next
    }

    /^# Source: / {
      capture = index($0, source_suffix) > 0
    }

    capture {
      print
    }
  ' "${rendered_manifest}" > "${output_file}"

  [[ -s "${output_file}" ]] || {
    printf 'required rendered resource is missing: %s\n' "${source_suffix}" >&2
    exit 1
  }
}

extract_container_block() {
  local input_file="$1"
  local section_name="$2"
  local resource_name="$3"
  local output_file="$4"

  awk \
    -v section_name="${section_name}" \
    -v resource_name="${resource_name}" '
    function indentation(line, prefix) {
      prefix = line
      sub(/[^ ].*$/, "", prefix)
      return length(prefix)
    }

    {
      stripped = $0
      sub(/^[[:space:]]+/, "", stripped)
      current_indent = indentation($0)

      if (capture) {
        if (stripped != "" && current_indent <= item_indent) {
          exit
        }
        print
        next
      }

      if (stripped == section_name ":") {
        in_section = 1
        section_indent = current_indent
        next
      }

      if (in_section && stripped != "" && current_indent <= section_indent) {
        in_section = 0
      }

      if (in_section && stripped == "- name: " resource_name) {
        capture = 1
        item_indent = current_indent
        print
      }
    }
  ' "${input_file}" > "${output_file}"

  [[ -s "${output_file}" ]] || {
    printf 'required %s container is missing from rendered manifest: %s\n' \
      "${section_name}" "${resource_name}" >&2
    exit 1
  }
}

verify_complete_resources() {
  local input_file="$1"
  local resource_name="$2"
  local cpu_count
  local memory_count

  grep -Eq '^[[:space:]]+resources:$' "${input_file}" || {
    printf 'container has no resources block: %s\n' "${resource_name}" >&2
    exit 1
  }
  grep -Eq '^[[:space:]]+limits:$' "${input_file}" || {
    printf 'container has no resource limits: %s\n' "${resource_name}" >&2
    exit 1
  }
  grep -Eq '^[[:space:]]+requests:$' "${input_file}" || {
    printf 'container has no resource requests: %s\n' "${resource_name}" >&2
    exit 1
  }

  cpu_count="$(awk '$1 == "cpu:" { count += 1 } END { print count + 0 }' "${input_file}")"
  memory_count="$(awk '$1 == "memory:" { count += 1 } END { print count + 0 }' "${input_file}")"

  [[ "${cpu_count}" -ge 2 && "${memory_count}" -ge 2 ]] || {
    printf 'container must define CPU and memory requests and limits: %s\n' \
      "${resource_name}" >&2
    exit 1
  }
}

grafana_deployment="${temporary_directory}/grafana-deployment.yaml"
prometheus_operator="${temporary_directory}/prometheus-operator.yaml"
grafana_service_monitor="${temporary_directory}/grafana-service-monitor.yaml"
grafana_test="${temporary_directory}/grafana-test.yaml"
solar_smoke_test="${temporary_directory}/solar-smoke-test.yaml"
admission_create_job="${temporary_directory}/admission-create-job.yaml"
admission_patch_job="${temporary_directory}/admission-patch-job.yaml"

extract_source \
  'kube-prometheus-stack/charts/grafana/templates/deployment.yaml' \
  "${grafana_deployment}"
extract_source \
  'kube-prometheus-stack/templates/prometheus-operator/deployment.yaml' \
  "${prometheus_operator}"
extract_source \
  'kube-prometheus-stack/charts/grafana/templates/servicemonitor.yaml' \
  "${grafana_service_monitor}"
extract_source \
  'kube-prometheus-stack/charts/grafana/templates/tests/test.yaml' \
  "${grafana_test}"
extract_source \
  'solar-battery/templates/tests/smoke-test.yaml' \
  "${solar_smoke_test}"
extract_source \
  'kube-prometheus-stack/templates/prometheus-operator/admission-webhooks/job-patch/job-createSecret.yaml' \
  "${admission_create_job}"
extract_source \
  'kube-prometheus-stack/templates/prometheus-operator/admission-webhooks/job-patch/job-patchWebhook.yaml' \
  "${admission_patch_job}"

while read -r section_name container_name
do
  container_block="${temporary_directory}/${container_name}.yaml"
  extract_container_block \
    "${grafana_deployment}" \
    "${section_name}" \
    "${container_name}" \
    "${container_block}"
  verify_complete_resources "${container_block}" "${container_name}"
done <<'CONTAINERS'
initContainers init-chown-data
containers grafana-sc-dashboard
containers grafana-sc-datasources
containers grafana
CONTAINERS

while read -r resource_file section_name container_name
do
  container_block="${temporary_directory}/${container_name}-policy.yaml"
  extract_container_block \
    "${resource_file}" \
    "${section_name}" \
    "${container_name}" \
    "${container_block}"
  verify_complete_resources "${container_block}" "${container_name}"
done <<CONTAINERS
${grafana_test} containers solar-test
${solar_smoke_test} containers smoke
${admission_create_job} containers create
${admission_patch_job} containers patch
CONTAINERS

if grep -Eq '^# Source: .*loki-stack/templates/tests/' "${rendered_manifest}"
then
  printf 'Loki test Pod must be disabled because it has no resource configuration\n' >&2
  exit 1
fi

for expected_argument in \
  '--config-reloader-cpu-request=10m' \
  '--config-reloader-cpu-limit=100m' \
  '--config-reloader-memory-request=32Mi' \
  '--config-reloader-memory-limit=128Mi'
do
  grep -Fq -- "${expected_argument}" "${prometheus_operator}" || {
    printf 'Prometheus config reloader resource argument is missing: %s\n' \
      "${expected_argument}" >&2
    exit 1
  }
done

grep -Eq '^[[:space:]]+interval: 15s$' "${grafana_service_monitor}" || {
  printf 'Grafana ServiceMonitor interval must be 15s\n' >&2
  exit 1
}
grep -Eq '^[[:space:]]+scrapeTimeout: 10s$' "${grafana_service_monitor}" || {
  printf 'Grafana ServiceMonitor scrapeTimeout must be 10s\n' >&2
  exit 1
}

printf 'MONITORING_RESOURCE_POLICY_OK\n'
