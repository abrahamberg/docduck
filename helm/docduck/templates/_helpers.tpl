{{/*
Common template helpers for DocDuck chart
*/}}

{{- define "docduck.name" -}}
{{- default .Chart.Name .Values.global.nameOverride | trunc 63 | trimSuffix "-" -}}
{{- end -}}

{{- define "docduck.fullname" -}}
{{- if .Values.global.fullnameOverride -}}
{{- .Values.global.fullnameOverride | trunc 63 | trimSuffix "-" -}}
{{- else -}}
{{- $name := default .Chart.Name .Values.global.nameOverride -}}
{{- if contains $name .Release.Name -}}
{{- .Release.Name | trunc 63 | trimSuffix "-" -}}
{{- else -}}
{{- printf "%s-%s" .Release.Name $name | trunc 63 | trimSuffix "-" -}}
{{- end -}}
{{- end -}}
{{- end -}}

{{- define "docduck.chart" -}}
{{- printf "%s-%s" .Chart.Name .Chart.Version | replace "+" "_" -}}
{{- end -}}

{{- define "docduck.selectorLabels" -}}
app.kubernetes.io/name: {{ include "docduck.name" . }}
app.kubernetes.io/instance: {{ .Release.Name }}
{{- end -}}

{{- define "docduck.labels" -}}
helm.sh/chart: {{ include "docduck.chart" . }}
{{ include "docduck.selectorLabels" . }}
app.kubernetes.io/managed-by: {{ .Release.Service }}
app.kubernetes.io/version: {{ .Chart.AppVersion | quote }}
{{- range $k, $v := .Values.global.extraLabels }}
{{ $k }}: {{ $v | quote }}
{{- end }}
{{- end -}}

{{- define "docduck.image" -}}
{{- $registry := default .Values.image.registry .Values.global.imageRegistry -}}
{{- $image := printf "%s/%s:%s" $registry .Values.image.repository .Values.image.tag -}}
{{- $image -}}
{{- end -}}

{{- define "docduck.componentImage" -}}
{{- $globalRegistry := .Values.global.imageRegistry -}}
{{- with .component }}
{{- $r := default $globalRegistry .image.registry -}}
{{- printf "%s/%s:%s" $r .image.repository .image.tag -}}
{{- end -}}
{{- end -}}

{{- define "docduck.secretName" -}}
{{- if .Values.secrets.create -}}
{{- default (printf "%s-secrets" (include "docduck.fullname" .)) .Values.secrets.name -}}
{{- else -}}
{{- .Values.secrets.name -}}
{{- end -}}
{{- end -}}

{{- define "docduck.configName" -}}
{{- if .Values.config.enabled -}}
{{- default (printf "%s-config" (include "docduck.fullname" .)) .Values.config.name -}}
{{- else -}}
{{- .Values.config.name -}}
{{- end -}}
{{- end -}}

{{- define "docduck.commonEnv" -}}
- name: DB_CONNECTION_STRING
  valueFrom:
    secretKeyRef:
      name: {{ include "docduck.secretName" . }}
      key: db-connection-string
- name: OPENAI_API_KEY
  valueFrom:
    secretKeyRef:
      name: {{ include "docduck.secretName" . }}
      key: openai-api-key
{{- end -}}

{{/* Render arbitrary values as templates (like include) */}}
{{- define "tplvalues.render" -}}
{{- if typeIs "string" .value -}}
{{- tpl .value .context -}}
{{- else -}}
{{- tpl (.value | toYaml) .context -}}
{{- end -}}
{{- end -}}
