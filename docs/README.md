# DocDuck Documentation

This folder contains all project documentation. The repository root keeps only a concise `README.md`—everything else lives here for clarity and long-term maintenance.

## Guides

- Getting started: `guides/quickstart.md`
- Authentication setup: `guides/authentication.md`
- Developer Guide (local dev, conventions): `guides/developer-guide.md`

## Database

- PostgreSQL + pgvector guide: `database/pgvector.md`
- Quick reference: `database/pgvector-quickref.md`

## Architecture

- System overview and design: `architecture.md`

## Release notes

- Changelog: `changelog.md`

## Reports (generated, ephemeral, or implementation summaries)

- API implementation summary: `reports/api-implementation.md`
- pgvector implementation summary: `reports/pgvector-implementation.md`

Notes:
- Reports are useful for historical context or PR review, but prefer updating canonical guides to avoid duplication.
- If a report stops providing long-term value, feel free to delete it after merging knowledge into the relevant guide.
