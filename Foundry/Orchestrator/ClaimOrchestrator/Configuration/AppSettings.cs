using Microsoft.Extensions.Configuration;

namespace ClaimOrchestrator.Configuration;

public sealed class FoundryOptions
{
    public const string SectionName = "Foundry";

    public string ProjectEndpoint { get; set; } = string.Empty;

    /// <summary>Name of the existing Foundry prompt agent that turns bill text into JSON.</summary>
    public string IntakeAgentName { get; set; } = string.Empty;

    /// <summary>Name of the existing Foundry prompt agent that answers coverage questions.</summary>
    public string PolicyAgentName { get; set; } = string.Empty;

    public int AgentTimeoutSeconds { get; set; } = 120;
}

public sealed class ClaimOptions
{
    public const string SectionName = "Claim";

    /// <summary>Largest allowed difference between the sum of items and the printed total.</summary>
    public decimal TotalTolerance { get; set; }

    /// <summary>Bills longer than this are rejected before anything is sent to an agent.</summary>
    public int MaxBillCharacters { get; set; } = 50000;

    /// <summary>Values taken from agent output are cut to this length before reuse in a prompt.</summary>
    public int MaxFieldLength { get; set; } = 200;

    /// <summary>Question sent to the policy agent. {procedure} and {diagnosis} are replaced.</summary>
    public string PolicyQuestionTemplate { get; set; } = string.Empty;
}

public sealed record AppSettings(FoundryOptions Foundry, ClaimOptions Claim)
{
    /// <summary>
    /// Binds appsettings.json. FOUNDRY_PROJECT_ENDPOINT, INTAKE_AGENT_NAME and POLICY_AGENT_NAME
    /// override the Foundry section when set, so only the environment changes between machines.
    /// </summary>
    public static AppSettings Load(IConfiguration configuration)
    {
        var foundry = configuration.GetSection(FoundryOptions.SectionName).Get<FoundryOptions>() ?? new FoundryOptions();
        var claim = configuration.GetSection(ClaimOptions.SectionName).Get<ClaimOptions>() ?? new ClaimOptions();

        foundry.ProjectEndpoint = FirstNonBlank(Environment.GetEnvironmentVariable("FOUNDRY_PROJECT_ENDPOINT"), foundry.ProjectEndpoint);
        foundry.IntakeAgentName = FirstNonBlank(Environment.GetEnvironmentVariable("INTAKE_AGENT_NAME"), foundry.IntakeAgentName);
        foundry.PolicyAgentName = FirstNonBlank(Environment.GetEnvironmentVariable("POLICY_AGENT_NAME"), foundry.PolicyAgentName);

        var settings = new AppSettings(foundry, claim);
        settings.Validate();
        return settings;
    }

    private void Validate()
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(this.Foundry.ProjectEndpoint))
        {
            errors.Add("FOUNDRY_PROJECT_ENDPOINT is not set (or Foundry:ProjectEndpoint in appsettings.json).");
        }
        else if (!Uri.TryCreate(this.Foundry.ProjectEndpoint, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            errors.Add("FOUNDRY_PROJECT_ENDPOINT must be an https URL.");
        }

        if (string.IsNullOrWhiteSpace(this.Foundry.IntakeAgentName))
        {
            errors.Add("INTAKE_AGENT_NAME is not set (or Foundry:IntakeAgentName in appsettings.json).");
        }

        if (string.IsNullOrWhiteSpace(this.Foundry.PolicyAgentName))
        {
            errors.Add("POLICY_AGENT_NAME is not set (or Foundry:PolicyAgentName in appsettings.json).");
        }

        if (this.Foundry.AgentTimeoutSeconds <= 0)
        {
            errors.Add("Foundry:AgentTimeoutSeconds must be greater than 0.");
        }

        if (this.Claim.TotalTolerance < 0)
        {
            errors.Add("Claim:TotalTolerance cannot be negative.");
        }

        if (this.Claim.MaxBillCharacters <= 0 || this.Claim.MaxFieldLength <= 0)
        {
            errors.Add("Claim:MaxBillCharacters and Claim:MaxFieldLength must be greater than 0.");
        }

        if (!this.Claim.PolicyQuestionTemplate.Contains("{procedure}", StringComparison.Ordinal))
        {
            errors.Add("Claim:PolicyQuestionTemplate must contain {procedure}.");
        }

        if (errors.Count > 0)
        {
            throw new InvalidOperationException("Configuration error:" + Environment.NewLine + "  " + string.Join(Environment.NewLine + "  ", errors));
        }
    }

    // Environment variables can be set but blank, so skip blanks.
    private static string FirstNonBlank(params string?[] candidates) =>
        Array.Find(candidates, c => !string.IsNullOrWhiteSpace(c))?.Trim() ?? string.Empty;
}
