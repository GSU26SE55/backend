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

{{/* Image full path — vd ghcr.io/your-org/authservice:abc1234 */}}
{{- define "solar.image" -}}
{{ .Values.global.imageRegistry }}/{{ .image }}:{{ .Values.global.imageTag }}
{{- end }}

{{/* Pod security context baseline — non-root, drop capabilities */}}
{{- define "solar.podSecurityContext" -}}
runAsNonRoot: true
runAsUser: 1000
fsGroup: 1000
seccompProfile:
  type: RuntimeDefault
{{- end }}
