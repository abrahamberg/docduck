# Local Filesystem Provider Setup

The local provider indexes documents from a directory on the host filesystem. Useful for development, testing, or on-prem ingestion of shared folders.

## Overview
DocDuck scans a root path and optionally recurses into subdirectories, filtering by configured file extensions and exclusion patterns.

## Configuration
Example (`appsettings.yaml`):
```yaml
Providers:
  Local:
    Enabled: true
    Name: "LocalFiles"
    RootPath: "/data/documents"
    Recursive: true
    FileExtensions:
      - ".docx"
      - ".pdf"
      - ".txt"
    ExcludePatterns:
      - ".git"
      - "node_modules"
      - "__pycache__"
```
Environment variable expansion applies if you embed `${VAR}` in values.

## Root Path
- For Docker, mount the host directory into the container (e.g. `-v /host/docs:/data/documents`).
- For Kubernetes, use a PersistentVolumeClaim or hostPath (development only).

## Document Identification
Each document receives a stable ID derived from the relative path using SHA-256 (first 16 hex chars). If you move/rename a file the ID changes; treat as delete + create.

## Exclusions
`ExcludePatterns` performs a simple substring match against the relative path. Use short tokens (e.g. `node_modules`, not full path).

## Permissions & Access
Ensure the process user has read access to all files. For large trees consider running with restricted service account and granting directory-level ACLs.

## Performance Tips
- Limit extensions to reduce directory walks.
- Disable `Recursive` for shallow directories.
- Avoid extremely deep directory structures inside containers (inode scanning overhead).

## Troubleshooting
| Issue | Cause | Resolution |
|-------|-------|------------|
| Empty index | Wrong `RootPath` or no matching extensions | Verify container mount & path existence; adjust extensions. |
| Permission denied | File system ACL blocks access | Run as user with read rights or adjust ownership. |
| High CPU during scan | Extremely large number of files | Narrow extensions, segment folders, or consider S3/OneDrive provider for cloud storage. |

## Security Notes
- Do not mount sensitive system directories.
- Keep container read-only if possible; provider only needs read.

## Cross-links & Next Steps
Configure additional providers like [OneDrive Business](onedrive-business.md) or [AWS S3](s3.md) for hybrid ingestion. See [File Lifecycle](../guides/file-lifecycle.md) for how local changes propagate.
