using Api.Models;
using Api.Services.Agents.Interfaces;
using Api.Services.Agents.Models;
using Api.Services.Interfaces;

namespace Api.Services.Agents;

/// <summary>
/// Evaluator agent: scores findings and adds explanatory comments.
/// </summary>
public sealed class EvaluatorAgent : IEvaluatorAgent
{
    private readonly IDocumentAggregationService _aggregationService;
    private readonly ILogger<EvaluatorAgent> _logger;

    public EvaluatorAgent(
        IDocumentAggregationService aggregationService,
        ILogger<EvaluatorAgent> logger)
    {
        _aggregationService = aggregationService;
        _logger = logger;
    }

    public async Task<List<SearchFinding>> EvaluateAsync(
        SearchPlan plan,
        List<RawSearchResult> rawResults,
        CancellationToken ct = default)
    {
        _logger.LogInformation("Evaluating {Count} raw results", rawResults.Count);

        if (rawResults.Count == 0)
        {
            return [];
        }

        // Use document aggregation service to group by document, fetch context, and calculate strength
        var findings = await _aggregationService.AggregateByDocumentAsync(
            rawResults,
            contextChunkCount: 2,
            ct);

        // Enhance comments with plan-specific context
        var enhancedFindings = findings.Select(f => EnhanceFinding(f, plan)).ToList();

        _logger.LogInformation(
            "Evaluation complete: {FindingCount} findings, top strength: {TopStrength}",
            enhancedFindings.Count,
            enhancedFindings.MaxBy(f => f.Strength)?.Strength ?? 0);

        return enhancedFindings;
    }

    private static SearchFinding EnhanceFinding(SearchFinding finding, SearchPlan plan)
    {
        // Check if document type matches plan (if specified)
        var docTypeMatch = plan.DocType == null || IsDocTypeMatch(finding.Filename, plan.DocType);

        // Check if language matches plan (if specified)
        var languageMatch = plan.Language == null || IsLanguageMatch(finding.Filename, plan.Language);

        // Adjust strength based on doc type and language match
        var strengthAdjustment = 0;
        if (docTypeMatch) strengthAdjustment += 5;
        if (languageMatch) strengthAdjustment += 5;

        var adjustedStrength = Math.Clamp(finding.Strength + strengthAdjustment, 0, 100);

        // Enhance comment with additional context
        var commentParts = new List<string> { finding.Comment };

        if (plan.DocType != null && docTypeMatch)
        {
            commentParts.Add($"matches '{plan.DocType}' type");
        }

        if (plan.Language != null && languageMatch)
        {
            commentParts.Add($"{plan.Language} file");
        }

        var enhancedComment = string.Join("; ", commentParts);
        if (enhancedComment.Length > 300)
        {
            enhancedComment = string.Concat(enhancedComment.AsSpan(0, 297), "...");
        }

        return finding with
        {
            Strength = adjustedStrength,
            Comment = enhancedComment
        };
    }

    private static bool IsDocTypeMatch(string filename, string docType)
    {
        var lowerFilename = filename.ToLowerInvariant();
        var lowerDocType = docType.ToLowerInvariant();

        return lowerFilename.Contains(lowerDocType);
    }

    private static bool IsLanguageMatch(string filename, string language)
    {
        var extension = Path.GetExtension(filename).ToLowerInvariant();
        var lowerLanguage = language.ToLowerInvariant();

        return lowerLanguage switch
        {
            "python" => extension == ".py",
            "javascript" or "typescript" or "javascript/typescript" => extension is ".js" or ".ts" or ".jsx" or ".tsx",
            "csharp" or "c#" => extension == ".cs",
            "java" => extension == ".java",
            "sql" => extension == ".sql",
            "markdown" => extension is ".md" or ".markdown",
            "html" => extension is ".html" or ".htm",
            _ => false
        };
    }
}
