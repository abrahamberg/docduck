using Microsoft.Extensions.Logging;

namespace DocDuck.Providers.Ai;

/// <summary>
/// Task complexity classification for intelligent model selection.
/// </summary>
public enum TaskComplexity
{
    /// <summary>
    /// Simple task - query refinement, basic classification, short responses.
    /// </summary>
    Simple,

    /// <summary>
    /// Moderate task - typical RAG chat, evaluation, moderate reasoning.
    /// </summary>
    Moderate,

    /// <summary>
    /// Complex task - long context, advanced reasoning, critical accuracy.
    /// </summary>
    Complex
}

/// <summary>
/// Intelligently selects the appropriate AI model based on task requirements,
/// available models, user strategy preference, and model capabilities.
/// </summary>
public sealed class AiModelSelector(AiProviderConfiguration config, ILogger<AiModelSelector> logger)
{
    /// <summary>
    /// Select the best available model for a task based on complexity, context size, and strategy.
    /// </summary>
    /// <param name="complexity">Task complexity level</param>
    /// <param name="strategy">User's model selection preference</param>
    /// <param name="estimatedTokens">Estimated context tokens (optional, for context size validation)</param>
    /// <param name="requiresFunctionCalling">Whether the task requires function calling support</param>
    /// <returns>Selected model assignment, or null if no suitable model available</returns>
    public AiModelAssignment? SelectModel(
        TaskComplexity complexity,
        ModelSelectionStrategy? strategy = null,
        int? estimatedTokens = null,
        bool requiresFunctionCalling = false)
    {
        var effectiveStrategy = strategy ?? config.DefaultSelectionStrategy;

        logger.LogDebug("Selecting model: complexity={Complexity}, strategy={Strategy}, tokens={Tokens}, needsTools={Tools}",
            complexity, effectiveStrategy, estimatedTokens ?? 0, requiresFunctionCalling);

        // Get preference-ordered tiers based on strategy and complexity
        var preferredTiers = GetPreferredTiers(complexity, effectiveStrategy);

        // Try each tier in preference order
        foreach (var tier in preferredTiers)
        {
            var model = GetModelForTier(tier);

            if (model == null || !model.Enabled)
            {
                logger.LogDebug("Tier {Tier} has no enabled model, trying next", tier);
                continue;
            }

            // Check function calling requirement
            if (requiresFunctionCalling && !model.SupportsFunctionCalling)
            {
                logger.LogDebug("Model {Model} doesn't support function calling, trying next", model.ModelId);
                continue;
            }

            // Check context size if provided
            if (estimatedTokens.HasValue && estimatedTokens.Value > model.MaxContextTokens)
            {
                logger.LogDebug("Model {Model} context {MaxTokens} insufficient for {Tokens} tokens, trying next",
                    model.ModelId, model.MaxContextTokens, estimatedTokens.Value);
                continue;
            }

            logger.LogInformation("Selected {Tier} model: {Model} (cost factor: {Cost})",
                tier, model.DisplayName, model.CostFactor);
            return model;
        }

        // No suitable model found
        logger.LogWarning("No suitable model found for complexity={Complexity}, strategy={Strategy}, requiresTools={RequiresTools}",
            complexity, effectiveStrategy, requiresFunctionCalling);
        return null;
    }

    /// <summary>
    /// Get ordered list of tiers to try based on task complexity and user strategy.
    /// </summary>
    private static List<AiModelTier> GetPreferredTiers(TaskComplexity complexity, ModelSelectionStrategy strategy)
    {
        return (complexity, strategy) switch
        {
            // Simple tasks
            (TaskComplexity.Simple, ModelSelectionStrategy.Eco) =>
                new() { AiModelTier.Micro, AiModelTier.Mini, AiModelTier.Full },

            (TaskComplexity.Simple, ModelSelectionStrategy.Standard) =>
                new() { AiModelTier.Micro, AiModelTier.Mini, AiModelTier.Full },

            (TaskComplexity.Simple, ModelSelectionStrategy.Turbo) =>
                new() { AiModelTier.Mini, AiModelTier.Full, AiModelTier.Micro },

            // Moderate tasks
            (TaskComplexity.Moderate, ModelSelectionStrategy.Eco) =>
                new() { AiModelTier.Mini, AiModelTier.Micro, AiModelTier.Full },

            (TaskComplexity.Moderate, ModelSelectionStrategy.Standard) =>
                new() { AiModelTier.Mini, AiModelTier.Full, AiModelTier.Micro },

            (TaskComplexity.Moderate, ModelSelectionStrategy.Turbo) =>
                new() { AiModelTier.Full, AiModelTier.Mini, AiModelTier.Micro },

            // Complex tasks
            (TaskComplexity.Complex, ModelSelectionStrategy.Eco) =>
                new() { AiModelTier.Full, AiModelTier.Mini, AiModelTier.Micro },

            (TaskComplexity.Complex, ModelSelectionStrategy.Standard) =>
                new() { AiModelTier.Full, AiModelTier.Mini, AiModelTier.Micro },

            (TaskComplexity.Complex, ModelSelectionStrategy.Turbo) =>
                new() { AiModelTier.Full, AiModelTier.Mini, AiModelTier.Micro },

            _ => new() { AiModelTier.Mini, AiModelTier.Full, AiModelTier.Micro }
        };
    }

    /// <summary>
    /// Get the model assignment for a specific tier.
    /// </summary>
    private AiModelAssignment? GetModelForTier(AiModelTier tier)
    {
        return tier switch
        {
            AiModelTier.Micro => config.MicroModel,
            AiModelTier.Mini => config.MiniModel,
            AiModelTier.Full => config.FullModel,
            _ => null
        };
    }

    /// <summary>
    /// Check if any chat model is available and enabled.
    /// </summary>
    public bool HasAnyEnabledModel()
    {
        return (config.MicroModel?.Enabled == true) ||
               (config.MiniModel?.Enabled == true) ||
               (config.FullModel?.Enabled == true);
    }

    /// <summary>
    /// Get all available tier assignments for admin display.
    /// </summary>
    public Dictionary<AiModelTier, AiModelAssignment?> GetAllTierAssignments()
    {
        return new Dictionary<AiModelTier, AiModelAssignment?>
        {
            [AiModelTier.Micro] = config.MicroModel,
            [AiModelTier.Mini] = config.MiniModel,
            [AiModelTier.Full] = config.FullModel
        };
    }
}
