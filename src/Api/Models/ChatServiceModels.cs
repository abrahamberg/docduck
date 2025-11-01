using Api.Models;
using Api.Services;

namespace Api.Models;

/// <summary>
/// Parameters for search attempt execution
/// </summary>
internal sealed record SearchAttemptParams(
    string InitialPhrase,
    ChatRequest Request,
    List<ChatMessage> History,
    int MaxAttempts,
    int Depth,
    Func<string, Task> RecordStepAsync,
    List<ModelUsageInfo> ModelUsage,
    Func<ChatStreamUpdate, Task>? Progress,
    List<string> Steps);

/// <summary>
/// Parameters for no-results handling
/// </summary>
internal sealed record NoResultsParams(
    int Attempt,
    int MaxAttempts,
    string CurrentPhrase,
    string UserMessage,
    List<ChatMessage> History,
    List<Source> LatestSources,
    List<string> Steps,
    int TotalTokens,
    Func<ChatStreamUpdate, Task>? Progress,
    Func<string, Task> RecordStepAsync);

/// <summary>
/// Parameters for decision action processing
/// </summary>
internal sealed record DecisionActionParams(
    RefinementDecision Decision,
    int Attempt,
    int MaxAttempts,
    string CurrentPhrase,
    string UserMessage,
    List<ChatMessage> History,
    List<Source> LatestSources,
    List<string> Steps,
    int TotalTokens,
    Func<ChatStreamUpdate, Task>? Progress,
    Func<string, Task> RecordStepAsync);

/// <summary>
/// Parameters for building chat response
/// </summary>
internal sealed record BuildResponseParams(
    string Answer,
    string UserMessage,
    List<ChatMessage> History,
    List<string> Steps,
    List<Source> Sources,
    int Tokens,
    bool IncludeStepsInHistory,
    bool IncludeStepsInResponse,
    List<ModelUsageInfo>? ModelUsage = null);

/// <summary>
/// Result from search attempt execution
/// </summary>
internal sealed record SearchAttemptResult(
    string? FinalAnswer,
    List<Source> LatestSources,
    int TotalTokens,
    ChatResponse? EarlyResponse);

/// <summary>
/// Result from decision action processing
/// </summary>
internal sealed record DecisionActionResult(
    bool ShouldReturn,
    bool ShouldContinue,
    string? NewPhrase,
    ChatResponse? EarlyResponse);
