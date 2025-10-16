# FAQ

## What is DocDuck?
An open-source system to index your documents across providers and query them using AI with cited context.

## Do I need deep ML knowledge?
No. Provide an OpenAI-compatible key and follow the quick start.

## Is my data sent to OpenAI?
Only chunk text (for embeddings) and constructed prompts (for answers). Use a self-hosted or private endpoint if required.

## Which file types are supported?
Common text formats, DOCX, PDF* (optional), ODT, RTF, Markdown. Unsupported types are skipped.

## Can I add my own provider?
Yes—implement a small interface (`IDocumentProvider`). See Provider Framework.

## How do I reindex everything?
Set `FORCE_FULL_REINDEX=true` and run the indexer.

## How big should chunks be?
Start at 1000 chars with 200 overlap; adjust based on answer granularity.

## Does it support authentication on queries?
Not yet—planned. Presently secure network access or add a reverse proxy auth layer.

## Can I change the embedding model?
Yes, but update vector dimension and reindex. Multi-model support is future work.

## What database size should I expect?
Roughly (#chunks * (text + ~6KB embedding + metadata)).

## How do I deploy on Kubernetes?
Run the indexer as a CronJob and the API as a Deployment. See docs sections.

## Is there a UI?
Not yet; API-first. A reference UI is on the roadmap.

## Why not use dedicated vector DB X?
PostgreSQL + pgvector lowers operational complexity and is sufficient for many workloads. Abstraction path considered for future.

## Symbol / Branding meaning?
"DocDuck" = Get your document ducks in a row 🦆.

## License?
MIT.
