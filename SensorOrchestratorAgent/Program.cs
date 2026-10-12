// Sensor orchestrator hosted as a Microsoft Foundry hosted agent (Responses protocol).

using Azure.AI.AgentServer.Core;
using DotNetEnv;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Foundry.Hosting;
using SensorOrchestratorAgent.Agents;
using SensorOrchestratorAgent.Configuration;
using SensorOrchestratorAgent.Tools;

// Load a local .env file when present (local development only).
Env.TraversePath().Load();

// AgentHost adds the Foundry agent-server pipeline on top of ASP.NET Core: request-id and
// platform headers, OpenTelemetry (Application Insights / OTLP), health probes and graceful shutdown.
var builder = AgentHost.CreateBuilder(args);

// appsettings.json is loaded by default; APP_CONFIG_PATH points at an alternative JSON file.
var configPath = Environment.GetEnvironmentVariable("APP_CONFIG_PATH");
if (!string.IsNullOrWhiteSpace(configPath))
{
    builder.WebApplicationBuilder.Configuration.AddJsonFile(Path.GetFullPath(configPath), optional: false, reloadOnChange: false);
}

var settings = AppSettings.Load(builder.Configuration);

// The agent must exist before AddFoundryResponses, so tool dependencies are created here
// rather than resolved from the container.
var loggerFactory = LoggerFactory.Create(logging => logging
    .AddConfiguration(builder.Configuration.GetSection("Logging"))
    .AddConsole());

var sensorTools = new SensorTools(
    new SensorApiClient(new HttpClient(), settings.SensorApi),
    settings.SensorApi,
    loggerFactory.CreateLogger<SensorTools>());

AIAgent agent = OrchestratorAgentFactory.Create(settings, sensorTools.AsAITools());

builder.Services.AddFoundryResponses(agent);
builder.RegisterProtocol("responses", endpoints => endpoints.MapFoundryResponses());

var app = builder.Build();
app.Run();
