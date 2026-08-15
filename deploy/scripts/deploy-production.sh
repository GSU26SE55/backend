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
mqtt_node_ip="$(read_env MQTT_NODE_IP "${host_env}")"
mqtt_auth_dir="$(read_env MQTT_AUTH_DIR "${host_env}")"
kubeconfig="$(read_env KUBECONFIG "${host_env}")"
namespace="$(read_env K3S_NAMESPACE "${host_env}")"
helm_release="$(read_env HELM_RELEASE "${host_env}")"

[[ -n "${platform_domain}" && -n "${frontend_origin}" && -n "${ai_grpc_address}" \
  && -n "${ai_http_base_url}" && -n "${mqtt_node_ip}" && -n "${mqtt_auth_dir}" \
  && -n "${kubeconfig}" && -n "${namespace}" && -n "${helm_release}" ]] || {
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

if kubectl -n "${namespace}" get cronjob postgres-backup >/dev/null 2>&1; then
  backup_job="postgres-predeploy-${release_sha:0:12}"
  kubectl -n "${namespace}" delete job "${backup_job}" --ignore-not-found >/dev/null
  kubectl -n "${namespace}" create job "${backup_job}" --from=cronjob/postgres-backup
  if ! kubectl -n "${namespace}" wait \
    --for=condition=complete "job/${backup_job}" --timeout=15m; then
    kubectl -n "${namespace}" logs "job/${backup_job}" --all-containers=true >&2 || true
    printf 'pre-deployment database backup failed\n' >&2
    exit 1
  fi
fi

helm_value_args=(
  -f "${chart_dir}/values.yaml"
  -f "${chart_dir}/values-vps-small.yaml"
  -f "${chart_dir}/values-production.yaml"
  --set-string "global.domain=${platform_domain}"
  --set-string "global.frontendOrigin=${frontend_origin}"
  --set-string "config.Ai__GrpcAddress=${ai_grpc_address}"
  --set-string "config.Ai__HttpBaseUrl=${ai_http_base_url}"
  --set-string "config.TicketAi__AiGrpcAddress=${ai_grpc_address}"
  --set-string "iot.mqttNodeIp=${mqtt_node_ip}"
  --set-string "iot.mqttPasswordSync.hostPath=${mqtt_auth_dir}"
)

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
cleanup_rendered_manifest() {
  rm -f "${rendered_manifest}"
}
trap cleanup_rendered_manifest EXIT

helm template "${helm_release}" "${chart_dir}" \
  --namespace "${namespace}" \
  "${helm_value_args[@]}" \
  > "${rendered_manifest}"

"${script_dir}/verify-monitoring-resource-policy.sh" "${rendered_manifest}"

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
  kubectl -n "${namespace}" rollout status "${resource}" --timeout=10m
done < <(kubectl -n "${namespace}" get deployment,statefulset,daemonset -o name)

helm test "${helm_release}" --namespace "${namespace}" --logs --timeout 5m

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
