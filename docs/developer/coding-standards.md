# Coding Standards

## Language & Runtime
- C# 12, .NET 8
- Nullable reference types enabled
- File-scoped namespaces

## General Principles
- Clarity over cleverness
- Small focused classes & methods
- Fail fast on misconfiguration
- Avoid premature abstractions (only add when ≥2 use cases)

## Logging
- Use structured logging (`logger.LogInformation("Processed {File}", file)`)
- Avoid logging secrets or large payloads
- Use `LogDebug` for verbose diagnostic detail

## Error Handling
- Guard clauses with `ArgumentNullException.ThrowIfNull`
- Catch → log → rethrow only when adding context
- Per-file errors in indexing should not abort entire run

## Async
- Prefer async/await end-to-end
- Accept `CancellationToken` on public async APIs

## Options & Config
- Centralize option binding in startup
- Provide explicit environment variable mapping

## Data Access
- Keep SQL explicit & reviewed
- Parameterize queries; avoid string concatenation

## Testing
- Add tests for new providers/extractors core logic
- Keep test data minimal and deterministic

## Naming
- Providers: `<Type>Provider`
- Extractors: `<Format>TextExtractor`
- Options: `<Feature>Options`

## Style
- Follow standard .NET style / editorconfig
- Prefer expression-bodied members for trivial getters

## Dependency Injection
- Register concrete types unless interface adds clear value or needed for mocking

## Future Enhancements
- Introduce analyzers for consistent logging

## Next
- Observability: [Observability](observability.md)
