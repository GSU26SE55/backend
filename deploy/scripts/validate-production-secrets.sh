#!/usr/bin/env bash
set -Eeuo pipefail

secret_file="${1:?path to backend.env is required}"
monitoring_file="${2:?path to monitoring.env is required}"

[[ -r "${secret_file}" ]] || {
  printf 'backend secret file is not readable: %s\n' "${secret_file}" >&2
  exit 1
}
[[ -r "${monitoring_file}" ]] || {
  printf 'monitoring secret file is not readable: %s\n' "${monitoring_file}" >&2
  exit 1
}

required_keys=(
  POSTGRES_PASSWORD
  RabbitMQ__Password
  JwtSettings__SecretKey
  JwtSettings__Issuer
  JwtSettings__Audience
  MailJet__ApiKey
  MailJet__ApiSecret
  MailJet__FromEmail
  ObjectStorage__AccessKey
  ObjectStorage__SecretKey
  GoogleOAuth__ClientId
  GoogleOAuth__ClientSecret
  Chat__DeepSeek__ApiKey
  Chat__Ai__ApiKey
  ApiKeys__SensorIngest
  ApiKeys__EnvironmentalIngest
  ADMIN_PASSWORD
  DISCORD_WEBHOOK
  Notification__Unsubscribe__Secret
  Mqtt__Password
)

value_for() {
  local key="$1"
  local file="$2"
  sed -n "s/^${key}=//p" "${file}" | tail -n 1 | tr -d '\r'
}

for key in "${required_keys[@]}"; do
  value="$(value_for "${key}" "${secret_file}")"
  [[ -n "${value}" ]] || {
    printf 'required production secret is missing or empty: %s\n' "${key}" >&2
    exit 1
  }
  if [[ "${value}" =~ (CHANGE_ME|PLACEHOLDER|YOUR_|xxx|XXX) ]]; then
    printf 'production secret still contains a placeholder: %s\n' "${key}" >&2
    exit 1
  fi
done

jwt_secret="$(value_for 'JwtSettings__SecretKey' "${secret_file}")"
(( ${#jwt_secret} >= 32 )) || {
  printf 'JwtSettings__SecretKey must contain at least 32 characters\n' >&2
  exit 1
}

for key in ApiKeys__SensorIngest ApiKeys__EnvironmentalIngest; do
  api_key="$(value_for "${key}" "${secret_file}")"
  (( ${#api_key} >= 32 )) || {
    printf '%s must contain at least 32 characters\n' "${key}" >&2
    exit 1
  }
done

mailjet_sender="$(value_for 'MailJet__FromEmail' "${secret_file}")"
[[ "${mailjet_sender}" =~ ^[^[:space:]@]+@[^[:space:]@]+\.[^[:space:]@]+$ ]] || {
  printf 'MailJet__FromEmail must be a valid, MailJet-verified sender address\n' >&2
  exit 1
}

for key in admin-user admin-password; do
  value="$(value_for "${key}" "${monitoring_file}")"
  [[ -n "${value}" && ! "${value}" =~ (CHANGE_ME|PLACEHOLDER|YOUR_) ]] || {
    printf 'monitoring credential is missing or still a placeholder: %s\n' "${key}" >&2
    exit 1
  }
done

printf 'Production secret contracts are complete (values were not printed).\n'
