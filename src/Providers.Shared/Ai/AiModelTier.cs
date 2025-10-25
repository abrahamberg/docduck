namespace DocDuck.Providers.Ai;

/// <summary>
/// Model tier classification for intelligent model selection.
/// Tiers allow flexible assignment of different models based on capability and cost.
/// </summary>
public enum AiModelTier
{
    /// <summary>
    /// Micro tier - Smallest, fastest, most cost-effective models.
    /// Suitable for simple tasks like query refinement, basic classification.
    /// Example: gpt-4o-nano, Qwen-2b
    /// </summary>
    Micro,

    /// <summary>
    /// Mini tier - Medium-sized models balancing cost and capability.
    /// Suitable for most chat/RAG tasks, evaluation, moderate complexity.
    /// Example: gpt-4o-mini, deepseek-18b, Qwen-30b
    /// </summary>
    Mini,

    /// <summary>
    /// Full tier - Largest, most capable models.
    /// Suitable for complex reasoning, long context, critical tasks.
    /// Example: gpt-4o, gpt-5, Claude Opus, Qwen (full)
    /// </summary>
    Full
}

/// <summary>
/// User preference for model selection strategy across tiers.
/// </summary>
public enum ModelSelectionStrategy
{
    /// <summary>
    /// Eco mode - Prefer cheaper/smaller models when possible.
    /// Will use Micro for simple tasks, Mini for moderate, Full only when necessary.
    /// Prioritizes cost savings.
    /// </summary>
    Eco,

    /// <summary>
    /// Standard mode - Balanced approach.
    /// Uses Mini for most tasks, Micro for simple operations, Full for complex ones.
    /// Default recommended setting.
    /// </summary>
    Standard,

    /// <summary>
    /// Turbo mode - Prefer more capable models for better quality.
    /// Will use Mini/Full more aggressively, avoiding Micro unless unavailable.
    /// Prioritizes quality and accuracy.
    /// </summary>
    Turbo
}
