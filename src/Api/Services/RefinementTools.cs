using OpenAI.Chat;

namespace Api.Services;

/// <summary>
/// Represents the model's decision after evaluating retrieved context.
/// </summary>
public sealed record RefinementDecision(
    RefinementAction Action,
    string? Reasoning = null,
    string? SuggestedQuery = null,
    string? CannotAnswerReason = null
);

/// <summary>
/// Actions the model can take when evaluating context.
/// </summary>
public enum RefinementAction
{
    AnswerReady,
    NeedsMoreContext,
    RefineQuery,
    CannotAnswer
}

/// <summary>
/// Tool definitions for LLM-driven refinement decisions using OpenAI function calling.
/// 
/// Modern approach: Instead of asking the model to return unstructured JSON and hoping it's valid,
/// we define explicit functions (tools) the model can call. OpenAI guarantees these will be
/// properly structured and conform to the schema.
/// 
/// The model chooses which tool to call based on the query and retrieved context:
/// - answer_ready: Context is sufficient to answer confidently
/// - needs_more_context: Context is related but incomplete (need broader search)
/// - refine_query: Context is off-topic (need better search phrase)
/// - cannot_answer: Question is fundamentally unanswerable
/// 
/// Benefits over manual JSON parsing:
/// - Guaranteed valid, structured output
/// - Model explicitly signals intent (no ambiguity)
/// - Better reasoning captured in tool parameters
/// - Can add new tools without breaking existing code
/// - Follows OpenAI best practices for structured outputs
/// </summary>
public static class RefinementTools
{
    /// <summary>
    /// Tool: Model signals the retrieved context is sufficient to answer the question.
    /// </summary>
    public static ChatTool AnswerReadyTool { get; } = ChatTool.CreateFunctionTool(
        functionName: "answer_ready",
        functionDescription: "Call this when the retrieved context contains enough information to confidently answer the user's question. Use this if you can cite specific facts or details from the provided chunks.",
        functionParameters: BinaryData.FromString("""
        {
            "type": "object",
            "properties": {
                "confidence": {
                    "type": "string",
                    "enum": ["high", "medium"],
                    "description": "Your confidence level that the context is sufficient"
                },
                "reasoning": {
                    "type": "string",
                    "description": "Brief explanation of why the context is sufficient (which chunks contain the answer)"
                }
            },
            "required": ["confidence", "reasoning"],
            "additionalProperties": false
        }
        """)
    );

    /// <summary>
    /// Tool: Model requests a broader or different search because current context is too narrow or off-topic.
    /// </summary>
    public static ChatTool NeedsMoreContextTool { get; } = ChatTool.CreateFunctionTool(
        functionName: "needs_more_context",
        functionDescription: "Call this when the retrieved chunks are related but incomplete, or when you need additional information from different parts of the documentation. Don't use this if the context is completely off-topic (use refine_query instead).",
        functionParameters: BinaryData.FromString("""
        {
            "type": "object",
            "properties": {
                "what_is_missing": {
                    "type": "string",
                    "description": "What specific information is missing from the current context"
                },
                "reasoning": {
                    "type": "string",
                    "description": "Why the current context is insufficient"
                }
            },
            "required": ["what_is_missing", "reasoning"],
            "additionalProperties": false
        }
        """)
    );

    /// <summary>
    /// Tool: Model provides a refined search query with explanation.
    /// </summary>
    public static ChatTool RefineQueryTool { get; } = ChatTool.CreateFunctionTool(
        functionName: "refine_query",
        functionDescription: "Call this when the current search phrase produced irrelevant or off-topic results. Provide a better search phrase optimized for semantic similarity matching. Use concrete domain-specific terms.",
        functionParameters: BinaryData.FromString("""
        {
            "type": "object",
            "properties": {
                "new_query": {
                    "type": "string",
                    "description": "The refined search phrase (3-10 words, concrete nouns and technical terms)"
                },
                "reasoning": {
                    "type": "string",
                    "description": "Why this refinement will yield better results"
                },
                "strategy": {
                    "type": "string",
                    "enum": ["expand", "narrow", "rephrase", "add_technical_terms", "remove_noise"],
                    "description": "The refinement strategy being applied"
                }
            },
            "required": ["new_query", "reasoning", "strategy"],
            "additionalProperties": false
        }
        """)
    );

    /// <summary>
    /// Tool: Model explains why the question cannot be answered with available knowledge.
    /// </summary>
    public static ChatTool CannotAnswerTool { get; } = ChatTool.CreateFunctionTool(
        functionName: "cannot_answer",
        functionDescription: "Call this when the question is fundamentally unanswerable based on the knowledge base (e.g., asking about content that doesn't exist, future predictions, or topics completely outside the documentation scope).",
        functionParameters: BinaryData.FromString("""
        {
            "type": "object",
            "properties": {
                "reason": {
                    "type": "string",
                    "enum": ["out_of_scope", "requires_external_knowledge", "ambiguous_question", "no_relevant_documents"],
                    "description": "Category of why the question cannot be answered"
                },
                "explanation": {
                    "type": "string",
                    "description": "Detailed explanation to help the user rephrase their question"
                }
            },
            "required": ["reason", "explanation"],
            "additionalProperties": false
        }
        """)
    );

    /// <summary>
    /// Get all refinement tools for use in ChatCompletionOptions.
    /// </summary>
    public static IReadOnlyList<ChatTool> AllTools { get; } = new[]
    {
        AnswerReadyTool,
        NeedsMoreContextTool,
        RefineQueryTool,
        CannotAnswerTool
    };

    /// <summary>
    /// Parse a tool call result into a RefinementDecision.
    /// </summary>
    public static RefinementDecision ParseToolCall(ChatToolCall toolCall)
    {
        ArgumentNullException.ThrowIfNull(toolCall);

        var functionName = toolCall.FunctionName;
        var args = toolCall.FunctionArguments;

        return functionName switch
        {
            "answer_ready" => ParseAnswerReady(args),
            "needs_more_context" => ParseNeedsMoreContext(args),
            "refine_query" => ParseRefineQuery(args),
            "cannot_answer" => ParseCannotAnswer(args),
            _ => new RefinementDecision(
                Action: RefinementAction.CannotAnswer,
                Reasoning: $"Unknown tool: {functionName}",
                CannotAnswerReason: "internal_error"
            )
        };
    }

    private static RefinementDecision ParseAnswerReady(BinaryData args)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(args);
            var root = doc.RootElement;
            
            var confidence = root.TryGetProperty("confidence", out var confProp) 
                ? confProp.GetString() ?? "medium" 
                : "medium";
            var reasoning = root.TryGetProperty("reasoning", out var reasonProp)
                ? reasonProp.GetString() ?? "Context appears sufficient"
                : "Context appears sufficient";

            return new RefinementDecision(
                Action: RefinementAction.AnswerReady,
                Reasoning: $"[{confidence} confidence] {reasoning}"
            );
        }
        catch
        {
            return new RefinementDecision(
                Action: RefinementAction.AnswerReady,
                Reasoning: "Model signaled context is sufficient"
            );
        }
    }

    private static RefinementDecision ParseNeedsMoreContext(BinaryData args)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(args);
            var root = doc.RootElement;
            
            var missing = root.TryGetProperty("what_is_missing", out var missProp)
                ? missProp.GetString() ?? "additional context"
                : "additional context";
            var reasoning = root.TryGetProperty("reasoning", out var reasonProp)
                ? reasonProp.GetString() ?? "Context incomplete"
                : "Context incomplete";

            return new RefinementDecision(
                Action: RefinementAction.NeedsMoreContext,
                Reasoning: $"{reasoning} (Missing: {missing})"
            );
        }
        catch
        {
            return new RefinementDecision(
                Action: RefinementAction.NeedsMoreContext,
                Reasoning: "Model requested more context"
            );
        }
    }

    private static RefinementDecision ParseRefineQuery(BinaryData args)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(args);
            var root = doc.RootElement;
            
            var newQuery = root.TryGetProperty("new_query", out var queryProp)
                ? queryProp.GetString()
                : null;
            var reasoning = root.TryGetProperty("reasoning", out var reasonProp)
                ? reasonProp.GetString() ?? "Refinement suggested"
                : "Refinement suggested";
            var strategy = root.TryGetProperty("strategy", out var stratProp)
                ? stratProp.GetString() ?? "rephrase"
                : "rephrase";

            return new RefinementDecision(
                Action: RefinementAction.RefineQuery,
                Reasoning: $"[{strategy}] {reasoning}",
                SuggestedQuery: newQuery
            );
        }
        catch
        {
            return new RefinementDecision(
                Action: RefinementAction.RefineQuery,
                Reasoning: "Model suggested query refinement"
            );
        }
    }

    private static RefinementDecision ParseCannotAnswer(BinaryData args)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(args);
            var root = doc.RootElement;
            
            var reason = root.TryGetProperty("reason", out var reasonProp)
                ? reasonProp.GetString() ?? "no_relevant_documents"
                : "no_relevant_documents";
            var explanation = root.TryGetProperty("explanation", out var explProp)
                ? explProp.GetString() ?? "Question cannot be answered with available knowledge"
                : "Question cannot be answered with available knowledge";

            return new RefinementDecision(
                Action: RefinementAction.CannotAnswer,
                Reasoning: explanation,
                CannotAnswerReason: reason
            );
        }
        catch
        {
            return new RefinementDecision(
                Action: RefinementAction.CannotAnswer,
                Reasoning: "Model indicated question cannot be answered",
                CannotAnswerReason: "unknown"
            );
        }
    }
}
