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

  [[ -s "${output_file}" ]] || {
    printf 'rendered resource block is empty: %s/%s\n' \
      "${expected_kind}" "${expected_name}" >&2
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

verify_resource_value() {
  local input_file="$1"
  local resource_section="$2"
  local resource_key="$3"
  local expected_value="$4"
  local resource_name="$5"
  local actual_value

  actual_value="$(
    awk \
      -v expected_section="${resource_section}" \
      -v expected_key="${resource_key}" '
        function indentation(line, prefix) {
          prefix = line
          sub(/[^ ].*$/, "", prefix)
          return length(prefix)
        }

        {
          stripped = $0
          sub(/^[[:space:]]+/, "", stripped)
          current_indent = indentation($0)

          if (in_value_section &&
              stripped != "" &&
              current_indent <= value_section_indent) {
            in_value_section = 0
          }

          if (in_resources &&
              stripped != "" &&
              current_indent <= resources_indent) {
            in_resources = 0
          }

          if (in_value_section &&
              stripped ~ ("^" expected_key ":[[:space:]]*")) {
            sub("^[^:]+:[[:space:]]*", "", stripped)
            gsub(/^"|"$/, "", stripped)
            print stripped
            exit
          }

          if (in_resources && stripped == expected_section ":") {
            in_value_section = 1
            value_section_indent = current_indent
            next
          }

          if (stripped == "resources:") {
            in_resources = 1
            resources_indent = current_indent
          }
        }
      ' "${input_file}"
  )"

  if [[ "${actual_value}" != "${expected_value}" ]]
  then
    printf '%s resources.%s.%s must equal %s (got %s)\n' \
      "${resource_name}" \
      "${resource_section}" \
      "${resource_key}" \
      "${expected_value}" \
      "${actual_value:-missing}" >&2
    exit 1
  fi
}

verify_application_service_monitor_contract() {
  local service_name="$1"
  local service_manifest="${temporary_directory}/${service_name}-service.yaml"
  local service_monitor_manifest="${temporary_directory}/${service_name}-service-monitor.yaml"
  local expected_component_label="app.kubernetes.io/component: ${service_name}"

  extract_named_resource \
    'Service' \
    "${service_name}" \
    "${service_manifest}"
  extract_named_resource \
    'ServiceMonitor' \
    "${service_name}" \
    "${service_monitor_manifest}"

  awk -v expected_label="${expected_component_label}" '
    /^metadata:[[:space:]]*$/ {
      in_metadata = 1
      next
    }

    /^spec:[[:space:]]*$/ {
      in_metadata = 0
    }

    in_metadata {
      line = $0
      sub(/^[[:space:]]+/, "", line)
      if (line == expected_label) {
        found = 1
      }
    }

    END { exit(found ? 0 : 1) }
  ' "${service_manifest}" || {
    printf 'application Service metadata label is missing: %s (%s)\n' \
      "${service_name}" "${expected_component_label}" >&2
    exit 1
  }

  grep -Eq \
    "^[[:space:]]+app[.]kubernetes[.]io/component:[[:space:]]+${service_name}[[:space:]]*$" \
    "${service_monitor_manifest}" || {
    printf 'application ServiceMonitor selector is missing: %s (%s)\n' \
      "${service_name}" "${expected_component_label}" >&2
    exit 1
  }
}

verify_network_policy_ingress_port() {
  local input_file="$1"
  local source_label="$2"
  local target_port="$3"
  local contract_name="$4"

  awk \
    -v expected_source="${source_label}" \
    -v expected_port="port: ${target_port}" '
      function flush_rule() {
        if (has_source && has_port) {
          found = 1
        }
        has_source = 0
        has_port = 0
      }

      /^  - from:[[:space:]]*$/ {
        flush_rule()
        in_rule = 1
        next
      }

      in_rule {
        if (index($0, expected_source) > 0) {
          has_source = 1
        }
        if (index($0, expected_port) > 0) {
          has_port = 1
        }
      }

      END {
        flush_rule()
        exit(found ? 0 : 1)
      }
    ' "${input_file}" || {
    printf '%s NetworkPolicy ingress contract is missing: source=%s port=%s\n' \
      "${contract_name}" "${source_label}" "${target_port}" >&2
    exit 1
  }
}

for application_service in \
  apigateway \
  auditaggregatorservice \
  authservice \
  batteryservice \
  emailservice \
  filestorageservice \
  notificationservice \
  smsservice \
  ticketservice
do
  verify_application_service_monitor_contract "${application_service}"
done

grafana_deployment="${temporary_directory}/grafana-deployment.yaml"
prometheus_operator="${temporary_directory}/prometheus-operator.yaml"
grafana_service_monitor="${temporary_directory}/grafana-service-monitor.yaml"
monitoring_network_policy="${temporary_directory}/monitoring-network-policy.yaml"
grafana_smoke_network_policy="${temporary_directory}/grafana-smoke-network-policy.yaml"
grafana_kubernetes_api_network_policy="${temporary_directory}/grafana-kubernetes-api-network-policy.yaml"
grafana_datasource_configmap="${temporary_directory}/grafana-datasource-configmap.yaml"
loki_datasource_configmap="${temporary_directory}/loki-datasource-configmap.yaml"
tempo_datasource_configmap="${temporary_directory}/tempo-datasource-configmap.yaml"
tempo_smoke_network_policy="${temporary_directory}/tempo-smoke-network-policy.yaml"
tempo_deployment="${temporary_directory}/tempo-deployment.yaml"
tempo_container="${temporary_directory}/tempo-container.yaml"
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
  'solar-battery/templates/tests/smoke-test.yaml' \
  "${solar_smoke_test}"
extract_source \
  'kube-prometheus-stack/templates/prometheus-operator/admission-webhooks/job-patch/job-createSecret.yaml' \
  "${admission_create_job}"
extract_source \
  'kube-prometheus-stack/templates/prometheus-operator/admission-webhooks/job-patch/job-patchWebhook.yaml' \
  "${admission_patch_job}"
extract_named_resource \
  'NetworkPolicy' \
  'allow-monitoring-stack' \
  "${monitoring_network_policy}"
extract_named_resource \
  'NetworkPolicy' \
  'allow-helm-smoke-to-grafana' \
  "${grafana_smoke_network_policy}"
extract_named_resource \
  'NetworkPolicy' \
  'allow-grafana-to-kubernetes-api' \
  "${grafana_kubernetes_api_network_policy}"
extract_named_resource \
  'NetworkPolicy' \
  'allow-helm-smoke-to-tempo' \
  "${tempo_smoke_network_policy}"
extract_named_resource \
  'ConfigMap' \
  'monitoring-grafana-datasource' \
  "${grafana_datasource_configmap}"
extract_named_resource \
  'ConfigMap' \
  'solar-loki-stack' \
  "${loki_datasource_configmap}"
extract_named_resource \
  'ConfigMap' \
  'tempo-grafana-datasource' \
  "${tempo_datasource_configmap}"

extract_named_resource \
  'Deployment' \
  'tempo' \
  "${tempo_deployment}"

extract_container_block \
  "${tempo_deployment}" \
  'containers' \
  'tempo' \
  "${tempo_container}"

verify_complete_resources "${tempo_container}" 'tempo'
verify_resource_value "${tempo_container}" 'requests' 'cpu' '50m' 'tempo'
verify_resource_value "${tempo_container}" 'requests' 'memory' '256Mi' 'tempo'
verify_resource_value "${tempo_container}" 'limits' 'cpu' '500m' 'tempo'
verify_resource_value "${tempo_container}" 'limits' 'memory' '1Gi' 'tempo'

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
${solar_smoke_test} containers smoke
${admission_create_job} containers create
${admission_patch_job} containers patch
CONTAINERS

if grep -Eq '^# Source: .*loki-stack/templates/tests/' "${rendered_manifest}"
then
  printf 'Loki test Pod must be disabled because it has no resource configuration\n' >&2
  exit 1
fi

if grep -Fq \
  '# Source: solar-battery/charts/kube-prometheus-stack/charts/grafana/templates/tests/test.yaml' \
  "${rendered_manifest}"
then
  printf 'upstream Grafana test Pod must be disabled to avoid Service selector collision\n' >&2
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

for sidecar_name in grafana-sc-dashboard grafana-sc-datasources
do
  sidecar_manifest="${temporary_directory}/${sidecar_name}.yaml"

  grep -Fq 'value: "configmap"' "${sidecar_manifest}" || {
    printf 'Grafana sidecar must watch ConfigMaps only: %s\n' \
      "${sidecar_name}" >&2
    exit 1
  }

  if grep -Fq 'value: "ALL"' "${sidecar_manifest}"
  then
    printf 'Grafana sidecar must be scoped to the release namespace: %s\n' \
      "${sidecar_name}" >&2
    exit 1
  fi
done

for expected in \
  'app.kubernetes.io/name: grafana' \
  'app.kubernetes.io/instance: solar' \
  'protocol: TCP' \
  'port: 443' \
  'port: 6443'
do
  grep -Fq "${expected}" "${grafana_kubernetes_api_network_policy}" || {
    printf 'Grafana Kubernetes API NetworkPolicy contract is missing: %s\n' \
      "${expected}" >&2
    exit 1
  }
done

while read -r source_name target_port
do
  verify_network_policy_ingress_port \
    "${monitoring_network_policy}" \
    "app.kubernetes.io/name: ${source_name}" \
    "${target_port}" \
    'monitoring stack'
done <<'MONITORING_INGRESS'
prometheus 3000
prometheus 9093
grafana 9090
grafana 9093
grafana 3100
grafana 3200
promtail 3100
MONITORING_INGRESS

default_datasource_count="$(
  awk '/^[[:space:]]+isDefault:[[:space:]]+true[[:space:]]*$/ { count += 1 }
       END { print count + 0 }' \
    "${grafana_datasource_configmap}" \
    "${loki_datasource_configmap}" \
    "${tempo_datasource_configmap}"
)"

[[ "${default_datasource_count}" == '1' ]] || {
  printf 'Grafana must have exactly one default datasource (got %s)\n' \
    "${default_datasource_count}" >&2
  exit 1
}

for expected in \
  'uid: prometheus' \
  'isDefault: true'
do
  grep -Fq "${expected}" "${grafana_datasource_configmap}" || {
    printf 'Grafana datasource provisioning is missing: %s\n' \
      "${expected}" >&2
    exit 1
  }
done
for expected in \
  'uid: tempo' \
  'url: http://tempo:3200' \
  'isDefault: false'
do
  grep -Fq "${expected}" "${tempo_datasource_configmap}" || {
    printf 'Tempo datasource provisioning is missing: %s\n' \
      "${expected}" >&2
    exit 1
  }
done

for expected in \
  'uid: "loki"' \
  'isDefault: false'
do
  grep -Fq "${expected}" "${loki_datasource_configmap}" || {
    printf 'Loki datasource provisioning is missing: %s\n' \
      "${expected}" >&2
    exit 1
  }
done

grep -Eq '^[[:space:]]+app[.]kubernetes[.]io/component: helm-smoke-test$' \
  "${solar_smoke_test}" || {
  printf 'Solar smoke test must use the dedicated helm-smoke-test label\n' >&2
  exit 1
}

if grep -Eq '^[[:space:]]+app[.]kubernetes[.]io/name: grafana$' \
  "${solar_smoke_test}"
then
  printf 'Solar smoke test must not match the Grafana Service selector\n' >&2
  exit 1
fi

for specification in \
  "${grafana_smoke_network_policy}|app.kubernetes.io/name: grafana|3000|Grafana" \
  "${tempo_smoke_network_policy}|app: tempo|3200|Tempo"
do
  policy_file="${specification%%|*}"
  remainder="${specification#*|}"
  target_label="${remainder%%|*}"
  remainder="${remainder#*|}"
  target_port="${remainder%%|*}"
  target_name="${remainder#*|}"

  for expected in \
    "${target_label}" \
    'app.kubernetes.io/component: helm-smoke-test' \
    'app.kubernetes.io/instance: solar' \
    'protocol: TCP' \
    "port: ${target_port}"
  do
    grep -Fq "${expected}" "${policy_file}" || {
      printf '%s smoke NetworkPolicy contract is missing: %s\n' \
        "${target_name}" "${expected}" >&2
      exit 1
    }
  done
done

for expected_url in \
  'http://notificationservice:80/metrics' \
  'http://filestorageservice:80/metrics' \
  'http://smsservice:80/metrics' \
  'http://solar-grafana:80/api/health' \
  'http://tempo:3200/ready'
do
  grep -Fq "${expected_url}" "${solar_smoke_test}" || {
    printf 'Solar smoke test endpoint is missing: %s\n' "${expected_url}" >&2
    exit 1
  }
done

for expected_grafana_resource in \
  '/api/datasources/uid/prometheus/health' \
  '/api/datasources/uid/alertmanager/health' \
  '/api/datasources/uid/loki/health' \
  '/api/datasources/uid/tempo/health' \
  '/api/dashboards/uid/alert-ticket-saga' \
  '/api/dashboards/uid/audit-pipeline' \
  '/api/dashboards/uid/auth-security' \
  '/api/dashboards/uid/solar-battery-health' \
  '/api/dashboards/uid/chat-hub-wave6' \
  '/api/dashboards/uid/solar-environmental-monitoring' \
  '/api/dashboards/uid/infrastructure' \
  '/api/dashboards/uid/solar-iot-fleet' \
  '/api/dashboards/uid/logs-overview' \
  '/api/dashboards/uid/messaging-reliability' \
  '/api/dashboards/uid/notification-ops' \
  '/api/dashboards/uid/services-overview' \
  '/api/dashboards/uid/solar-sla-ops'
do
  grep -Fq "${expected_grafana_resource}" "${solar_smoke_test}" || {
    printf 'Solar smoke test does not verify Grafana resource: %s\n' \
      "${expected_grafana_resource}" >&2
    exit 1
  }
done

for expected_secret_key in admin-user admin-password
do
  grep -Fq "key: ${expected_secret_key}" "${solar_smoke_test}" || {
    printf 'Solar smoke test Grafana credential reference is missing: %s\n' \
      "${expected_secret_key}" >&2
    exit 1
  }
done

for forbidden_url in \
  'http://notificationservice:80/health' \
  'http://filestorageservice:80/health' \
  'http://smsservice:80/health'
do
  if grep -Fq "${forbidden_url}" "${solar_smoke_test}"
  then
    printf 'Solar smoke test uses an unsupported endpoint: %s\n' "${forbidden_url}" >&2
    exit 1
  fi
done

grep -Eq '^[[:space:]]+"?helm[.]sh/hook-delete-policy"?:[[:space:]]+"?before-hook-creation"?$' \
  "${solar_smoke_test}" || {
    printf 'Solar smoke test must retain successful pods for diagnostics\n' >&2
    exit 1
  }

if grep -Fq 'hook-succeeded' "${solar_smoke_test}"
then
  printf 'Solar smoke test must not delete successful diagnostic pods\n' >&2
  exit 1
fi

printf 'MONITORING_RESOURCE_POLICY_OK\n'
