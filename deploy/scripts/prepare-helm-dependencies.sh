#!/usr/bin/env bash
set -Eeuo pipefail

chart_directory="${1:?Usage: prepare-helm-dependencies.sh <chart-directory>}"
max_attempts="${HELM_DEPENDENCY_MAX_ATTEMPTS:-4}"
retry_delay_seconds="${HELM_DEPENDENCY_RETRY_DELAY_SECONDS:-5}"

[[ -r "${chart_directory}/Chart.yaml" ]] || {
  printf 'Helm chart is not readable: %s/Chart.yaml\n' "${chart_directory}" >&2
  exit 1
}

case "${max_attempts}" in
  '' | *[!0-9]*)
    printf 'HELM_DEPENDENCY_MAX_ATTEMPTS must be a positive integer\n' >&2
    exit 1
    ;;
esac

case "${retry_delay_seconds}" in
  '' | *[!0-9]*)
    printf 'HELM_DEPENDENCY_RETRY_DELAY_SECONDS must be a non-negative integer\n' >&2
    exit 1
    ;;
esac

((max_attempts >= 1)) || {
  printf 'HELM_DEPENDENCY_MAX_ATTEMPTS must be at least 1\n' >&2
  exit 1
}

command -v helm >/dev/null 2>&1 || {
  printf 'Required command is missing: helm\n' >&2
  exit 1
}

retry_helm_command() {
  local description="$1"
  shift

  local attempt=1
  local current_delay="${retry_delay_seconds}"
  local status

  while :; do
    if "$@"; then
      return 0
    else
      status=$?
    fi

    if ((attempt >= max_attempts)); then
      printf '%s failed after %d attempts (exit %d)\n' \
        "${description}" "${attempt}" "${status}" >&2
      return "${status}"
    fi

    printf '%s failed on attempt %d/%d (exit %d); retrying in %ds\n' \
      "${description}" "${attempt}" "${max_attempts}" "${status}" \
      "${current_delay}" >&2
    sleep "${current_delay}"

    attempt=$((attempt + 1))
    current_delay=$((current_delay * 2))
  done
}

retry_helm_command \
  'Adding the prometheus-community Helm repository' \
  helm repo add prometheus-community \
  https://prometheus-community.github.io/helm-charts \
  --force-update

retry_helm_command \
  'Adding the Grafana Helm repository' \
  helm repo add grafana \
  https://grafana.github.io/helm-charts \
  --force-update

retry_helm_command \
  'Updating Helm repository indexes' \
  helm repo update

retry_helm_command \
  "Building Helm dependencies for ${chart_directory}" \
  helm dependency build "${chart_directory}"
