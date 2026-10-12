using Azure.AI.Projects;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using SensorOrchestratorAgent.Configuration;

namespace SensorOrchestratorAgent.Agents;

public static class OrchestratorAgentFactory
{
    /// <summary>Creates the MAF orchestrator agent backed by the Foundry project model deployment.</summary>
    public static AIAgent Create(AppSettings settings, IList<AITool> tools)
    {
        // DefaultAzureCredential uses the agent's managed identity in Foundry and az login locally.
        // Set AZURE_TOKEN_CREDENTIALS=dev locally to skip the slow managed identity probe.
        var projectClient = new AIProjectClient(new Uri(settings.Foundry.ProjectEndpoint), new DefaultAzureCredential());

        return projectClient.AsAIAgent(
            model: settings.Foundry.ModelDeploymentName,
            instructions: string.Join(Environment.NewLine, settings.Agent.Instructions),
            name: settings.Agent.Name,
            description: settings.Agent.Description,
            tools: tools);
    }
}
