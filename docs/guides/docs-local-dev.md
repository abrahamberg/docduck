# Documentation Local Development

DocDuck uses [MkDocs Material] for technical documentation.

## Install
Assumes Python 3.11+.

```bash
pip install mkdocs-material
```

(Optionally pin versions in a `requirements-docs.txt` file.)

## Serve
From repo root:
```bash
mkdocs serve
```
Navigate to http://127.0.0.1:8000 — changes auto-reload.

## Build Static Site
```bash
mkdocs build
```
Outputs to `site/`. Publish with any static host.

## Adding Pages
1. Create markdown under `docs/` (or `docs/providers/` for provider pages).
2. Update `mkdocs.yml` nav if you want it visible.

## Style Guidelines
- Keep headings concise
- Use admonitions for warnings: `!!! warning`
- Cross-link related concepts (e.g., provider setup -> authentication guide)

## Future Enhancements
- Add search indexing plugin (Material includes built-in search)
- CI job to build and publish documentation
