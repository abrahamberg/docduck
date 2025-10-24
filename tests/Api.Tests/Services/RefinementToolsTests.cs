using Api.Services;
using OpenAI.Chat;

namespace Api.Tests.Services;

public class RefinementToolsTests
{
    [Fact]
    public void ParseToolCall_AnswerReady_ReturnsCorrectDecision()
    {
        // Arrange
        var args = BinaryData.FromString("""
            {
                "confidence": "high",
                "reasoning": "All required information found in chunks 1-3"
            }
            """);
        var toolCall = ChatToolCall.CreateFunctionToolCall("id1", "answer_ready", args);

        // Act
        var decision = RefinementTools.ParseToolCall(toolCall);

        // Assert
        Assert.Equal(RefinementAction.AnswerReady, decision.Action);
        Assert.Contains("high confidence", decision.Reasoning);
        Assert.Contains("chunks 1-3", decision.Reasoning);
        Assert.Null(decision.SuggestedQuery);
        Assert.Null(decision.CannotAnswerReason);
    }

    [Fact]
    public void ParseToolCall_NeedsMoreContext_ReturnsCorrectDecision()
    {
        // Arrange
        var args = BinaryData.FromString("""
            {
                "what_is_missing": "installation steps",
                "reasoning": "Chunks only cover overview, not detailed setup"
            }
            """);
        var toolCall = ChatToolCall.CreateFunctionToolCall("id2", "needs_more_context", args);

        // Act
        var decision = RefinementTools.ParseToolCall(toolCall);

        // Assert
        Assert.Equal(RefinementAction.NeedsMoreContext, decision.Action);
        Assert.Contains("installation steps", decision.Reasoning);
        Assert.Contains("overview", decision.Reasoning);
        Assert.Null(decision.SuggestedQuery);
        Assert.Null(decision.CannotAnswerReason);
    }

    [Fact]
    public void ParseToolCall_RefineQuery_ReturnsCorrectDecision()
    {
        // Arrange
        var args = BinaryData.FromString("""
            {
                "new_query": "kubernetes deployment yaml configuration",
                "reasoning": "Original was too generic, adding technical terms",
                "strategy": "add_technical_terms"
            }
            """);
        var toolCall = ChatToolCall.CreateFunctionToolCall("id3", "refine_query", args);

        // Act
        var decision = RefinementTools.ParseToolCall(toolCall);

        // Assert
        Assert.Equal(RefinementAction.RefineQuery, decision.Action);
        Assert.Contains("add_technical_terms", decision.Reasoning);
        Assert.Equal("kubernetes deployment yaml configuration", decision.SuggestedQuery);
        Assert.Null(decision.CannotAnswerReason);
    }

    [Fact]
    public void ParseToolCall_CannotAnswer_ReturnsCorrectDecision()
    {
        // Arrange
        var args = BinaryData.FromString("""
            {
                "reason": "out_of_scope",
                "explanation": "Question asks about future pricing which is not in documentation"
            }
            """);
        var toolCall = ChatToolCall.CreateFunctionToolCall("id4", "cannot_answer", args);

        // Act
        var decision = RefinementTools.ParseToolCall(toolCall);

        // Assert
        Assert.Equal(RefinementAction.CannotAnswer, decision.Action);
        Assert.Contains("future pricing", decision.Reasoning);
        Assert.Equal("out_of_scope", decision.CannotAnswerReason);
        Assert.Null(decision.SuggestedQuery);
    }

    [Fact]
    public void ParseToolCall_MalformedJson_DoesNotThrow()
    {
        // Arrange
        var args = BinaryData.FromString("{ invalid json }");
        var toolCall = ChatToolCall.CreateFunctionToolCall("id5", "answer_ready", args);

        // Act
        var decision = RefinementTools.ParseToolCall(toolCall);

        // Assert
        Assert.Equal(RefinementAction.AnswerReady, decision.Action);
        Assert.NotNull(decision.Reasoning);
    }

    [Fact]
    public void ParseToolCall_UnknownTool_ReturnsCannotAnswer()
    {
        // Arrange
        var args = BinaryData.FromString("{}");
        var toolCall = ChatToolCall.CreateFunctionToolCall("id6", "unknown_tool", args);

        // Act
        var decision = RefinementTools.ParseToolCall(toolCall);

        // Assert
        Assert.Equal(RefinementAction.CannotAnswer, decision.Action);
        Assert.Contains("Unknown tool", decision.Reasoning);
    }

    [Fact]
    public void AllTools_ContainsFourTools()
    {
        // Act
        var tools = RefinementTools.AllTools;

        // Assert
        Assert.Equal(4, tools.Count);
        Assert.Contains(tools, t => t.FunctionName == "answer_ready");
        Assert.Contains(tools, t => t.FunctionName == "needs_more_context");
        Assert.Contains(tools, t => t.FunctionName == "refine_query");
        Assert.Contains(tools, t => t.FunctionName == "cannot_answer");
    }

    [Fact]
    public void ParseToolCall_MissingOptionalFields_UsesDefaults()
    {
        // Arrange - missing optional fields
        var args = BinaryData.FromString("""
            {
                "new_query": "docker containers"
            }
            """);
        var toolCall = ChatToolCall.CreateFunctionToolCall("id7", "refine_query", args);

        // Act
        var decision = RefinementTools.ParseToolCall(toolCall);

        // Assert
        Assert.Equal(RefinementAction.RefineQuery, decision.Action);
        Assert.Equal("docker containers", decision.SuggestedQuery);
        Assert.NotNull(decision.Reasoning);
    }
}
