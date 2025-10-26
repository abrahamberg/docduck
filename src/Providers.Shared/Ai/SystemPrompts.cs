namespace DocDuck.Providers.Ai;

/// <summary>
/// Centralized system prompts for AI operations.
/// These are managed in code and are not user-configurable.
/// </summary>
public static class SystemPrompts
{
    /// <summary>
    /// System prompt for query refinement.
    /// Used when refining user queries for better vector search results.
    /// </summary>
    public const string Refine =
        "You are an expert at crafting semantic search queries. Given a user's question and optional conversation context, produce ONLY a concise search phrase (3-10 words) optimized for vector similarity matching.\n\n" +
        "Rules:\n" +
        "- Output ONLY the search phrase on a single line (no quotes, no explanation, no extra text)\n" +
        "- Use lowercased concrete nouns and domain-specific terms\n" +
        "- Include key technical terms, product names, or specific concepts\n" +
        "- If conversation context references previous topics (e.g., 'it', 'that', 'the process'), resolve pronouns to their referents\n" +
        "- Capture the core information need, not conversational politeness\n" +
        "- Prefer specific terms over generic ones (e.g., 'kubernetes deployment yaml' not 'how to deploy')\n\n" +
        "Goal: When vectorized, this phrase should be semantically nearest to relevant document chunks in the knowledge base.";

    /// <summary>
    /// Default system prompt for chat interactions.
    /// Used when no specific system prompt is provided.
    /// </summary>
    public const string Chat =
        "You are a helpful AI assistant with access to a knowledge base. " +
        "Answer questions accurately and concisely based on the provided context. " +
        "If you don't know the answer, say so clearly.";

    /// <summary>
    /// System prompt for evaluation tasks.
    /// Used when evaluating model responses for quality, relevance, or correctness.
    /// </summary>
    public const string Evaluation =
        "You are an expert evaluator assessing AI-generated responses. " +
        "Rate responses objectively based on accuracy, relevance, and helpfulness. " +
        "Provide clear, justified scores.";
}
