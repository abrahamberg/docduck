using Api.Services.Agents.Interfaces;
using Api.Services.Agents.Models;
using Api.Services.Interfaces;
using DocDuck.Providers.Ai;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Api.Services.Agents;

/// <summary>
/// Query planner agent implementation using AI for intelligent query analysis.
/// </summary>
public sealed partial class QueryPlannerAgent(
    IModelAgnosticAiService aiService,
    IKeywordSearchService keywordService,
    ILogger<QueryPlannerAgent> logger) : IQueryPlannerAgent
{
    [GeneratedRegex(@"""([^""]+)""")]
    private static partial Regex QuotePattern();

    [GeneratedRegex(@"\b[A-ZÅÄÖ]{2,}(?:\s+[A-ZÅÄÖ]{2,})*\b")]
    private static partial Regex CapsPattern();

    [GeneratedRegex(@"[åäöÅÄÖ]")]
    private static partial Regex SwedishPattern();

    public async Task<SearchPlan> PlanSearchAsync(string query, CancellationToken ct = default)
    {
        logger.LogDebug("Planning search for query: {Query}", query);

        // Extract keywords - preserve exact phrases in quotes and important terms
        var keywords = ExtractKeywordsPreservingLanguage(query);

        // Use AI to refine the query into an optimal search phrase
        // CRITICAL: Must preserve original language and important terms
        var systemPrompt = """
            You are a multilingual search query optimizer. Generate a NATURAL LANGUAGE search phrase for semantic vector search.

            CRITICAL RULES:
            1. OUTPUT MUST BE NATURAL LANGUAGE - NOT boolean queries, NOT SQL syntax, NOT programming syntax
            2. NO boolean operators like AND, OR, parentheses - use natural connecting words instead
            3. PRESERVE the original language - do NOT translate (Swedish→Swedish, English→English, etc.)
            4. KEEP exact phrases in quotes as-is: "be in progress" stays "be in progress"
            5. KEEP ALL-CAPS terms exactly: "HEMSIDA OCH UNDERHÅLL KONTRAKT" stays as-is
            6. KEEP domain-specific terms in their original form
            7. Remove only conversational fluff like "check", "see if", "tell me"
            8. Focus on nouns and key concepts that would appear in documents

            GOOD Examples (natural language):
            - 'check contract with "be in progress"' → 'contract "be in progress" status'
            - "HEMSIDA OCH UNDERHÅLL KONTRAKT CV-maker" → "HEMSIDA OCH UNDERHÅLL KONTRAKT CV-maker offered"
            - "Hur konfigurerar jag API?" → "API konfiguration"
            - "What are deployment best practices?" → "deployment best practices"

            BAD Examples (DO NOT use these formats):
            - "(contract OR agreement) AND status" ❌ NO BOOLEAN
            - "contract | agreement & status" ❌ NO OPERATORS
            - "SELECT * FROM contracts WHERE..." ❌ NO SQL

            Return ONLY the natural language search phrase, nothing else.
            """;

        var messages = new List<ChatMessagePayload>
        {
            new("system", systemPrompt),
            new("user", query)
        };

        var result = await aiService.CompleteChatAsync(
            messages,
            TaskComplexity.Simple,
            strategy: null,
            options: null,
            ct: ct);

        var phrase = result.Content?.Trim() ?? query;

        // Detect document type hints (simple pattern matching)
        var docType = DetectDocumentType(query);

        // Detect natural language (not programming language)
        var language = DetectLanguage(query);

        var plan = new SearchPlan(
            OriginalQuery: query,
            Keywords: keywords,
            Phrase: phrase,
            DocType: docType,
            Language: language,
            LookingFor: $"Documents about: {phrase}"
        );

        logger.LogInformation(
            "Query plan created: phrase=\"{Phrase}\", keywords=[{Keywords}], docType={DocType}, language={Language}",
            plan.Phrase,
            string.Join(", ", plan.Keywords),
            plan.DocType ?? "any",
            plan.Language ?? "unknown");

        return plan;
    }

    /// <summary>
    /// Extract keywords while preserving language, exact phrases, and important terms.
    /// </summary>
    private List<string> ExtractKeywordsPreservingLanguage(string query)
    {
        var keywords = new List<string>();

        // Extract exact phrases in quotes first
        var matches = QuotePattern().Matches(query);
        foreach (Match match in matches)
        {
            keywords.Add(match.Groups[1].Value);
        }

        // Extract ALL-CAPS terms (likely important identifiers)
        var capsMatches = CapsPattern().Matches(query);
        foreach (Match match in capsMatches)
        {
            var term = match.Value.Trim();
            if (!keywords.Contains(term, StringComparer.OrdinalIgnoreCase))
            {
                keywords.Add(term);
            }
        }

        // If we don't have enough keywords, use the basic extraction
        if (keywords.Count < 3)
        {
            var basicKeywords = keywordService.ExtractKeywords(query, maxKeywords: 5)
                .Where(kw => !keywords.Contains(kw, StringComparer.OrdinalIgnoreCase));
            keywords.AddRange(basicKeywords);
        }

        // Limit to reasonable number
        return keywords.Take(5).ToList();
    }

    private static string? DetectDocumentType(string query)
    {
        var lowerQuery = query.ToLowerInvariant();

        return lowerQuery switch
        {
            _ when lowerQuery.Contains("invoice") => "invoice",
            _ when lowerQuery.Contains("receipt") => "receipt",
            _ when lowerQuery.Contains("contract") => "contract",
            _ when lowerQuery.Contains("report") => "report",
            _ when lowerQuery.Contains("article") => "article",
            _ when lowerQuery.Contains("documentation") || lowerQuery.Contains("docs") => "documentation",
            _ when lowerQuery.Contains("readme") => "readme",
            _ when lowerQuery.Contains("guide") => "guide",
            _ when lowerQuery.Contains("tutorial") => "tutorial",
            _ => null
        };
    }

    private static string? DetectLanguage(string query)
    {
        // Detect natural language (Swedish, English, etc.)
        var hasSwedish = SwedishPattern().IsMatch(query);
        if (hasSwedish)
            return "swedish";

        // Simple programming language detection based on keywords
        var lowerQuery = query.ToLowerInvariant();
        if (lowerQuery.Contains("python") || lowerQuery.Contains(".py"))
            return "python";
        if (lowerQuery.Contains("javascript") || lowerQuery.Contains("typescript") || lowerQuery.Contains(".js") || lowerQuery.Contains(".ts"))
            return "javascript/typescript";
        if (lowerQuery.Contains("c#") || lowerQuery.Contains("csharp") || lowerQuery.Contains(".cs"))
            return "csharp";
        if (lowerQuery.Contains("java") && !lowerQuery.Contains("javascript"))
            return "java";
        if (lowerQuery.Contains("sql"))
            return "sql";

        return "english"; // Default assumption
    }
}
