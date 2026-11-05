using System.Text;

namespace ClaimOrchestrator.Safety;

/// <summary>Helpers for text that comes from the bill file or from agent output, both of which are untrusted.</summary>
public static class UntrustedText
{
    /// <summary>
    /// Removes control characters (which could carry terminal escape codes or break prompt formatting),
    /// collapses whitespace and quotes, and cuts the value to maxLength.
    /// </summary>
    public static string Clean(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(Math.Min(value.Length, maxLength));
        foreach (var c in value)
        {
            if (builder.Length >= maxLength)
            {
                break;
            }

            if (char.IsControl(c) || char.IsWhiteSpace(c))
            {
                // Collapse any run of whitespace or control characters into one space.
                if (builder.Length > 0 && builder[^1] != ' ')
                {
                    builder.Append(' ');
                }
            }
            else
            {
                // Double quotes delimit values in the policy question, so swap them for single quotes.
                builder.Append(c == '"' ? '\'' : c);
            }
        }

        return builder.ToString().Trim();
    }

    /// <summary>Removes control characters except newlines and tabs, for printing multi-line agent answers.</summary>
    public static string ForDisplay(string? value) =>
        string.IsNullOrEmpty(value)
            ? string.Empty
            : new string(value.Where(c => !char.IsControl(c) || c is '\n' or '\r' or '\t').ToArray());
}
