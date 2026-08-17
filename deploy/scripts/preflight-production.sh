#!/usr/bin/env bash
set -Eeuo pipefail

root="${SOLAR_PLATFORM_ROOT:-/opt/solar-platform}"
script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
host_env="${root}/config/host.env"
backend_env="${root}/secrets/backend.env"
monitoring_env="${root}/secrets/monitoring.env"
geoip_db="${root}/geoip/GeoLite2-City.mmdb"

for tool in docker kubectl helm curl openssl base64 getent jq cosign ip grpcurl systemctl; do
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

validate_geoip_database() {
  local database_path="$1"
  local file_age_seconds
  local file_mode
  local file_size
  local other_permissions

  [[ -f "${database_path}" && -r "${database_path}" && -s "${database_path}" ]] || {
    printf 'GeoLite2 City database is missing, unreadable or empty: %s\n' \
      "${database_path}" >&2
    return 1
  }

  file_size="$(stat -c '%s' "${database_path}")"
  (( file_size >= 1048576 )) || {
    printf 'GeoLite2 City database is unexpectedly small (%s bytes): %s\n' \
      "${file_size}" "${database_path}" >&2
    return 1
  }

  # Do not use grep -q in a pipe while pipefail is enabled: an early grep exit
  # can SIGPIPE tail and turn a valid database into a false-negative preflight.
  LC_ALL=C grep -aF 'MaxMind.com' \
    < <(tail -c 131072 "${database_path}") >/dev/null || {
    printf 'GeoLite2 City database is missing the MaxMind metadata marker: %s\n' \
      "${database_path}" >&2
    return 1
  }

  # The container runs as uid/gid 10001. GeoLite2 contains no secret material,
  # so the host copy is deliberately 0644 and all parent directories 0755.
  file_mode="$(stat -c '%a' "${database_path}")"
  other_permissions="${file_mode: -1}"
  (( (10#${other_permissions} & 4) != 0 )) || {
    printf 'GeoLite2 City database must be readable by the non-root container (mode is %s): %s\n' \
      "${file_mode}" "${database_path}" >&2
    return 1
  }

  file_age_seconds="$(( $(date +%s) - $(stat -c '%Y' "${database_path}") ))"
  (( file_age_seconds >= 0 && file_age_seconds <= 3888000 )) || {
    printf 'GeoLite2 City database is older than 45 days or has a future timestamp: %s\n' \
      "${database_path}" >&2
    return 1
  }
}

validate_geoip_database "${geoip_db}"

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
AI_WIREGUARD_IPV4="$(read_env AI_WIREGUARD_IPV4)"
PLATFORM_WIREGUARD_IPV4="$(read_env PLATFORM_WIREGUARD_IPV4)"
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
: "${AI_WIREGUARD_IPV4:?AI_WIREGUARD_IPV4 is required}"
: "${PLATFORM_WIREGUARD_IPV4:?PLATFORM_WIREGUARD_IPV4 is required}"

for wireguard_ipv4 in "${AI_WIREGUARD_IPV4}" "${PLATFORM_WIREGUARD_IPV4}"; do
  [[ "${wireguard_ipv4}" =~ ^([0-9]{1,3}\.){3}[0-9]{1,3}$ ]] || {
    printf 'WireGuard IPv4 is malformed: %s\n' "${wireguard_ipv4}" >&2
    exit 1
  }
done
[[ "${AI_WIREGUARD_IPV4}" != "${PLATFORM_WIREGUARD_IPV4}" ]] || {
  printf 'AI and platform WireGuard addresses must be different\n' >&2
  exit 1
}

ip -4 -o address show dev wg0 |
  awk '{print $4}' | cut -d/ -f1 | grep -Fxq "${PLATFORM_WIREGUARD_IPV4}" || {
    printf 'PLATFORM_WIREGUARD_IPV4 is not assigned to wg0: %s\n' \
      "${PLATFORM_WIREGUARD_IPV4}" >&2
    exit 1
  }
ip -4 route get "${AI_WIREGUARD_IPV4}" | grep -Eq '(^|[[:space:]])dev wg0([[:space:]]|$)' || {
  printf 'AI WireGuard address is not routed through wg0: %s\n' \
    "${AI_WIREGUARD_IPV4}" >&2
  exit 1
}

# The deploy account intentionally has no CAP_NET_ADMIN, so it cannot query
# handshake timestamps with `wg show`. The /32 route above and the bounded
# HTTPS plus gRPC probes below prove that the configured peer, encrypted data
# path, TLS/SNI routing and AI application are all usable now. The first probe
# also initiates a fresh WireGuard handshake when an otherwise healthy tunnel
# has been idle.

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

"${script_dir}/validate-production-secrets.sh" \
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
curl --fail --silent --show-error --connect-timeout 5 --max-time 10 \
  "${AI_HTTP_BASE_URL}/ready" |
  jq -e '.ready == true' >/dev/null || {
    printf 'AI HTTPS readiness check failed: %s/ready\n' "${AI_HTTP_BASE_URL}" >&2
    exit 1
  }

# Resolve the public certificate name directly to the WireGuard peer. This
# proves HTTPS fallback uses wg0 while still validating the public TLS chain.
curl --fail --silent --show-error --connect-timeout 5 --max-time 10 \
  --resolve "${ai_host}:443:${AI_WIREGUARD_IPV4}" \
  "${AI_HTTP_BASE_URL}/ready" |
  jq -e '.ready == true' >/dev/null || {
    printf 'AI HTTPS readiness over WireGuard failed: %s -> %s\n' \
      "${ai_host}" "${AI_WIREGUARD_IPV4}" >&2
    exit 1
  }

# The AI server implements the standard grpc.health.v1 contract. -authority
# preserves both HTTP/2 :authority and TLS certificate verification while the
# socket connects to the private WireGuard address.
grpcurl \
  -max-time 10 \
  -authority "${ai_host}" \
  -import-path "${script_dir}/../contracts" \
  -proto grpc_health_v1.proto \
  -d '{"service":"aimodule.v1.AiService"}' \
  "${AI_WIREGUARD_IPV4}:443" \
  grpc.health.v1.Health/Check |
  jq -e '.status == "SERVING"' >/dev/null || {
    printf 'AI gRPC health over WireGuard failed: %s -> %s\n' \
      "${ai_host}" "${AI_WIREGUARD_IPV4}" >&2
    exit 1
  }

# Standard health proves the gRPC transport. The application Health RPC also
# proves that Caddy selected the real AI service and the production model set is
# usable, rather than only reaching a generic health implementation.
grpcurl \
  -max-time 15 \
  -authority "${ai_host}" \
  -import-path "${script_dir}/../contracts" \
  -proto ai_health_v1.proto \
  -d '{}' \
  "${AI_WIREGUARD_IPV4}:443" \
  aimodule.v1.AiService/Health |
  jq -e '
    .status == "ok"
    and .scalerLoaded == true
    and .mambaLoaded == true
    and .isolationForestLoaded == true
    and .lfpLoaded == true
  ' >/dev/null || {
    printf 'AI application gRPC health/model contract failed over WireGuard\n' >&2
    exit 1
  }

systemctl is-active --quiet solar-loki-wireguard.service || {
  printf 'solar-loki-wireguard.service is not active\n' >&2
  exit 1
}
curl --fail --silent --show-error \
  "http://${PLATFORM_WIREGUARD_IPV4}:3100/ready" >/dev/null || {
    printf 'Loki is not reachable on the platform WireGuard bridge\n' >&2
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
