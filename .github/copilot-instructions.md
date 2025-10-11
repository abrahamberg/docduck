# Copilot Instructions: Clean Code, Pragmatic SOLID, No Overkill

These instructions guide suggestions and edits in this repository. Favor readability and maintainability with a pragmatic take on SOLID. Keep things simple unless complexity is clearly justified.

## Philosophy
- Prefer clarity over cleverness. Optimize for the next person reading the code.
- Apply SOLID principles pragmatically. Avoid ceremony and unnecessary layers (YAGNI, KISS).
- Make small, composable units with clear responsibilities and names.
- Bias toward explicitness and predictable behavior.

## Defaults for code generation
- Structure
  - Keep functions focused (single responsibility) and small enough to grasp quickly.
  - Introduce abstractions only when there are at least two concrete use-cases or clear testability benefits.
  - Avoid premature patterns (factory, strategy, etc.) unless they reduce real duplication or complexity.
- Dependencies
  - Prefer standard library and existing project utilities before adding new packages.
  - If a new dependency is necessary, choose well-maintained, minimal, and widely used; pin versions.
- Error handling and robustness
  - Fail fast on programmer errors; handle recoverable errors with context-rich messages.
  - Don’t swallow exceptions; log with actionable detail. Use structured logging when available.
  - Validate inputs at boundaries; assume internals receive validated data.
- Side effects and state
  - Keep side effects at the edges; keep core functions pure where reasonable.
  - Avoid global mutable state. Pass parameters explicitly; use DI only when it materially improves testability.
- Performance and security
  - Prefer simple, correct solutions. Optimize only with evidence (profiled hot paths).
  - Use timeouts for I/O; consider retries with backoff where appropriate.
  - Never hardcode secrets. Read config via environment variables with sane defaults.
- Types, docs, and tests
  - Provide types/annotations where the language supports it; favor clear, explicit interfaces.
  - Add docstrings or brief comments for non-obvious intent or invariants.
  - Include minimal, focused unit tests for public functions and bug fixes (happy path + 1–2 edge cases).

## Style and conventions
- Follow existing project conventions. If none exist, default to community standards:
  - Python: PEP 8, black formatting, pytest tests.
  - JavaScript/TypeScript: Prettier formatting, sensible ESLint rules, Jest/Vitest tests, ESM when feasible.
  - Go: gofmt/go vet, table-driven tests.
  - Shell: POSIX sh where possible; be explicit about assumptions.
  - C#: target .NET 8 LTS (or repo-wide chosen version), C# 12 language features, run `dotnet format` and enable analyzers; prefer xUnit for tests.
- Naming: verbs for actions (functions), nouns for entities (types, classes), clear and descriptive.

## C# (modern) guidelines
- Language/runtime
  - Use C# 12 with file-scoped namespaces, implicit usings, and nullable reference types enabled (`<Nullable>enable</Nullable>`).
  - Prefer .NET 8 LTS unless the project explicitly requires another TFN.
- Async, I/O, and cancellation
  - Prefer async/await end-to-end; avoid sync-over-async.
  - Accept and pass through CancellationToken on public async APIs; honor it in loops and I/O.
  - For streams and disposables, use `await using` with `IAsyncDisposable` when available.
- Types and data modeling
  - Use `record`/`record struct` for immutable DTOs and value-like models; prefer `init` setters over mutable state.
  - Favor composition and small types over large, multi-purpose classes.
  - Use pattern matching and switch expressions for clear branching; avoid deep if/else pyramids.
- Error handling
  - Throw `ArgumentNullException.ThrowIfNull(...)` for guard clauses; keep messages actionable.
  - Reserve exceptions for exceptional paths; prefer `Try*` patterns for expected misses.
  - Log with `Microsoft.Extensions.Logging` using structured messages (no string concatenation in logs).
- Web/hosted apps
  - Prefer ASP.NET Core minimal APIs or lean controllers; avoid premature layering.
  - Use the Generic Host with built-in DI; register interfaces only when there will be multiple implementations or tests benefit.
  - Use `System.Text.Json` by default; add converters when needed rather than switching libraries.
- Configuration and options
  - Bind settings to POCOs with the Options pattern; validate using `ValidateDataAnnotations` or custom validators.
  - Read secrets from environment or secret stores; never hardcode.
- Performance and correctness
  - Start with clear code; optimize with evidence. Consider spans/memory pooling only in proven hot paths.
  - Avoid allocations in tight loops; prefer `foreach` over LINQ in hot paths when profiling shows impact.
- Tooling and quality
  - Keep analyzers on; treat warnings as errors where practical. Fix or suppress with justification.
  - Use `dotnet test` with xUnit; add a minimal happy-path test plus 1–2 edge cases for public APIs.

## API and module design
- Keep public APIs small and consistent. Avoid breaking changes unless necessary; if needed, provide migration notes.
- Return simple data structures; avoid leaking internal types.
- Prefer composition over inheritance.

## When NOT to apply more SOLID
- Avoid interfaces/abstract classes until there are multiple implementations or tests truly need them.
- Skip layering (e.g., repository/service/use-case) unless the project scale or requirements demand it.
- Don’t wrap libraries "just in case"—only when it isolates volatility or simplifies usage.

## Pull requests and changes
- Include a brief rationale and a short usage/example snippet for new features.
- Keep diffs small and cohesive. Update or add tests alongside changes.
- Document assumptions if requirements are ambiguous; choose the simplest reasonable path.

## What to ask or assume
- If a detail is missing, make 1–2 reasonable assumptions aligned with these guidelines and proceed, clearly noting them in comments/PR text.

---
By default, generate code that is clean, small, and obvious; apply SOLID where it reduces real complexity, not as ceremony.

## Documentation policy (repo hygiene)
- Keep only a concise `README.md` in the repository root. All other documentation must be placed under `docs/`.
- Prefer updating existing canonical docs over creating new top-level files. Avoid duplicated content.
- Place how-to guides under `docs/guides/` (e.g., `quickstart.md`, `authentication.md`, `developer-guide.md`).
- Place database docs under `docs/database/` (e.g., `pgvector.md`, `pgvector-quickref.md`).
- Put ephemeral or auto-generated implementation summaries under `docs/reports/` (e.g., `api-implementation.md`, `pgvector-implementation.md`). If the information has no long-term value, do not generate a new file; integrate the useful bits into existing guides instead.