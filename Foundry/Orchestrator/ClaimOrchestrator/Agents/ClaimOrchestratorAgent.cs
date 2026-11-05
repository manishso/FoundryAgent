using System.Runtime.CompilerServices;
using System.Text.Json;
using ClaimOrchestrator.Configuration;
using ClaimOrchestrator.Workflow;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace ClaimOrchestrator.Agents;

/// <summary>
/// Exposes the claim workflow as an agent for Foundry hosting.
/// The user's latest message is the bill text; the reply is the outcome JSON.
/// A fresh workflow is built for every request, so nothing is shared between users or runs.
/// </summary>
public sealed class ClaimOrchestratorAgent(FoundryAgentProvider agents, AppSettings settings) : AIAgent
{
    public override string? Name => "claim-orchestrator";

    public override string? Description => "Checks a hospital bill: extracts it, validates the totals in C#, then checks policy coverage or stops for review.";

    protected override async Task<AgentResponse> RunCoreAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session = null,
        AgentRunOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        // Only the latest user message is used. Earlier turns are ignored, so each bill is checked on its own.
        var billText = messages.LastOrDefault(m => m.Role == ChatRole.User)?.Text;

        var result = await ClaimWorkflow.RunAsync(billText, agents, settings, cancellationToken);
        var reply = result.Outcome is not null ? ClaimWorkflow.ToJson(result.Outcome) : $"Error: {result.Error}";

        return new AgentResponse(new ChatMessage(ChatRole.Assistant, reply))
        {
            AgentId = this.Id,
            ResponseId = Guid.NewGuid().ToString("N"),
        };
    }

    protected override async IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session = null,
        AgentRunOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // The workflow produces one final result, so stream it as a single update.
        var response = await this.RunCoreAsync(messages, session, options, cancellationToken);
        yield return new AgentResponseUpdate(ChatRole.Assistant, response.Text)
        {
            AgentId = this.Id,
            ResponseId = response.ResponseId,
            MessageId = Guid.NewGuid().ToString("N"),
        };
    }

    // The agent keeps no conversation state, so a session is an empty marker.
    protected override ValueTask<AgentSession> CreateSessionCoreAsync(CancellationToken cancellationToken = default) =>
        new(new ClaimSession());

    protected override ValueTask<JsonElement> SerializeSessionCoreAsync(AgentSession session, JsonSerializerOptions? jsonSerializerOptions = null, CancellationToken cancellationToken = default) =>
        new(JsonSerializer.SerializeToElement(new { }));

    protected override ValueTask<AgentSession> DeserializeSessionCoreAsync(JsonElement serializedState, JsonSerializerOptions? jsonSerializerOptions = null, CancellationToken cancellationToken = default) =>
        new(new ClaimSession());

    private sealed class ClaimSession : AgentSession;
}
