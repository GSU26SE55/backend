using System.Text.RegularExpressions;
using TicketService.Application.Interfaces.Services;

namespace TicketService.Infrastructure.Implements.Services;

/// <summary>
/// Thay thế abbreviation kỹ thuật bằng [[term_N]] trước khi gửi AI, restore sau khi nhận kết quả.
/// Không dùng Redis — mask và unmask xảy ra trong cùng 1 request (#639).
/// </summary>
public class TechnicalTermMasker : ITechnicalTermMasker
{
    // Sorted longest-first để tránh partial match (e.g. "kWh" trước "kW").
    private static readonly IReadOnlyList<string> Terms =
    [
        "Li-ion", "MPPT", "kWh", "kWp",
        "SOH", "SLA", "BMS", "ITIL",
        "P1", "P2", "P3",
        "DC", "AC",
    ];

    // Pre-compiled regex per term; \b = word boundary.
    private static readonly IReadOnlyList<Regex> TermRegexes = Terms
        .Select(t => new Regex($@"\b{Regex.Escape(t)}\b", RegexOptions.Compiled | RegexOptions.IgnoreCase))
        .ToList();

    public (string MaskedText, IReadOnlyDictionary<string, string> TermMap) Mask(string text)
    {
        if (string.IsNullOrEmpty(text))
            return (text, new Dictionary<string, string>());

        var termMap = new Dictionary<string, string>();
        var counter = 0;
        var masked = text;

        for (var i = 0; i < TermRegexes.Count; i++)
        {
            var regex = TermRegexes[i];
            masked = regex.Replace(masked, match =>
            {
                counter++;
                var placeholder = $"[[term_{counter}]]";
                termMap[placeholder] = match.Value;
                return placeholder;
            });
        }

        return (masked, termMap);
    }

    public string Unmask(string text, IReadOnlyDictionary<string, string> termMap)
    {
        if (string.IsNullOrEmpty(text) || termMap.Count == 0)
            return text;

        foreach (var (placeholder, original) in termMap)
            text = text.Replace(placeholder, original, StringComparison.OrdinalIgnoreCase);

        return text;
    }
}
