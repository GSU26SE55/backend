#!/usr/bin/env bash
set -Eeuo pipefail

test_root="$(mktemp -d /tmp/solar-registry-auth-test.XXXXXX)"
cleanup() {
  rm -rf "${test_root}"
}
trap cleanup EXIT

payload_root="${test_root}/payload"
mock_bin="${test_root}/bin"
platform_root="${test_root}/platform"
call_log="${test_root}/cosign-calls"

mkdir -p \
  "${payload_root}/deploy/scripts" \
  "${payload_root}/deploy/production" \
  "${platform_root}/config" \
  "${platform_root}/secrets" \
  "${platform_root}/geoip" \
  "${mock_bin}"

cp deploy/scripts/deploy-production.sh \
  "${payload_root}/deploy/scripts/deploy-production.sh"
chmod 0555 "${payload_root}/deploy/scripts/deploy-production.sh"

cat > "${payload_root}/deploy/scripts/preflight-production.sh" <<'PREFLIGHT'
#!/usr/bin/env bash
set -Eeuo pipefail
exit 0
PREFLIGHT
chmod 0555 "${payload_root}/deploy/scripts/preflight-production.sh"

cat > "${platform_root}/config/host.env" <<HOST_ENV
KUBECONFIG=${test_root}/kubeconfig
K3S_NAMESPACE=solar-prod
HOST_ENV

digest='sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa'
cat > "${payload_root}/deploy/production/image-lock.env" <<IMAGE_LOCK
APIGATEWAY_DIGEST=${digest}
AUTHSERVICE_DIGEST=${digest}
EMAILSERVICE_DIGEST=${digest}
SMSSERVICE_DIGEST=${digest}
FILESTORAGESERVICE_DIGEST=${digest}
BATTERYSERVICE_DIGEST=${digest}
TICKETSERVICE_DIGEST=${digest}
NOTIFICATIONSERVICE_DIGEST=${digest}
AUDITAGGREGATORSERVICE_DIGEST=${digest}
IMAGE_LOCK

docker_config_json='{"auths":{"ghcr.io":{"auth":"test-only"}}}'
docker_config_base64="$(printf '%s' "${docker_config_json}" | base64 | tr -d '\n')"

cat > "${mock_bin}/kubectl" <<'KUBECTL'
#!/usr/bin/env bash
set -Eeuo pipefail

for argument in "$@"; do
  case "${argument}" in
    'jsonpath={.type}')
      printf 'kubernetes.io/dockerconfigjson'
      exit 0
      ;;
    'jsonpath={.data.\.dockerconfigjson}')
      printf '%s' "${REGISTRY_AUTH_TEST_DOCKER_CONFIG}"
      exit 0
      ;;
  esac
done

printf 'unexpected kubectl invocation: %s\n' "$*" >&2
exit 1
KUBECTL
chmod 0555 "${mock_bin}/kubectl"

cat > "${mock_bin}/cosign" <<'COSIGN'
#!/usr/bin/env bash
set -Eeuo pipefail

[[ "${1:-}" == 'verify' ]]
[[ -n "${DOCKER_CONFIG:-}" ]]
config_file="${DOCKER_CONFIG}/config.json"
[[ -s "${config_file}" ]]
[[ "$(stat -c '%a' "${config_file}")" == '600' ]]
jq -e '.auths["ghcr.io"].auth == "test-only"' "${config_file}" >/dev/null
printf '%s\n' "${DOCKER_CONFIG}" >> "${REGISTRY_AUTH_TEST_CALL_LOG}"
COSIGN
chmod 0555 "${mock_bin}/cosign"

export REGISTRY_AUTH_TEST_DOCKER_CONFIG="${docker_config_base64}"
export REGISTRY_AUTH_TEST_CALL_LOG="${call_log}"

set +e
PATH="${mock_bin}:${PATH}" \
SOLAR_PLATFORM_ROOT="${platform_root}" \
  "${payload_root}/deploy/scripts/deploy-production.sh" \
    'bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb' \
    > "${test_root}/deploy-output" 2>&1
deploy_status=$?
set -e

[[ "${deploy_status}" -ne 0 ]]
grep -Fq 'host.env is incomplete' "${test_root}/deploy-output"
[[ "$(wc -l < "${call_log}" | tr -d ' ')" == '9' ]]

registry_config="$(head -n 1 "${call_log}")"
[[ -n "${registry_config}" ]]
[[ "$(sort -u "${call_log}" | wc -l | tr -d ' ')" == '1' ]]
[[ ! -e "${registry_config}" ]]

echo 'DEPLOY_REGISTRY_AUTH_CONTRACT_OK'
