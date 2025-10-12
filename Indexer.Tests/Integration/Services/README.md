# OpenAI Embeddings Client Integration Tests

Integration tests for the `OpenAiEmbeddingsClient` that verify actual communication with the OpenAI API.

## Prerequisites

- Valid OpenAI API key
- Internet connection

## Configuration

Set the following environment variables before running the tests:

```bash
export OPENAI_API_KEY="sk-your-api-key-here"
export OPENAI_BASE_URL="https://api.openai.com/v1"  # Optional, defaults to this
export OPENAI_EMBED_MODEL="text-embedding-3-small"   # Optional, defaults to this
```

## Running the Tests

### Run ALL integration tests (requires API key):
```bash
cd Indexer.Tests
OPENAI_API_KEY=sk-... dotnet test --filter "Category=Integration&FullyQualifiedName~OpenAi"
```

### Run ONLY the OpenAI integration tests:
```bash
cd Indexer.Tests
OPENAI_API_KEY=sk-... dotnet test --filter "FullyQualifiedName~OpenAiEmbeddingsClientTests"
```

### Skip integration tests (run unit tests only):
```bash
cd Indexer.Tests
dotnet test --filter "Category!=Integration"
```

## Test Coverage

The integration test suite covers:

### Basic Functionality
- ✅ Single input embedding generation
- ✅ Multiple inputs embedding generation
- ✅ Empty input handling
- ✅ Null input validation

### Batching
- ✅ Large input processing with automatic batching
- ✅ Small input processing (single batch)
- ✅ Batch size configuration

### Cancellation
- ✅ Cancellation token support

### Semantic Quality
- ✅ Similar texts produce similar embeddings
- ✅ Dissimilar texts produce different embeddings
- ✅ Identical texts produce identical embeddings
- ✅ Embedding vector dimensions (1536 for text-embedding-3-small)
- ✅ Embedding values within expected range [-1, 1]

### Edge Cases
- ✅ Long text handling (within token limits)

## Cost Considerations

⚠️ **Note**: These tests make real API calls to OpenAI and will incur costs.

Approximate cost per test run (as of 2024):
- Model: `text-embedding-3-small`
- Total test inputs: ~150 short texts
- Estimated tokens: ~3,000 tokens
- Cost: ~$0.001 USD per run

To minimize costs during development:
1. Run unit tests only: `dotnet test --filter "Category!=Integration"`
2. Use environment variable checks to skip tests when API key is not set
3. Run integration tests only when needed (e.g., before releases)

## Test Behavior Without API Key

If `OPENAI_API_KEY` is not set, the tests will be **skipped automatically** using xUnit's `Skip.If()` functionality. You'll see output like:

```
[SKIP] EmbedAsync_WithSingleInput_ReturnsEmbedding
  Reason: OPENAI_API_KEY environment variable not set
```

## Troubleshooting

### Tests are skipped
- Ensure `OPENAI_API_KEY` environment variable is set
- Verify the API key is valid
- Check that you're running with the Integration category filter

### API rate limiting errors
- The tests use a test collection to run sequentially
- Add delays between test runs if needed
- Consider using a higher tier API key

### Embedding dimension mismatch
- Verify you're using the expected model (`text-embedding-3-small` = 1536 dimensions)
- If using a different model, update the dimension assertions in tests

## Example Output

```bash
$ OPENAI_API_KEY=sk-... dotnet test --filter "FullyQualifiedName~OpenAiEmbeddingsClientTests"

[xUnit.net 00:00:00.00] xUnit.net VSTest Adapter v2.8.2+699d445a1a
[xUnit.net 00:00:00.13]   Discovering: Indexer.Tests
[xUnit.net 00:00:00.27]   Discovered:  Indexer.Tests
[xUnit.net 00:00:00.28]   Starting:    Indexer.Tests
[xUnit.net 00:00:02.53]   Finished:    Indexer.Tests

Test summary: total: 11, failed: 0, succeeded: 11, skipped: 0, duration: 2.5s
Build succeeded in 3.8s
```

## CI/CD Integration

For CI/CD pipelines, store the OpenAI API key as a secret and conditionally run integration tests:

```yaml
# GitHub Actions example
- name: Run Integration Tests
  env:
    OPENAI_API_KEY: ${{ secrets.OPENAI_API_KEY }}
  run: dotnet test --filter "Category=Integration&FullyQualifiedName~OpenAi"
  if: env.OPENAI_API_KEY != ''
```

## Related Documentation

- [OpenAI Embeddings API](https://platform.openai.com/docs/api-reference/embeddings)
- [text-embedding-3-small model](https://platform.openai.com/docs/guides/embeddings/embedding-models)
- [Integration Test README](../Integration/README.md)
