# DocDuck Helm Chart

Production-ready Helm chart for deploying the DocDuck stack: API, Web UI, and the scheduled Indexer.

## Features
- Modular enable/disable of API, Web, and Indexer
- Configurable Ingress (multiple hosts & path routing)
- Optional Horizontal Pod Autoscaling for API
- Secrets & ConfigMaps templated with minimal overrides needed
- Scheduled CronJob for indexing with templated `appsettings.yaml`
- Optional persistence (PVC) for local document ingestion
- Full `values.yaml` acts as a reference (richly commented)

## Quick Start
```bash
# Add / use local (example assumes local path)
helm upgrade --install docduck ./helm/docduck \
  --namespace docduck --create-namespace \
  --set ingress.hosts[0].host=docduck.local \
  --set secrets.stringData.db-connection-string="Host=postgres;Database=docduck;Username=docduck;Password=YOURPASS" \
  --set secrets.stringData.openai-api-key="sk-YOURKEY"
```

## Minimal Required Overrides
| Key | Description |
|-----|-------------|
| `secrets.stringData.db-connection-string` | PostgreSQL connection string |
| `secrets.stringData.openai-api-key` | OpenAI API key |
| `ingress.hosts` | Public hostnames for API/Web (if ingress enabled) |
| `image.registry` / `api.image.repository` etc. | Your container registry paths |

## Ingress
Configure multi-host or single host with path routing. Example single host using sub-path for API:
```yaml
ingress:
  enabled: true
  hosts:
    - host: docduck.example.com
      paths:
        - path: /api
          pathType: Prefix
          service: api
          port: http
        - path: /
          pathType: Prefix
          service: web
          port: http
  tls:
    enabled: true
    secretName: docduck-tls
```

## Persistence
If using local file ingestion inside the cluster:
```yaml
persistence:
  enabled: true
  size: 50Gi
  storageClass: nfs-storage
```
If you already have a PVC: set `persistence.existingClaim`.

## Indexer AppSettings Override
The `indexer.appsettings.content` is templated (supports `${ENV}` substitution at runtime). Disable the embedded config by setting `indexer.appsettings.enabled=false` if you mount your own.

## Security & IAM
Annotate service accounts for cloud IAM roles (e.g., AWS IRSA) using component `serviceAccount.annotations` or global `serviceAccountAnnotations`.

## Values Reference
See `values.yaml` for the exhaustive list of configurable parameters.

## CI/CD & Release Model

Images:
 1. Pushing to `main` builds multi-arch images (amd64, arm64) and tags them:
    - `ghcr.io/abrahamberg/docduck/{api,web,indexer}:latest`
    - `ghcr.io/abrahamberg/docduck/{api,web,indexer}:<derived-version>` (short SHA based if not a tag)
 2. Pushing a tag `vX.Y.Z` (e.g. `v0.3.0`) triggers the same workflow producing images with tag `X.Y.Z`.

Helm Chart:
  - Not auto-published on image build. Release manually with the `Release Helm Chart` workflow dispatch supplying:
    - `version`: chart semver (no leading `v`)
    - `appVersion`: image tag (e.g. `0.3.0`)
  - Chart is pushed to OCI: `oci://ghcr.io/abrahamberg/charts`.

Pulling the chart:
```bash
helm registry login ghcr.io
helm pull oci://ghcr.io/abrahamberg/charts/docduck --version 0.3.0
helm install docduck oci://ghcr.io/abrahamberg/charts/docduck --version 0.3.0 \
  --set secrets.stringData.openai-api-key=sk-XXX \
  --set secrets.stringData.db-connection-string='Host=...'
```

## Typical Release Flow
1. Merge code to `main` (images build with short-sha derived version).
2. Create annotated tag `v0.3.0` and push (images build with `0.3.0`).
3. Run `Release Helm Chart` workflow with `version=0.3.0` and `appVersion=0.3.0`.
4. Consumers install/upgrade using the new chart version.

## Multi-Architecture
All images are built for `linux/amd64` and `linux/arm64` via Buildx. If you need to extend platforms adjust `PLATFORMS` in `build-and-push-images.yaml`.

## Security / Supply Chain Suggestions
- Add Cosign signing (not yet enabled).
- Add Trivy scan job before push.
- Use provenance attestations for higher assurance (SLSA level upgrades).

## Manual Overrides
If you need to deploy a custom image without releasing a chart:
```bash
helm upgrade --install docduck oci://ghcr.io/abrahamberg/charts/docduck \
  --version 0.3.0 \
  --set api.image.tag=custom-feature-sha123456 \
  --set indexer.image.tag=custom-feature-sha123456 \
  --set web.image.tag=custom-feature-sha123456
```

## Uninstall
```bash
helm uninstall docduck -n docduck
```

## Contributing
Submit PRs with chart version bump (`Chart.yaml` `version`) following semver when modifying templates or defaults.
