#!/usr/bin/env bash
set -Eeuo pipefail

release_sha="${1:?full Git SHA is required}"
root="${SOLAR_PLATFORM_ROOT:-/opt/solar-platform}"
script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
payload_dir="$(cd -- "${script_dir}/../.." && pwd)"

[[ "${release_sha}" =~ ^[0-9a-f]{40}$ ]] || {
  printf 'release SHA must contain exactly 40 lowercase hex characters\n' >&2
  exit 2
}

host_env="${root}/config/host.env"
backend_env="${root}/secrets/backend.env"
monitoring_env="${root}/secrets/monitoring.env"
geoip_db="${root}/geoip/GeoLite2-City.mmdb"
image_lock="${payload_dir}/deploy/production/image-lock.env"

[[ -r "${image_lock}" ]] || {
  printf 'trusted image lock is missing: %s\n' "${image_lock}" >&2
  exit 1
}

"${script_dir}/preflight-production.sh"

read_env() {
  local key="$1"
  local file="$2"
  sed -n "s/^${key}=//p" "${file}" | tail -n 1 | tr -d '\r'
}

required_digest_keys=(
  APIGATEWAY_DIGEST
  AUTHSERVICE_DIGEST
  EMAILSERVICE_DIGEST
  SMSSERVICE_DIGEST
  FILESTORAGESERVICE_DIGEST
  BATTERYSERVICE_DIGEST
  TICKETSERVICE_DIGEST
  NOTIFICATIONSERVICE_DIGEST
  AUDITAGGREGATORSERVICE_DIGEST
)

for key in "${required_digest_keys[@]}"; do
  value="$(read_env "${key}" "${image_lock}")"
  [[ "${value}" =~ ^sha256:[0-9a-f]{64}$ ]] || {
    printf 'invalid immutable application digest: %s\n' "${key}" >&2
    exit 1
  }
done

image_namespace="ghcr.io/gsu26se55"
for specification in \
  "apigateway|APIGATEWAY_DIGEST" \
  "authservice|AUTHSERVICE_DIGEST" \
  "emailservice|EMAILSERVICE_DIGEST" \
  "smsservice|SMSSERVICE_DIGEST" \
  "filestorageservice|FILESTORAGESERVICE_DIGEST" \
  "batteryservice|BATTERYSERVICE_DIGEST" \
  "ticketservice|TICKETSERVICE_DIGEST" \
  "notificationservice|NOTIFICATIONSERVICE_DIGEST" \
  "auditaggregatorservice|AUDITAGGREGATORSERVICE_DIGEST"
do
  service="${specification%%|*}"
  key="${specification#*|}"
  image_ref="${image_namespace}/${service}@$(read_env "${key}" "${image_lock}")"
  cosign verify --key "${root}/config/cosign.pub" "${image_ref}" >/dev/null
done

platform_domain="$(read_env PLATFORM_PUBLIC_DOMAIN "${host_env}")"
frontend_origin="$(read_env FRONTEND_PUBLIC_ORIGIN "${host_env}")"
ai_grpc_address="$(read_env AI_GRPC_ADDRESS "${host_env}")"
ai_http_base_url="$(read_env AI_HTTP_BASE_URL "${host_env}")"
ai_wireguard_ipv4="$(read_env AI_WIREGUARD_IPV4 "${host_env}")"
platform_wireguard_ipv4="$(read_env PLATFORM_WIREGUARD_IPV4 "${host_env}")"
mqtt_node_ip="$(read_env MQTT_NODE_IP "${host_env}")"
mqtt_auth_dir="$(read_env MQTT_AUTH_DIR "${host_env}")"
kubeconfig="$(read_env KUBECONFIG "${host_env}")"
namespace="$(read_env K3S_NAMESPACE "${host_env}")"
helm_release="$(read_env HELM_RELEASE "${host_env}")"
deployment_phase="$(read_env PLATFORM_DEPLOYMENT_PHASE "${host_env}")"
ticket_periodic_maintenance_enabled="$(read_env TICKET_PERIODIC_MAINTENANCE_ENABLED "${host_env}")"
ticket_periodic_maintenance_time_zone_id="$(read_env TICKET_PERIODIC_MAINTENANCE_TIME_ZONE_ID "${host_env}")"
ticket_periodic_maintenance_cycle_months="$(read_env TICKET_PERIODIC_MAINTENANCE_CYCLE_MONTHS "${host_env}")"
ticket_periodic_maintenance_lead_days="$(read_env TICKET_PERIODIC_MAINTENANCE_LEAD_DAYS "${host_env}")"
ticket_periodic_maintenance_overdue_window_days="$(read_env TICKET_PERIODIC_MAINTENANCE_OVERDUE_WINDOW_DAYS "${host_env}")"
ticket_periodic_maintenance_reminder_time="$(read_env TICKET_PERIODIC_MAINTENANCE_REMINDER_TIME "${host_env}")"
ticket_periodic_maintenance_poll_interval_seconds="$(read_env TICKET_PERIODIC_MAINTENANCE_POLL_INTERVAL_SECONDS "${host_env}")"
ticket_periodic_maintenance_batch_size="$(read_env TICKET_PERIODIC_MAINTENANCE_BATCH_SIZE "${host_env}")"
sla_business_hours_time_zone_id="$(read_env SLA_BUSINESS_HOURS_TIME_ZONE_ID "${host_env}")"
sla_business_hours_start="$(read_env SLA_BUSINESS_HOURS_START "${host_env}")"
sla_business_hours_end="$(read_env SLA_BUSINESS_HOURS_END "${host_env}")"
sla_business_hours_working_days_0="$(read_env SLA_BUSINESS_HOURS_WORKING_DAYS_0 "${host_env}")"
sla_business_hours_working_days_1="$(read_env SLA_BUSINESS_HOURS_WORKING_DAYS_1 "${host_env}")"
sla_business_hours_working_days_2="$(read_env SLA_BUSINESS_HOURS_WORKING_DAYS_2 "${host_env}")"
sla_business_hours_working_days_3="$(read_env SLA_BUSINESS_HOURS_WORKING_DAYS_3 "${host_env}")"
sla_business_hours_working_days_4="$(read_env SLA_BUSINESS_HOURS_WORKING_DAYS_4 "${host_env}")"
sla_business_hours_working_days_5="$(read_env SLA_BUSINESS_HOURS_WORKING_DAYS_5 "${host_env}")"
sla_business_hours_working_days_6="$(read_env SLA_BUSINESS_HOURS_WORKING_DAYS_6 "${host_env}")"

[[ -n "${platform_domain}" && -n "${frontend_origin}" && -n "${ai_grpc_address}" \
  && -n "${ai_http_base_url}" && -n "${ai_wireguard_ipv4}" \
  && -n "${platform_wireguard_ipv4}" && -n "${mqtt_node_ip}" && -n "${mqtt_auth_dir}" \
  && -n "${kubeconfig}" && -n "${namespace}" && -n "${helm_release}" \
  && -n "${deployment_phase}" ]] || {
  printf 'host.env is incomplete\n' >&2
  exit 1
}
export KUBECONFIG="${kubeconfig}"

umask 027
release_dir="${root}/releases/${release_sha}"
mkdir -p "${root}/releases"
if [[ -e "${release_dir}" ]]; then
  cmp "${image_lock}" "${release_dir}/deploy/production/image-lock.env" || {
    printf 'immutable release directory exists with a different image lock\n' >&2
    exit 1
  }
else
  mkdir -p "${release_dir}"
  cp -a "${payload_dir}/deploy" "${release_dir}/deploy"
fi

chart_dir="${release_dir}/deploy/helm/solar-battery"
[[ -f "${chart_dir}/Chart.yaml" && -d "${chart_dir}/charts" ]] || {
  printf 'release payload does not contain a dependency-complete Helm chart\n' >&2
  exit 1
}

kubectl create namespace "${namespace}" --dry-run=client -o yaml | kubectl apply -f -
kubectl -n "${namespace}" create secret generic solar-secrets \
  --from-env-file="${backend_env}" --dry-run=client -o yaml | kubectl apply -f -
kubectl -n "${namespace}" create secret generic solar-monitoring-secrets \
  --from-env-file="${monitoring_env}" --dry-run=client -o yaml | kubectl apply -f -

kubectl -n "${namespace}" get secret ghcr-pull >/dev/null || {
  printf 'required registry pull Secret is missing: %s/ghcr-pull\n' "${namespace}" >&2
  exit 1
}

print_backup_diagnostics() {
  local backup_job="$1"
  local pod
  local -a backup_pods=()

  printf 'Pre-deployment PostgreSQL backup diagnostics for %s\n' "${backup_job}" >&2
  kubectl -n "${namespace}" get job "${backup_job}" -o wide >&2 || true
  kubectl -n "${namespace}" describe job "${backup_job}" >&2 || true

  mapfile -t backup_pods < <(
    kubectl -n "${namespace}" get pod \
      -l "job-name=${backup_job}" -o name 2>/dev/null
  )

  if [[ "${#backup_pods[@]}" -eq 0 ]]; then
    printf 'No Pod currently exists for backup Job %s\n' "${backup_job}" >&2
  fi

  for pod in "${backup_pods[@]}"; do
    kubectl -n "${namespace}" describe "${pod}" >&2 || true
    kubectl -n "${namespace}" logs "${pod}" \
      -c dump --timestamps >&2 || true
    kubectl -n "${namespace}" logs "${pod}" \
      -c dump --previous --timestamps >&2 || true
    kubectl -n "${namespace}" logs "${pod}" \
      -c upload --timestamps >&2 || true
    kubectl -n "${namespace}" logs "${pod}" \
      -c upload --previous --timestamps >&2 || true
  done

  kubectl -n "${namespace}" get resourcequota solar-quota >&2 || true
  kubectl -n "${namespace}" get event \
    --sort-by=.metadata.creationTimestamp | tail -n 100 >&2 || true
}

run_predeployment_backup() {
  local backup_active_deadline
  local backup_completed
  local backup_conditions
  local backup_job="postgres-predeploy-${release_sha:0:12}"
  local backup_wait_deadline

  kubectl -n "${namespace}" delete job "${backup_job}" --ignore-not-found >/dev/null
  kubectl -n "${namespace}" create job "${backup_job}" --from=cronjob/postgres-backup

  backup_active_deadline="$(
    kubectl -n "${namespace}" get job "${backup_job}" \
      -o jsonpath='{.spec.activeDeadlineSeconds}'
  )"
  [[ "${backup_active_deadline}" =~ ^[0-9]+$ ]] || {
    print_backup_diagnostics "${backup_job}"
    printf 'pre-deployment database backup has an invalid active deadline\n' >&2
    exit 1
  }

  backup_wait_deadline="$((SECONDS + backup_active_deadline + 30))"
  backup_completed=false
  while (( SECONDS < backup_wait_deadline )); do
    if ! backup_conditions="$(
      kubectl -n "${namespace}" get job "${backup_job}" \
        -o jsonpath='{range .status.conditions[*]}{.type}={.status}{"\n"}{end}'
    )"; then
      print_backup_diagnostics "${backup_job}"
      printf 'pre-deployment database backup Job disappeared\n' >&2
      exit 1
    fi

    if grep -qx 'Complete=True' <<< "${backup_conditions}"; then
      printf 'Pre-deployment PostgreSQL backup completed: %s\n' "${backup_job}"
      backup_completed=true
      break
    fi

    if grep -Eq '^(Failed|FailureTarget)=True$' <<< "${backup_conditions}"; then
      print_backup_diagnostics "${backup_job}"
      printf 'pre-deployment database backup failed\n' >&2
      exit 1
    fi

    sleep 5
  done

  if [[ "${backup_completed}" != true ]]; then
    print_backup_diagnostics "${backup_job}"
    printf 'pre-deployment database backup exceeded its active deadline\n' >&2
    exit 1
  fi
}

helm_value_args=(
  -f "${chart_dir}/values.yaml"
  -f "${chart_dir}/values-production.yaml"
  # Capacity/storage overlay is intentionally last so it wins on the 80 GB R4.
  -f "${chart_dir}/values-vps-small.yaml"
  --set-string "global.domain=${platform_domain}"
  --set-string "global.frontendOrigin=${frontend_origin}"
  --set-string "config.Ai__GrpcAddress=${ai_grpc_address}"
  --set-string "config.Ai__HttpBaseUrl=${ai_http_base_url}"
  --set-string "config.TicketAi__AiGrpcAddress=${ai_grpc_address}"
  --set-string "config.Ticket__PeriodicMaintenance__Enabled=${ticket_periodic_maintenance_enabled}"
  --set-string "config.Ticket__PeriodicMaintenance__TimeZoneId=${ticket_periodic_maintenance_time_zone_id}"
  --set-string "config.Ticket__PeriodicMaintenance__CycleMonths=${ticket_periodic_maintenance_cycle_months}"
  --set-string "config.Ticket__PeriodicMaintenance__LeadDays=${ticket_periodic_maintenance_lead_days}"
  --set-string "config.Ticket__PeriodicMaintenance__OverdueScheduleWindowDays=${ticket_periodic_maintenance_overdue_window_days}"
  --set-string "config.Ticket__PeriodicMaintenance__ReminderTime=${ticket_periodic_maintenance_reminder_time}"
  --set-string "config.Ticket__PeriodicMaintenance__PollIntervalSeconds=${ticket_periodic_maintenance_poll_interval_seconds}"
  --set-string "config.Ticket__PeriodicMaintenance__BatchSize=${ticket_periodic_maintenance_batch_size}"
  --set-string "config.SlaBusinessHours__TimeZoneId=${sla_business_hours_time_zone_id}"
  --set-string "config.SlaBusinessHours__Start=${sla_business_hours_start}"
  --set-string "config.SlaBusinessHours__End=${sla_business_hours_end}"
  --set-string "config.SlaBusinessHours__WorkingDays__0=${sla_business_hours_working_days_0}"
  --set-string "config.SlaBusinessHours__WorkingDays__1=${sla_business_hours_working_days_1}"
  --set-string "config.SlaBusinessHours__WorkingDays__2=${sla_business_hours_working_days_2}"
  --set-string "config.SlaBusinessHours__WorkingDays__3=${sla_business_hours_working_days_3}"
  --set-string "config.SlaBusinessHours__WorkingDays__4=${sla_business_hours_working_days_4}"
  --set-string "config.SlaBusinessHours__WorkingDays__5=${sla_business_hours_working_days_5}"
  --set-string "config.SlaBusinessHours__WorkingDays__6=${sla_business_hours_working_days_6}"
  --set-string "wireguard.aiIpv4=${ai_wireguard_ipv4}"
  --set-string "wireguard.platformIpv4=${platform_wireguard_ipv4}"
  --set-string "kube-prometheus-stack.prometheus.prometheusSpec.hostAliases[0].ip=${ai_wireguard_ipv4}"
  --set-string "kube-prometheus-stack.prometheus.prometheusSpec.hostAliases[0].hostnames[0]=ai.${platform_domain}"
  --set-string "iot.mqttNodeIp=${mqtt_node_ip}"
  --set-string "iot.mqttPasswordSync.hostPath=${mqtt_auth_dir}"
  --set-string "services.auditaggregatorservice.geoIp.hostPath=${geoip_db}"
)

if [[ "${deployment_phase}" == 'bootstrap' ]]; then
  # Loki does not exist until this Helm release completes, so R3 Alloy cannot
  # be enabled yet. All other application, infrastructure and observability
  # components remain enabled. A second, steady deployment restores this gate.
  helm_value_args+=(--set 'monitoring.ai.enabled=false')
else
  helm_value_args+=(--set 'monitoring.ai.enabled=true')
fi

helm_value_args+=(--set-string "services.apigateway.digest=$(read_env APIGATEWAY_DIGEST "${image_lock}")")
helm_value_args+=(--set-string "services.authservice.digest=$(read_env AUTHSERVICE_DIGEST "${image_lock}")")
helm_value_args+=(--set-string "services.emailservice.digest=$(read_env EMAILSERVICE_DIGEST "${image_lock}")")
helm_value_args+=(--set-string "services.smsservice.digest=$(read_env SMSSERVICE_DIGEST "${image_lock}")")
helm_value_args+=(--set-string "services.filestorageservice.digest=$(read_env FILESTORAGESERVICE_DIGEST "${image_lock}")")
helm_value_args+=(--set-string "services.batteryservice.digest=$(read_env BATTERYSERVICE_DIGEST "${image_lock}")")
helm_value_args+=(--set-string "services.ticketservice.digest=$(read_env TICKETSERVICE_DIGEST "${image_lock}")")
helm_value_args+=(--set-string "services.notificationservice.digest=$(read_env NOTIFICATIONSERVICE_DIGEST "${image_lock}")")
helm_value_args+=(--set-string "services.auditaggregatorservice.digest=$(read_env AUDITAGGREGATORSERVICE_DIGEST "${image_lock}")")

helm lint "${chart_dir}" "${helm_value_args[@]}"

rendered_manifest="$(mktemp)"
backup_cronjob_manifest="$(mktemp)"
cleanup_rendered_manifest() {
  rm -f "${rendered_manifest}" "${backup_cronjob_manifest}"
}
trap cleanup_rendered_manifest EXIT

helm template "${helm_release}" "${chart_dir}" \
  --namespace "${namespace}" \
  "${helm_value_args[@]}" \
  > "${rendered_manifest}"

"${script_dir}/verify-monitoring-resource-policy.sh" "${rendered_manifest}"
"${script_dir}/verify-postgres-backup-policy.sh" "${rendered_manifest}"
"${script_dir}/verify-geoip-production.sh" "${rendered_manifest}" "${geoip_db}"

# The mandatory backup must use the target release's bounded pg_dump policy,
# not the CronJob template left by the previous Helm revision. Pre-applying this
# Helm-owned resource is safe: a later atomic rollback restores the old release
# manifest if the upgrade itself fails.
awk '
  /^---$/ {
    if (capture) exit
    next
  }

  /^# Source: / {
    capture = index($0, "solar-battery/templates/infra/postgres-backup.yaml") > 0
  }

  capture { print }
' "${rendered_manifest}" > "${backup_cronjob_manifest}"

[[ -s "${backup_cronjob_manifest}" ]] || {
  printf 'target release does not contain the required PostgreSQL backup CronJob\n' >&2
  exit 1
}

if kubectl -n "${namespace}" get cronjob postgres-backup >/dev/null 2>&1; then
  kubectl -n "${namespace}" apply -f "${backup_cronjob_manifest}"
  run_predeployment_backup
else
  printf 'No existing PostgreSQL backup CronJob; skipping backup for initial Helm install\n'
fi

helm_args=(
  upgrade --install "${helm_release}" "${chart_dir}"
  --namespace "${namespace}"
  --atomic
  --cleanup-on-fail
  --wait
  --timeout 25m
  --history-max 10
  "${helm_value_args[@]}"
)

helm "${helm_args[@]}"

# Helm --wait covers the release resources. Explicit rollout checks provide a
# useful resource name on failure without waiting for completed Job pods to
# become Ready (a completed Pod never has Ready=True).
while IFS= read -r resource; do
  [[ -n "${resource}" ]] || continue
  if ! kubectl -n "${namespace}" rollout status "${resource}" --timeout=10m; then
    kubectl -n "${namespace}" describe "${resource}" >&2 || true
    kubectl -n "${namespace}" get event \
      --sort-by=.metadata.creationTimestamp >&2 || true
    printf 'workload rollout failed after Helm upgrade: %s\n' "${resource}" >&2
    exit 1
  fi
done < <(kubectl -n "${namespace}" get deployment,statefulset,daemonset -o name)

wait_for_loki_bridge() {
  local attempts=0

  while (( attempts < 60 )); do
    attempts=$((attempts + 1))
    if systemctl is-active --quiet solar-loki-wireguard.service \
      && curl --fail --silent --show-error --connect-timeout 2 --max-time 5 \
        "http://${platform_wireguard_ipv4}:3100/ready" >/dev/null 2>&1; then
      printf 'Loki WireGuard bridge is ready on %s:3100.\n' \
        "${platform_wireguard_ipv4}"
      return 0
    fi
    sleep 5
  done

  systemctl status solar-loki-wireguard.service --no-pager >&2 || true
  kubectl -n "${namespace}" get service loki -o wide >&2 || true
  printf 'Loki WireGuard bridge did not become ready after Helm deployment\n' >&2
  return 1
}

wait_for_loki_bridge || exit 1

verify_geoip_runtime() {
  local configured_path
  local geoip_logs
  local pod

  pod="$(
    kubectl -n "${namespace}" get pod \
      -l app.kubernetes.io/component=auditaggregatorservice \
      -o json |
      jq -er '
        [.items[]
          | select(.status.phase == "Running")
          | select((.status.containerStatuses // []) | length > 0)
          | select(all(.status.containerStatuses[]; .ready == true))]
        | sort_by(.metadata.creationTimestamp)
        | last
        | .metadata.name
      '
  )" || {
    kubectl -n "${namespace}" get pod \
      -l app.kubernetes.io/component=auditaggregatorservice -o wide >&2 || true
    printf 'unable to find a Ready AuditAggregatorService pod for GeoIP verification\n' >&2
    return 1
  }

  configured_path="$(
    kubectl -n "${namespace}" exec "${pod}" -c auditaggregatorservice -- \
      printenv GeoIp__DbPath
  )"
  [[ "${configured_path}" == '/app/geoip/GeoLite2-City.mmdb' ]] || {
    printf 'AuditAggregatorService has an unexpected GeoIp__DbPath: %s\n' \
      "${configured_path}" >&2
    return 1
  }

  # Expanded inside the container, not by this deployment shell.
  # shellcheck disable=SC2016
  kubectl -n "${namespace}" exec "${pod}" -c auditaggregatorservice -- \
    sh -ec 'test "$GeoIp__Required" = "true" && test -r "$GeoIp__DbPath" && test -s "$GeoIp__DbPath"' || {
      printf 'AuditAggregatorService cannot read the required GeoLite2 database\n' >&2
      return 1
    }

  geoip_logs="$(
    kubectl -n "${namespace}" logs "${pod}" -c auditaggregatorservice \
      --tail=2000
  )"
  grep -F 'MaxMind GeoLite2 loaded' <<< "${geoip_logs}" >/dev/null || {
    kubectl -n "${namespace}" logs "${pod}" -c auditaggregatorservice \
      --tail=2000 >&2 || true
    printf 'AuditAggregatorService did not confirm loading the GeoLite2 database\n' >&2
    return 1
  }

  printf 'AuditAggregatorService loaded the required GeoLite2 City database.\n'
}

verify_geoip_runtime || exit 1

# Helm does not wait for Prometheus Operator custom-resource conditions. In
# particular, Alertmanager can exist while its StatefulSet was never created.
wait_for_operator_resource() {
  local resource_type="$1"
  local display_name="$2"
  local resource
  local -a resources=()

  mapfile -t resources < <(
    kubectl -n "${namespace}" get "${resource_type}" \
      -l "app.kubernetes.io/instance=${helm_release}" \
      -o name
  )

  if [[ "${#resources[@]}" -ne 1 ]]; then
    kubectl -n "${namespace}" get "${resource_type}" -o wide >&2 || true
    printf 'expected exactly one %s custom resource for Helm release %s; found %s\n' \
      "${display_name}" "${helm_release}" "${#resources[@]}" >&2
    exit 1
  fi

  resource="${resources[0]}"
  if ! kubectl -n "${namespace}" wait \
    --for=condition=Available "${resource}" --timeout=10m; then
    kubectl -n "${namespace}" describe "${resource}" >&2 || true
    kubectl -n "${namespace}" get pod,statefulset -o wide >&2 || true
    kubectl -n "${namespace}" logs deployment/monitoring-operator \
      --since=15m --tail=200 >&2 || true
    kubectl -n "${namespace}" get event \
      --sort-by=.metadata.creationTimestamp >&2 || true
    printf '%s did not become Available after Helm upgrade: %s\n' \
      "${display_name}" "${resource}" >&2
    exit 1
  fi
}

wait_for_operator_resource 'alertmanagers.monitoring.coreos.com' 'Alertmanager'
wait_for_operator_resource 'prometheuses.monitoring.coreos.com' 'Prometheus'

verify_ai_observability_targets() {
  local attempts=0
  local port_forward_log
  local port_forward_pid
  local prometheus_service
  local query_result
  local verified=false

  prometheus_service="$(
    kubectl -n "${namespace}" get service -o json |
      jq -er -f "${script_dir}/select-prometheus-service.jq"
  )" || {
    kubectl -n "${namespace}" get service -o wide >&2 || true
    printf 'unable to identify the Prometheus service\n' >&2
    return 1
  }

  port_forward_log="$(mktemp)"
  kubectl -n "${namespace}" port-forward \
    "service/${prometheus_service}" 19090:9090 \
    >"${port_forward_log}" 2>&1 &
  port_forward_pid=$!

  while (( attempts < 36 )); do
    attempts=$((attempts + 1))
    if ! kill -0 "${port_forward_pid}" 2>/dev/null; then
      break
    fi

    query_result="$(
      curl --fail --silent --show-error --get \
        --data-urlencode 'query=count by(job) (up{job=~"ai-(application|node|cadvisor|alloy)"} == 1)' \
        http://127.0.0.1:19090/api/v1/query 2>/dev/null || true
    )"
    if jq -e '
      [.data.result[].metric.job] | sort
      == ["ai-alloy", "ai-application", "ai-cadvisor", "ai-node"]
    ' <<<"${query_result}" >/dev/null 2>&1; then
      verified=true
      break
    fi
    sleep 5
  done

  if [[ "${verified}" != true ]]; then
    printf 'AI Prometheus targets did not all become UP\n' >&2
    curl --silent --show-error --get \
      --data-urlencode 'state=active' \
      http://127.0.0.1:19090/api/v1/targets 2>/dev/null |
      jq '.data.activeTargets[] | select(.labels.job | startswith("ai-"))
          | {job: .labels.job, health, lastError, scrapeUrl}' >&2 || true
    cat "${port_forward_log}" >&2 || true
  else
    printf '%s\n' \
      'AI Prometheus targets are UP: ai-alloy, ai-application, ai-cadvisor, ai-node'
  fi

  kill "${port_forward_pid}" 2>/dev/null || true
  wait "${port_forward_pid}" 2>/dev/null || true
  rm -f "${port_forward_log}"
  [[ "${verified}" == true ]]
}

if [[ "${deployment_phase}" == 'steady' ]]; then
  verify_ai_observability_targets || exit 1
else
  printf '%s\n' \
    'Bootstrap phase: AI target verification is deferred until R3 is connected and steady deployment runs.'
fi

if ! helm test "${helm_release}" --namespace "${namespace}" --logs --timeout 5m; then
  helm status "${helm_release}" --namespace "${namespace}" >&2 || true
  kubectl -n "${namespace}" describe pod "${helm_release}-smoke-test" >&2 || true
  kubectl -n "${namespace}" logs pod/"${helm_release}-smoke-test" \
    --all-containers=true >&2 || true
  kubectl -n "${namespace}" get service "${helm_release}-grafana" -o wide >&2 || true
  kubectl -n "${namespace}" get endpointslice \
    -l "kubernetes.io/service-name=${helm_release}-grafana" -o wide >&2 || true
  kubectl -n "${namespace}" get networkpolicy \
    allow-monitoring-stack \
    allow-helm-smoke-to-grafana \
    allow-helm-smoke-to-tempo \
    allow-grafana-to-kubernetes-api \
    allow-monitoring-discovery-to-kubernetes-api \
    -o yaml >&2 || true
  kubectl -n "${namespace}" get configmap \
    -l grafana_datasource=1 -o name >&2 || true
  kubectl -n "${namespace}" get configmap \
    -l grafana_dashboard=1 -o name >&2 || true
  kubectl -n "${namespace}" logs deployment/"${helm_release}-grafana" \
    -c grafana-sc-datasources --since=15m --tail=200 >&2 || true
  kubectl -n "${namespace}" logs deployment/"${helm_release}-grafana" \
    -c grafana-sc-dashboard --since=15m --tail=200 >&2 || true
  kubectl -n "${namespace}" logs deployment/"${helm_release}-grafana" \
    -c grafana --since=15m --tail=200 >&2 || true
  printf '%s\n' \
    'Helm test failed after a successful upgrade; the deployed release was not automatically rolled back.' >&2
  exit 1
fi

tls_attempts=0
until curl --fail --silent --show-error "https://api.${platform_domain}/health" >/dev/null \
  && curl --fail --silent --show-error "https://files.${platform_domain}/minio/health/live" >/dev/null \
  && curl --fail --silent --show-error "https://grafana.${platform_domain}/api/health" >/dev/null; do
  tls_attempts=$((tls_attempts + 1))
  if (( tls_attempts >= 30 )); then
    kubectl -n "${namespace}" get pod,ingress,certificate >&2
    printf 'public HTTPS smoke checks failed\n' >&2
    exit 1
  fi
  sleep 10
done

previous_release=""
if [[ -L "${root}/current" ]]; then
  previous_release="$(readlink -f "${root}/current")"
fi
if [[ -n "${previous_release}" && "${previous_release}" != "${release_dir}" ]]; then
  ln -sfn "${previous_release}" "${root}/previous"
fi
ln -sfn "${release_dir}" "${root}/current"

printf 'Backend production deployed: sha=%s helm_revision=%s\n' \
  "${release_sha}" "$(helm history "${helm_release}" -n "${namespace}" -o json | jq -r '.[-1].revision')"
