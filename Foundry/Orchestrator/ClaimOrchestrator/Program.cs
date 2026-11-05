// ClaimOrchestrator: Microsoft Agent Framework workflow hosted as a Foundry hosted agent (Responses protocol).
// IntakeStep (Foundry agent) -> ValidateStep (plain C#) -> PolicyStep (Foundry agent) or StopStep.
// Listens on http://localhost:8088 locally; send the bill text as the message.

using Azure.AI.AgentServer.Core;
using Azure.AI.Projects;
using Azure.Identity;
using ClaimOrchestrator.Agents;
using ClaimOrchestrator.Configuration;
using Microsoft.Agents.AI.Foundry.Hosting;

// AgentHost adds the Foundry agent-server pipeline: health probes, telemetry and graceful shutdown.
var builder = AgentHost.CreateBuilder(args);

// appsettings.json is loaded by default; APP_CONFIG_PATH points at an alternative JSON file.
var configPath = Environment.GetEnvironmentVariable("APP_CONFIG_PATH");
if (!string.IsNullOrWhiteSpace(configPath))
{
    builder.WebApplicationBuilder.Configuration.AddJsonFile(Path.GetFullPath(configPath), optional: false, reloadOnChange: false);
}

// Configuration: appsettings.json plus FOUNDRY_PROJECT_ENDPOINT / INTAKE_AGENT_NAME / POLICY_AGENT_NAME overrides.
AppSettings settings;
try
{
    settings = AppSettings.Load(builder.Configuration);
}
catch (InvalidOperationException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}

// DefaultAzureCredential: az login locally, the agent's managed identity when hosted. No keys in code.
var projectClient = new AIProjectClient(new Uri(settings.Foundry.ProjectEndpoint), new DefaultAzureCredential());
var agents = new FoundryAgentProvider(projectClient, TimeSpan.FromSeconds(settings.Foundry.AgentTimeoutSeconds));

// The agent builds a fresh workflow for every request, so nothing is shared between users.
builder.Services.AddFoundryResponses(new ClaimOrchestratorAgent(agents, settings));
builder.RegisterProtocol("responses", endpoints => endpoints.MapFoundryResponses());

var app = builder.Build();
await app.RunAsync();
return 0;
