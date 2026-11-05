using Azure.AI.Projects;
using Azure.AI.Projects.Agents;
using Microsoft.Agents.AI;

namespace ClaimOrchestrator.Agents;

/// <summary>
/// Looks up existing Foundry prompt agents by name. Their model, instructions and tools live in Foundry,
/// so nothing about the agents is defined here.
/// </summary>
public sealed class FoundryAgentProvider(AIProjectClient projectClient, TimeSpan timeout)
{
    /// <summary>Gets the latest version of the named agent and sends it one message.</summary>
    public async Task<string> RunAsync(string agentName, string message, CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);

        ProjectsAgentRecord record = (await projectClient.AgentAdministrationClient.GetAgentAsync(agentName, timeoutSource.Token)).Value;
        AIAgent agent = projectClient.AsAIAgent(record);

        AgentResponse response = await agent.RunAsync(message, cancellationToken: timeoutSource.Token);
        return response.Text;
    }
}
