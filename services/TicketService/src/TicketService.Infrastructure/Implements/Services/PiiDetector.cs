using System.Collections.Generic;
using System.Text.RegularExpressions;
using SharedContracts.Interfaces;
using TicketService.Application.Interfaces.Services;

namespace TicketService.Infrastructure.Implements.Services;

/// <summary>Regex CCCD (12 số)/SĐT VN/email — chỉ cảnh báo, có thể false-positive (#519).</summary>
public class PiiDetector : IPiiDetector
{
    private static readonly Regex CccdRegex = new(@"\b\d{12}\b", RegexOptions.Compiled);
    private static readonly Regex PhoneRegex = new(@"\b0[35789]\d{8}\b", RegexOptions.Compiled);
    private static readonly Regex EmailRegex = new(@"[\w.+-]+@[\w-]+\.[\w]{2,}", RegexOptions.Compiled);

    private static readonly TimeSpan MaskTtl = TimeSpan.FromHours(1);

    private readonly ICacheService _cache;

    public PiiDetector(ICacheService cache) => _cache = cache;

    public bool ContainsPii(string body, out IReadOnlyList<string> matchedTypes)
    {
        var types = new List<string>();

        if (!string.IsNullOrWhiteSpace(body))
        {
            if (CccdRegex.IsMatch(body))
                types.Add("CCCD");
            if (PhoneRegex.IsMatch(body))
                types.Add("SĐT");
            if (EmailRegex.IsMatch(body))
                types.Add("Email");
        }

        matchedTypes = types;
        return types.Count > 0;
    }

    public async Task<(string MaskedText, string MaskKey)> MaskAsync(string text, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(text))
            return (string.Empty, string.Empty);

        var maskMap = new Dictionary<string, string>();
        var masked = text;

        masked = ReplaceMatches(masked, CccdRegex, "CCCD", maskMap);
        masked = ReplaceMatches(masked, PhoneRegex, "SĐT", maskMap);
        masked = ReplaceMatches(masked, EmailRegex, "EMAIL", maskMap);

        if (maskMap.Count == 0)
            return (masked, string.Empty);

        var maskKey = $"pii:mask:{Guid.NewGuid():N}";
        await _cache.SetAsync(maskKey, maskMap, MaskTtl, ct);

        return (masked, maskKey);
    }

    private static string ReplaceMatches(string text, Regex regex, string label, Dictionary<string, string> maskMap)
    {
        var counter = maskMap.Count(kv => kv.Key.StartsWith($"[{label}-"));
        return regex.Replace(text, match =>
        {
            counter++;
            var placeholder = $"[{label}-{counter}]";
            maskMap[placeholder] = match.Value;
            return placeholder;
        });
    }
}
