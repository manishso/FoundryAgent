using System.Text.Json.Serialization;

namespace ClaimOrchestrator.Models;

/// <summary>The JSON the intake agent returns. Every field is nullable because the agent sends null for missing values.</summary>
public sealed class IntakeBill
{
    [JsonPropertyName("hospital")] public string? Hospital { get; set; }

    [JsonPropertyName("patient")] public string? Patient { get; set; }

    [JsonPropertyName("diagnosis")] public string? Diagnosis { get; set; }

    [JsonPropertyName("procedure")] public string? Procedure { get; set; }

    [JsonPropertyName("admissionDate")] public string? AdmissionDate { get; set; }

    [JsonPropertyName("dischargeDate")] public string? DischargeDate { get; set; }

    [JsonPropertyName("items")] public List<BillItem?>? Items { get; set; }

    [JsonPropertyName("printedTotal")] public decimal? PrintedTotal { get; set; }
}

public sealed class BillItem
{
    [JsonPropertyName("description")] public string? Description { get; set; }

    [JsonPropertyName("amount")] public decimal? Amount { get; set; }
}

/// <summary>Result of the plain C# validation. The workflow routes on IsValid, never on model output.</summary>
public sealed record ValidationResult(bool IsValid, string Reason, IntakeBill? Bill, decimal? ItemsTotal, decimal? PrintedTotal);

/// <summary>Summary of the bill shown in the final output.</summary>
public sealed record BillSummary(
    string Hospital,
    string Patient,
    string Diagnosis,
    string Procedure,
    string AdmissionDate,
    string DischargeDate,
    int ItemCount,
    decimal ItemsTotal,
    decimal PrintedTotal);

/// <summary>Final workflow output. Status is "PolicyChecked" or "NeedsReview".</summary>
public sealed record ClaimOutcome(
    string Status,
    string? Reason,
    decimal? ItemsTotal,
    decimal? PrintedTotal,
    BillSummary? Bill,
    string? PolicyAnswer)
{
    public const string PolicyChecked = "PolicyChecked";
    public const string NeedsReview = "NeedsReview";
}
