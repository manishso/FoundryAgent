using System.Text.Encodings.Web;
using System.Text.Json;
using ClaimOrchestrator.Agents;
using ClaimOrchestrator.Configuration;
using ClaimOrchestrator.Models;
using Microsoft.Agents.AI.Workflows;

namespace ClaimOrchestrator.Workflow;

/// <summary>Result of one workflow run: either an outcome or an error message.</summary>
public sealed record ClaimRunResult(ClaimOutcome? Outcome, string? Error);

/// <summary>Builds and runs the claim workflow for the hosted agent.</summary>
public static class ClaimWorkflow
{
    private static readonly JsonSerializerOptions s_outputJson = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// Builds a fresh workflow with fresh executors. Call this once per bill so no state is shared
    /// between runs or between users of the hosted agent.
    /// Routing uses the C# ValidationResult only, never a model decision.
    /// </summary>
    public static Microsoft.Agents.AI.Workflows.Workflow Build(FoundryAgentProvider agents, AppSettings settings)
    {
        var intake = new IntakeStep(agents, settings);
        var validate = new ValidateStep(settings);
        var policy = new PolicyStep(agents, settings);
        var stop = new StopStep();

        return new WorkflowBuilder(intake)
            .AddEdge(intake, validate)
            .AddEdge<ValidationResult>(validate, policy, result => result is { IsValid: true })
            .AddEdge<ValidationResult>(validate, stop, result => result is not { IsValid: true })
            .WithOutputFrom(policy, stop)
            .Build();
    }

    /// <summary>Checks the bill text, runs a fresh workflow on it and returns the single ClaimOutcome.</summary>
    public static async Task<ClaimRunResult> RunAsync(string? billText, FoundryAgentProvider agents, AppSettings settings, CancellationToken cancellationToken = default)
    {
        // The bill text is untrusted: reject empty or oversized input before any agent call.
        if (string.IsNullOrWhiteSpace(billText))
        {
            return new ClaimRunResult(null, "The bill text is empty.");
        }

        if (billText.Length > settings.Claim.MaxBillCharacters)
        {
            return new ClaimRunResult(null, $"The bill has {billText.Length} characters; the limit is {settings.Claim.MaxBillCharacters} (Claim:MaxBillCharacters).");
        }

        ClaimOutcome? outcome = null;
        string? error = null;

        await using StreamingRun run = await InProcessExecution.RunStreamingAsync(Build(agents, settings), billText, cancellationToken: cancellationToken);
        await foreach (WorkflowEvent evt in run.WatchStreamAsync(cancellationToken))
        {
            switch (evt)
            {
                case WorkflowOutputEvent output when output.Data is ClaimOutcome claimOutcome:
                    outcome = claimOutcome;
                    break;
                case ExecutorFailedEvent stepFailed:
                    error = $"Step {stepFailed.ExecutorId} failed: {(stepFailed.Data as Exception)?.Message ?? stepFailed.Data?.ToString()}";
                    break;
                case WorkflowErrorEvent workflowError:
                    error ??= $"Workflow error: {workflowError.Exception?.Message}";
                    break;
            }
        }

        return outcome is not null
            ? new ClaimRunResult(outcome, null)
            : new ClaimRunResult(null, error ?? "The workflow finished without an output.");
    }

    /// <summary>Formats the outcome as JSON. The serializer escapes control characters in agent text.</summary>
    public static string ToJson(ClaimOutcome outcome) => JsonSerializer.Serialize(outcome, s_outputJson);
}
