# Roadmap

Planned & aspirational enhancements. Subject to change; community feedback welcome.

## Near Term
- Auth middleware (JWT / API key) for query endpoints
- Improved PDF extraction defaults
- Provider-specific configuration endpoint (hot reload)
- Basic reference web UI (search + chat)

## Medium Term
- Hybrid search (lexical + vector)
- Reranking (cross-encoder)
- Alternative embedding backends (local models)
- Multi-embedding storage strategy
- Metadata filters (path, tag, date)

## Long Term
- ACL-aware search results
- Pluggable summarization / answer styles
- Vector store abstraction (Postgres + optional external backends)
- Real-time incremental indexing (webhooks / events)

## Community Wishlist (Proposed)
- Google Drive provider
- Confluence provider
- Git repository markdown provider
- HTML extractor + site crawler

## Guiding Principles
- Keep operational footprint low
- Prefer explicit clarity over abstraction layers
- Add complexity only when driven by real user needs

## Contributing to Roadmap
Open a GitHub issue with label `proposal` including:
- Problem statement
- Proposed solution sketch
- Alternatives considered
- Impact / tradeoffs

## Changelog
See Releases / CHANGELOG for implemented items.
