using System.Text.Json;
using ClaimOrchestrator.Models;

namespace ClaimOrchestrator.Validation;

/// <summary>Plain C# checks on the intake agent's output. No AI is involved, so the result is deterministic.</summary>
public static class BillValidator
{
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        // Strict parsing: amounts must be JSON numbers, no comments or trailing commas, limited nesting.
        MaxDepth = 16,
        AllowTrailingCommas = false,
        ReadCommentHandling = JsonCommentHandling.Disallow,
    };

    public static ValidationResult Validate(string? agentOutput, decimal tolerance)
    {
        // 1. Parse the JSON. The output is untrusted, so any parse problem means review, not a crash.
        IntakeBill? bill;
        try
        {
            bill = JsonSerializer.Deserialize<IntakeBill>(StripCodeFence(agentOutput), s_jsonOptions);
        }
        catch (JsonException ex)
        {
            return Invalid($"Intake agent output is not valid JSON ({ex.Message}).");
        }

        if (bill is null)
        {
            return Invalid("Intake agent output is empty or null.");
        }

        // 2. Sum items[].amount, but only when every item has an amount.
        var items = bill.Items ?? [];
        decimal? itemsTotal = items.Count > 0 && items.All(i => i?.Amount is not null)
            ? items.Sum(i => i!.Amount!.Value)
            : null;

        // 3. Every required field must be present.
        var missing = MissingFields(bill);
        if (missing.Count > 0)
        {
            return new ValidationResult(false, $"Required field(s) are null or missing: {string.Join(", ", missing)}.", bill, itemsTotal, bill.PrintedTotal);
        }

        if (items.Any(i => i!.Amount < 0) || bill.PrintedTotal < 0)
        {
            return new ValidationResult(false, "Bill contains a negative amount.", bill, itemsTotal, bill.PrintedTotal);
        }

        // 4. Compare the computed total with the printed total.
        var difference = Math.Abs(itemsTotal!.Value - bill.PrintedTotal!.Value);
        if (difference > tolerance)
        {
            return new ValidationResult(false, $"Sum of items ({itemsTotal}) does not match printed total ({bill.PrintedTotal}); difference {difference}.", bill, itemsTotal, bill.PrintedTotal);
        }

        return new ValidationResult(true, "Totals match.", bill, itemsTotal, bill.PrintedTotal);
    }

    private static List<string> MissingFields(IntakeBill bill)
    {
        var missing = new List<string>();
        AddIfBlank(missing, "hospital", bill.Hospital);
        AddIfBlank(missing, "patient", bill.Patient);
        AddIfBlank(missing, "diagnosis", bill.Diagnosis);
        AddIfBlank(missing, "procedure", bill.Procedure);
        AddIfBlank(missing, "admissionDate", bill.AdmissionDate);
        AddIfBlank(missing, "dischargeDate", bill.DischargeDate);

        if (bill.PrintedTotal is null)
        {
            missing.Add("printedTotal");
        }

        if (bill.Items is null || bill.Items.Count == 0)
        {
            missing.Add("items");
            return missing;
        }

        for (var i = 0; i < bill.Items.Count; i++)
        {
            var item = bill.Items[i];
            if (item is null)
            {
                missing.Add($"items[{i}]");
                continue;
            }

            AddIfBlank(missing, $"items[{i}].description", item.Description);
            if (item.Amount is null)
            {
                missing.Add($"items[{i}].amount");
            }
        }

        return missing;
    }

    private static void AddIfBlank(List<string> missing, string name, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            missing.Add(name);
        }
    }

    // Models often wrap JSON in a ```json fence even when told not to. Remove one surrounding fence only.
    private static string StripCodeFence(string? text)
    {
        var trimmed = (text ?? string.Empty).Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal) || !trimmed.EndsWith("```", StringComparison.Ordinal) || trimmed.Length < 6)
        {
            return trimmed;
        }

        var firstNewLine = trimmed.IndexOf('\n');
        return firstNewLine < 0 ? trimmed : trimmed[(firstNewLine + 1)..^3].Trim();
    }

    private static ValidationResult Invalid(string reason) => new(false, reason, null, null, null);
}
