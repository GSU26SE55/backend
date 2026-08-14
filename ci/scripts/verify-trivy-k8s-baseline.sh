#!/usr/bin/env bash
set -Eeuo pipefail

report_path="${1:-trivy-k8s-misconfig.json}"
baseline_path="${2:-ci/security/trivy-k8s-baseline.env}"

for tool in jq sha256sum awk wc sort
do
  command -v "${tool}" >/dev/null 2>&1 || {
    echo "Missing required baseline verification tool: ${tool}" >&2
    exit 1
  }
done

test -s "${report_path}" || {
  echo "Trivy Kubernetes report is missing or empty: ${report_path}" >&2
  exit 1
}

test -s "${baseline_path}" || {
  echo "Trivy Kubernetes baseline is missing or empty: ${baseline_path}" >&2
  exit 1
}

# This file is repository-controlled and contains only two validated scalar values.
# shellcheck disable=SC1090
source "${baseline_path}"

case "${TRIVY_K8S_FINDING_COUNT:-}" in
  ''|*[!0-9]*)
    echo 'TRIVY_K8S_FINDING_COUNT must be a non-negative integer' >&2
    exit 1
    ;;
esac

if [[ ! "${TRIVY_K8S_FINDING_SHA256:-}" =~ ^[0-9a-f]{64}$ ]]
then
  echo 'TRIVY_K8S_FINDING_SHA256 must contain 64 lowercase hex characters' >&2
  exit 1
fi

canonical_findings="$(mktemp)"
trap 'rm -f "${canonical_findings}"' EXIT

jq -c '
  .Results[] |
  (.Misconfigurations // [])[] |
  select(.Severity == "HIGH" or .Severity == "CRITICAL") |
  [.Severity, .ID, .Title, .Message]
' "${report_path}" |
  LC_ALL=C sort > "${canonical_findings}"

actual_count="$(wc -l < "${canonical_findings}" | awk '{print $1}')"
actual_sha256="$(sha256sum "${canonical_findings}" | awk '{print $1}')"

if [[ "${actual_count}" != "${TRIVY_K8S_FINDING_COUNT}" ]] ||
   [[ "${actual_sha256}" != "${TRIVY_K8S_FINDING_SHA256}" ]]
then
  echo 'Kubernetes security findings differ from the reviewed baseline.' >&2
  echo "Expected count: ${TRIVY_K8S_FINDING_COUNT}" >&2
  echo "Actual count:   ${actual_count}" >&2
  echo "Expected hash:  ${TRIVY_K8S_FINDING_SHA256}" >&2
  echo "Actual hash:    ${actual_sha256}" >&2
  echo 'Current canonical HIGH/CRITICAL findings:' >&2
  cat "${canonical_findings}" >&2
  exit 1
fi

echo "KUBERNETES_SECURITY_BASELINE_OK count=${actual_count} sha256=${actual_sha256}"
