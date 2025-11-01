namespace Api.Models;

/// <summary>
/// Internal grouping key for document aggregation
/// </summary>
internal record DocumentKey(string DocId, string Filename, string ProviderType, string ProviderName);

/// <summary>
/// Internal metrics calculated for a document
/// </summary>
internal record DocumentMetrics(double BestDistance, int Strength, string Comment, List<string> AllKeywords);
