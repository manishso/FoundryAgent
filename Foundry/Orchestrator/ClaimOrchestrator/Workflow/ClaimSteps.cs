using ClaimOrchestrator.Agents;
using ClaimOrchestrator.Configuration;
using ClaimOrchestrator.Models;
using ClaimOrchestrator.Safety;
using ClaimOrchestrator.Validation;
using Microsoft.Agents.AI.Workflows;

namespace ClaimOrchestrator.Workflow;

/// <summary>Step 1: send the bill text to the intake agent and pass its raw reply on.</summary>
public sealed class IntakeStep(FoundryAgentProvider agents, AppSettings settings) : Executor<string, string>("IntakeStep")
{
    public override async ValueTask<string> HandleAsync(string billText, IWorkflowContext context, CancellationToken cancellationToken = default) =>
        await agents.RunAsync(settings.Foundry.IntakeAgentName, billText, cancellationToken);
}

/// <summary>Step 2: plain C# validation of the intake JSON. No AI.</summary>
public sealed class ValidateStep(AppSettings settings) : Executor<string, ValidationResult>("ValidateStep")
{
    public override ValueTask<ValidationResult> HandleAsync(string intakeOutput, IWorkflowContext context, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(BillValidator.Validate(intakeOutput, settings.Claim.TotalTolerance));
}

/// <summary>Step 3a (totals match): ask the policy agent whether the procedure is covered.</summary>
public sealed class PolicyStep(FoundryAgentProvider agents, AppSettings settings) : Executor<ValidationResult, ClaimOutcome>("PolicyStep")
{
    public override async ValueTask<ClaimOutcome> HandleAsync(ValidationResult result, IWorkflowContext context, CancellationToken cancellationToken = default)
    {
        var bill = result.Bill!;
        var max = settings.Claim.MaxFieldLength;

        // Values from the intake agent are untrusted: clean and cut them before they go into another prompt.
        var question = settings.Claim.PolicyQuestionTemplate
            .Replace("{procedure}", UntrustedText.Clean(bill.Procedure, max), StringComparison.Ordinal)
            .Replace("{diagnosis}", UntrustedText.Clean(bill.Diagnosis, max), StringComparison.Ordinal);

        var answer = await agents.RunAsync(settings.Foundry.PolicyAgentName, question, cancellationToken);

        var summary = new BillSummary(
            UntrustedText.Clean(bill.Hospital, max),
            UntrustedText.Clean(bill.Patient, max),
            UntrustedText.Clean(bill.Diagnosis, max),
            UntrustedText.Clean(bill.Procedure, max),
            UntrustedText.Clean(bill.AdmissionDate, max),
            UntrustedText.Clean(bill.DischargeDate, max),
            bill.Items!.Count,
            result.ItemsTotal!.Value,
            result.PrintedTotal!.Value);

        return new ClaimOutcome(ClaimOutcome.PolicyChecked, null, result.ItemsTotal, result.PrintedTotal, summary, UntrustedText.ForDisplay(answer));
    }
}

/// <summary>Step 3b (totals differ, bad JSON or missing field): stop for human review. The policy agent is not called.</summary>
public sealed class StopStep() : Executor<ValidationResult, ClaimOutcome>("StopStep")
{
    public override ValueTask<ClaimOutcome> HandleAsync(ValidationResult result, IWorkflowContext context, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new ClaimOutcome(ClaimOutcome.NeedsReview, result.Reason, result.ItemsTotal, result.PrintedTotal, null, null));
}
