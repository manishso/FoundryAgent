namespace SensorOrchestratorAgent.Configuration;

public sealed class FoundryOptions
{
    public const string SectionName = "Foundry";

    public string ProjectEndpoint { get; set; } = string.Empty;

    public string ModelDeploymentName { get; set; } = string.Empty;
}

public sealed class AgentOptions
{
    public const string SectionName = "Agent";

    public string Name { get; set; } = "sensor-orchestrator";

    public string Description { get; set; } = string.Empty;

    public string[] Instructions { get; set; } = [];
}

public sealed class SensorApiOptions
{
    public const string SectionName = "SensorApi";

    public bool UseMock { get; set; }

    public string BaseUrl { get; set; } = string.Empty;

    public string LiveDataPath { get; set; } = "sensors/{sensorId}/live";

    public string HistoryPath { get; set; } = "sensors/{sensorId}/history";

    /// <summary>Name of the environment variable that holds the API key. Secrets never live in appsettings.json.</summary>
    public string ApiKeyEnvVar { get; set; } = string.Empty;

    public int TimeoutSeconds { get; set; } = 10;

    public int DefaultHistoryHours { get; set; } = 24;

    public int MaxHistoryPoints { get; set; } = 500;

    public string? ApiKey =>
        string.IsNullOrWhiteSpace(this.ApiKeyEnvVar) ? null : Environment.GetEnvironmentVariable(this.ApiKeyEnvVar);
}

public sealed record AppSettings(FoundryOptions Foundry, AgentOptions Agent, SensorApiOptions SensorApi)
{
    /// <summary>
    /// Binds the JSON configuration. FOUNDRY_PROJECT_ENDPOINT / FOUNDRY_MODEL / AZURE_AI_MODEL_DEPLOYMENT_NAME
    /// override the Foundry section when set, because Foundry injects them into the hosted container.
    /// </summary>
    public static AppSettings Load(IConfiguration configuration)
    {
        var foundry = configuration.GetSection(FoundryOptions.SectionName).Get<FoundryOptions>() ?? new FoundryOptions();
        var agent = configuration.GetSection(AgentOptions.SectionName).Get<AgentOptions>() ?? new AgentOptions();
        var sensorApi = configuration.GetSection(SensorApiOptions.SectionName).Get<SensorApiOptions>() ?? new SensorApiOptions();

        foundry.ProjectEndpoint = FirstNonBlank(
            Environment.GetEnvironmentVariable("FOUNDRY_PROJECT_ENDPOINT"),
            foundry.ProjectEndpoint);
        foundry.ModelDeploymentName = FirstNonBlank(
            Environment.GetEnvironmentVariable("FOUNDRY_MODEL"),
            Environment.GetEnvironmentVariable("AZURE_AI_MODEL_DEPLOYMENT_NAME"),
            foundry.ModelDeploymentName);

        var settings = new AppSettings(foundry, agent, sensorApi);
        settings.Validate();
        return settings;
    }

    private void Validate()
    {
        if (!Uri.TryCreate(this.Foundry.ProjectEndpoint, UriKind.Absolute, out _) || this.Foundry.ProjectEndpoint.Contains('<'))
        {
            throw new InvalidOperationException("Foundry:ProjectEndpoint is not set to a valid URL.");
        }

        if (string.IsNullOrWhiteSpace(this.Foundry.ModelDeploymentName))
        {
            throw new InvalidOperationException("Foundry:ModelDeploymentName is not set.");
        }

        if (this.Agent.Instructions.Length == 0)
        {
            throw new InvalidOperationException("Agent:Instructions must contain at least one line.");
        }

        if (!this.SensorApi.UseMock && !Uri.TryCreate(this.SensorApi.BaseUrl, UriKind.Absolute, out _))
        {
            throw new InvalidOperationException("SensorApi:BaseUrl must be a valid URL when SensorApi:UseMock is false.");
        }
    }

    // Environment variables can arrive set but blank (azd substitutes an empty string), so skip blanks.
    private static string FirstNonBlank(params string?[] candidates) =>
        Array.Find(candidates, c => !string.IsNullOrWhiteSpace(c)) ?? string.Empty;
}
