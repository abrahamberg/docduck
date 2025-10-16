# Release & Versioning

## Versioning Strategy
- Semantic-ish: MAJOR (breaking schema/API), MINOR (additive features), PATCH (fixes)
- Pre-1.0: minor bumps may still introduce changes; document in changelog

## Release Steps
1. Update `CHANGELOG` (planned future migration from legacy file)
2. Ensure docs updated (README + relevant pages)
3. Tag commit: `git tag -a vX.Y.Z -m "Release vX.Y.Z"`
4. Push tags: `git push --tags`
5. Build & publish container images: `docduck-api`, `docduck-indexer`
6. Announce release notes (GitHub Release)

## Containers
Recommended naming:
- `ghcr.io/<org>/docduck-api:vX.Y.Z`
- `ghcr.io/<org>/docduck-indexer:vX.Y.Z`

## Schema Changes
- Provide SQL migration diff script
- Advise `FORCE_FULL_REINDEX=true` if embedding dimension changes

## Changelog Format (Keep Simple)
```
## [1.2.0] - 2025-10-16
### Added
- New S3 provider features
### Fixed
- Skipped chunk overlap bug
```

## Security Releases
- Note CVE / issue, impacted versions, mitigation steps

## Deprecations
- Announce in previous minor version, remove next minor

## Automation (Future)
- GitHub Actions for build/test/publish
- Automated docs site publish (MkDocs GH Pages)

## Next
- Coding style: [Coding Standards](coding-standards.md)
