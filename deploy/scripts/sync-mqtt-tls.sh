#!/usr/bin/env bash
set -Eeuo pipefail

namespace="${K3S_NAMESPACE:-solar-prod}"
secret_name="${MQTT_TLS_SECRET_NAME:-mqtt-public-tls}"
target_dir="${MQTT_TLS_DIR:-/opt/solar-iot/secrets/mosquitto/tls}"
kubeconfig="${KUBECONFIG:-/etc/rancher/k3s/k3s.yaml}"

export KUBECONFIG="${kubeconfig}"
umask 077
temporary_directory="$(mktemp -d)"
trap 'rm -rf "${temporary_directory}"' EXIT

kubectl -n "${namespace}" get secret "${secret_name}" >/dev/null
kubectl -n "${namespace}" get secret "${secret_name}" \
  -o jsonpath='{.data.tls\.crt}' | base64 --decode > "${temporary_directory}/tls.crt"
kubectl -n "${namespace}" get secret "${secret_name}" \
  -o jsonpath='{.data.tls\.key}' | base64 --decode > "${temporary_directory}/tls.key"

openssl x509 -in "${temporary_directory}/tls.crt" -noout -checkend 86400 >/dev/null
openssl pkey -in "${temporary_directory}/tls.key" -pubout -out "${temporary_directory}/key.pub" >/dev/null
openssl x509 -in "${temporary_directory}/tls.crt" -pubkey -noout > "${temporary_directory}/cert.pub"
cmp "${temporary_directory}/key.pub" "${temporary_directory}/cert.pub"

install -d -o root -g solar-runtime -m 0750 "${target_dir}"

changed=true
if [[ -f "${target_dir}/tls.crt" && -f "${target_dir}/tls.key" ]] \
  && cmp -s "${temporary_directory}/tls.crt" "${target_dir}/tls.crt" \
  && cmp -s "${temporary_directory}/tls.key" "${target_dir}/tls.key"; then
  changed=false
fi

if [[ "${changed}" == true ]]; then
  install -o root -g solar-runtime -m 0640 \
    "${temporary_directory}/tls.crt" "${target_dir}/.tls.crt.new"
  install -o root -g solar-runtime -m 0640 \
    "${temporary_directory}/tls.key" "${target_dir}/.tls.key.new"
  mv -f "${target_dir}/.tls.crt.new" "${target_dir}/tls.crt"
  mv -f "${target_dir}/.tls.key.new" "${target_dir}/tls.key"

  if docker inspect solar-iot-mosquitto >/dev/null 2>&1; then
    docker kill --signal HUP solar-iot-mosquitto >/dev/null
  fi
  printf 'MQTT TLS material updated and broker reload requested.\n'
else
  printf 'MQTT TLS material is already current.\n'
fi
