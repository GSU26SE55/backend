{{/* Helper template — function dùng chung cho mọi resource */}}

{{/* Standard labels — Kubernetes recommended set */}}
{{- define "solar.labels" -}}
app.kubernetes.io/name: solar-battery
app.kubernetes.io/instance: {{ .Release.Name }}
app.kubernetes.io/version: {{ .Chart.AppVersion | quote }}
app.kubernetes.io/managed-by: {{ .Release.Service }}
helm.sh/chart: {{ printf "%s-%s" .Chart.Name .Chart.Version | replace "+" "_" }}
{{- end }}

{{/* Service-specific labels — thêm vào Pod để Service selector match */}}
{{- define "solar.serviceLabels" -}}
{{ include "solar.labels" . }}
app.kubernetes.io/component: {{ .component }}
{{- end }}

{{/*
Image full path.

Production passes an immutable repository digest for every service. Development and
staging may still use a tag. Keeping the choice in one helper prevents one Deployment
from accidentally falling back to a mutable tag.
*/}}
{{- define "solar.image" -}}
{{- if .digest -}}
{{ .Values.global.appImageRegistry }}/{{ .image }}@{{ .digest }}
{{- else -}}
{{ .Values.global.appImageRegistry }}/{{ .image }}:{{ .Values.global.imageTag }}
{{- end -}}
{{- end }}

{{/* Pod security context baseline — non-root, drop capabilities */}}
{{- define "solar.podSecurityContext" -}}
runAsNonRoot: true
runAsUser: 10001
runAsGroup: 10001
fsGroup: 10001
fsGroupChangePolicy: OnRootMismatch
seccompProfile:
  type: RuntimeDefault
{{- end }}

{{/* Container security baseline for every ASP.NET workload. */}}
{{- define "solar.containerSecurityContext" -}}
allowPrivilegeEscalation: false
readOnlyRootFilesystem: false
capabilities:
  drop: ["ALL"]
{{- end }}
