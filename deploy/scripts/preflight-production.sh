#!/usr/bin/env bash
set -Eeuo pipefail

root="${SOLAR_PLATFORM_ROOT:-/opt/solar-platform}"
host_env="${root}/config/host.env"
backend_env="${root}/secrets/backend.env"
monitoring_env="${root}/secrets/monitoring.env"

for tool in docker kubectl helm curl openssl base64 getent jq cosign ip; do
  command -v "${tool}" >/dev/null 2>&1 || {
    printf 'missing required production tool: %s\n' "${tool}" >&2
    exit 1
  }
done

[[ -r "${host_env}" ]] || {
  printf 'host configuration is not readable: %s\n' "${host_env}" >&2
  exit 1
}
[[ -r "${root}/config/cosign.pub" ]] || {
  printf 'Cosign public key is not readable: %s/config/cosign.pub\n' "${root}" >&2
  exit 1
}
openssl pkey -pubin -in "${root}/config/cosign.pub" -noout

read_env() {
  local key="$1"
  sed -n "s/^${key}=//p" "${host_env}" | tail -n 1 | tr -d '\r'
}

PLATFORM_PUBLIC_DOMAIN="$(read_env PLATFORM_PUBLIC_DOMAIN)"
PLATFORM_PUBLIC_IPV4="$(read_env PLATFORM_PUBLIC_IPV4)"
FRONTEND_PUBLIC_ORIGIN="$(read_env FRONTEND_PUBLIC_ORIGIN)"
AI_GRPC_ADDRESS="$(read_env AI_GRPC_ADDRESS)"
AI_HTTP_BASE_URL="$(read_env AI_HTTP_BASE_URL)"
MQTT_NODE_IP="$(read_env MQTT_NODE_IP)"
MQTT_AUTH_DIR="$(read_env MQTT_AUTH_DIR)"
PLATFORM_PRIVATE_IPV4="$(read_env PLATFORM_PRIVATE_IPV4)"
ACME_EMAIL="$(read_env ACME_EMAIL)"
KUBECONFIG="$(read_env KUBECONFIG)"
K3S_NAMESPACE="$(read_env K3S_NAMESPACE)"
export KUBECONFIG

: "${PLATFORM_PUBLIC_DOMAIN:?PLATFORM_PUBLIC_DOMAIN is required}"
: "${PLATFORM_PUBLIC_IPV4:?PLATFORM_PUBLIC_IPV4 is required}"
: "${FRONTEND_PUBLIC_ORIGIN:?FRONTEND_PUBLIC_ORIGIN is required}"
: "${AI_GRPC_ADDRESS:?AI_GRPC_ADDRESS is required}"
: "${AI_HTTP_BASE_URL:?AI_HTTP_BASE_URL is required}"
: "${MQTT_NODE_IP:?MQTT_NODE_IP is required}"
: "${MQTT_AUTH_DIR:?MQTT_AUTH_DIR is required}"
: "${PLATFORM_PRIVATE_IPV4:?PLATFORM_PRIVATE_IPV4 is required}"
: "${ACME_EMAIL:?ACME_EMAIL is required}"
: "${KUBECONFIG:?KUBECONFIG is required}"
: "${K3S_NAMESPACE:?K3S_NAMESPACE is required}"

[[ "${FRONTEND_PUBLIC_ORIGIN}" == "https://${PLATFORM_PUBLIC_DOMAIN}" ]] || {
  printf 'FRONTEND_PUBLIC_ORIGIN must equal https://%s (no path or trailing slash)\n' \
    "${PLATFORM_PUBLIC_DOMAIN}" >&2
  exit 1
}
for ai_url in "${AI_GRPC_ADDRESS}" "${AI_HTTP_BASE_URL}"; do
  [[ "${ai_url}" =~ ^https://[^/:[:space:]]+(:443)?$ ]] || {
    printf 'AI endpoints must be HTTPS origins only (do not append /docs or another path): %s\n' \
      "${ai_url}" >&2
    exit 1
  }
done
[[ "${AI_GRPC_ADDRESS}" == "${AI_HTTP_BASE_URL}" ]] || {
  printf 'AI_GRPC_ADDRESS and AI_HTTP_BASE_URL must share the public AI origin\n' >&2
  exit 1
}

[[ "${MQTT_NODE_IP}" == "${PLATFORM_PRIVATE_IPV4}" ]] || {
  printf 'MQTT_NODE_IP must equal PLATFORM_PRIVATE_IPV4 on the shared backend + IoT VPS\n' >&2
  exit 1
}
ip -4 -o address show | awk '{print $4}' | cut -d/ -f1 | grep -Fxq "${PLATFORM_PRIVATE_IPV4}" || {
  printf 'PLATFORM_PRIVATE_IPV4 is not assigned to this VPS: %s\n' \
    "${PLATFORM_PRIVATE_IPV4}" >&2
  exit 1
}
[[ "${ACME_EMAIL}" =~ ^[^[:space:]@]+@[^[:space:]@]+[.][^[:space:]@]+$ ]] || {
  printf 'ACME_EMAIL is not a valid operational email address\n' >&2
  exit 1
}
[[ -d "${MQTT_AUTH_DIR}" && -w "${MQTT_AUTH_DIR}" ]] || {
  printf 'MQTT auth directory is missing or not writable: %s\n' "${MQTT_AUTH_DIR}" >&2
  exit 1
}

"$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)/validate-production-secrets.sh" \
  "${backend_env}" "${monitoring_env}"

docker info >/dev/null
kubectl version --client >/dev/null
kubectl get node >/dev/null
kubectl wait --for=condition=Ready node --all --timeout=2m >/dev/null
kubectl get crd certificates.cert-manager.io >/dev/null
kubectl wait --for=condition=Ready clusterissuer/letsencrypt-prod --timeout=2m >/dev/null
helm version >/dev/null

for host in \
  "api.${PLATFORM_PUBLIC_DOMAIN}" \
  "files.${PLATFORM_PUBLIC_DOMAIN}" \
  "mqtt.${PLATFORM_PUBLIC_DOMAIN}" \
  "grafana.${PLATFORM_PUBLIC_DOMAIN}"; do
  resolved="$(getent ahostsv4 "${host}" | awk 'NR == 1 {print $1}')"
  [[ "${resolved}" == "${PLATFORM_PUBLIC_IPV4}" ]] || {
    printf 'DNS mismatch: %s resolves to %s, expected %s\n' \
      "${host}" "${resolved:-nothing}" "${PLATFORM_PUBLIC_IPV4}" >&2
    exit 1
  }
done

ai_host="${AI_HTTP_BASE_URL#https://}"
ai_host="${ai_host%:443}"
getent ahostsv4 "${ai_host}" >/dev/null || {
  printf 'AI hostname does not resolve from the platform VPS: %s\n' "${ai_host}" >&2
  exit 1
}
curl --fail --silent --show-error "${AI_HTTP_BASE_URL}/ready" |
  jq -e '.ready == true' >/dev/null || {
    printf 'AI HTTPS readiness check failed: %s/ready\n' "${AI_HTTP_BASE_URL}" >&2
    exit 1
  }

available_kib="$(df -Pk "${root}" | awk 'NR == 2 {print $4}')"
(( available_kib >= 31457280 )) || {
  printf 'less than 30 GiB free disk remains on the production VPS\n' >&2
  exit 1
}

available_memory_kib="$(awk '/MemAvailable:/ {print $2}' /proc/meminfo)"
(( available_memory_kib >= 2097152 )) || {
  printf 'less than 2 GiB memory is currently available on the production VPS\n' >&2
  exit 1
}

printf 'Backend production preflight passed.\n'
