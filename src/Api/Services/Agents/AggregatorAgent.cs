using Api.Models;
using Api.Services.Agents.Interfaces;

namespace Api.Services.Agents;

/// <summary>
/// Aggregator agent: merges findings from multiple steps, deduplicates, and produces final ranking.
/// </summary>
public sealed class AggregatorAgent(ILogger<AggregatorAgent> logger) : IAggregatorAgent
{
    public Task<List<SearchFinding>> AggregateAsync(
        List<SearchStep> steps,
        CancellationToken ct = default)
    {
        logger.LogInformation("Aggregating findings from {StepCount} steps", steps.Count);

        if (steps.Count == 0)
        {
            return Task.FromResult(new List<SearchFinding>());
        }

        // Collect all findings from all steps
        var allFindings = steps.SelectMany(s => s.Findings).ToList();

        if (allFindings.Count == 0)
        {
            return Task.FromResult(new List<SearchFinding>());
        }

        // Group by document ID to merge findings for the same document
        var documentGroups = allFindings
            .GroupBy(f => f.DocId)
            .ToList();

        var mergedFindings = new List<SearchFinding>();

        foreach (var group in documentGroups)
        {
            var merged = MergeDocumentFindings(group.ToList());
            mergedFindings.Add(merged);
        }

        // Sort by strength descending
        var sortedFindings = mergedFindings
            .OrderByDescending(f => f.Strength)
            .ToList();

        logger.LogInformation(
            "Aggregation complete: {DocumentCount} documents, top strength: {TopStrength}",
            sortedFindings.Count,
            sortedFindings.FirstOrDefault()?.Strength ?? 0);

        return Task.FromResult(sortedFindings);
    }

    private static SearchFinding MergeDocumentFindings(List<SearchFinding> findings)
    {
        if (findings.Count == 1)
        {
            return findings[0];
        }

        var first = findings[0];

        // Merge chunks from all findings, deduplicate by chunk number
        var allChunks = findings
            .SelectMany(f => f.Chunks)
            .GroupBy(c => c.ChunkNum)
            .Select(g => g.OrderBy(c => c.Distance).First()) // Take best score for each chunk
            .OrderBy(c => c.ChunkNum)
            .ToList();

        // Merge context chunks (should be the same across findings for same document)
        var contextChunks = findings
            .SelectMany(f => f.ContextChunks ?? [])
            .GroupBy(c => c.ChunkNum)
            .Select(g => g.First())
            .OrderBy(c => c.ChunkNum)
            .ToList();

        // Merge keywords
        var allKeywords = findings
            .SelectMany(f => f.Keywords ?? [])
            .Distinct()
            .ToList();

        // Take best (lowest) distance
        var bestDistance = findings.Min(f => f.Distance);

        // Take highest strength
        var bestStrength = findings.Max(f => f.Strength);

        // Merge comments (take the longest/most informative)
        var bestComment = findings
            .OrderByDescending(f => f.Comment.Length)
            .First()
            .Comment;

        return new SearchFinding(
            DocId: first.DocId,
            Filename: first.Filename,
            ProviderType: first.ProviderType,
            ProviderName: first.ProviderName,
            Strength: bestStrength,
            Comment: bestComment,
            Distance: bestDistance,
            Keywords: allKeywords,
            ChunkCount: allChunks.Count,
            Chunks: allChunks,
            ContextChunks: contextChunks
        );
    }
}
