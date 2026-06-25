using System.Collections.Generic;
using System.Text.RegularExpressions;
using TicketService.Application.Interfaces.Services;

namespace TicketService.Infrastructure.Implements.Services;

/// <summary>Regex CCCD (12 số)/SĐT VN/email — chỉ cảnh báo, có thể false-positive (#519).</summary>
public class PiiDetector : IPiiDetector
{
    private static readonly Regex CccdRegex = new(@"\b\d{12}\b", RegexOptions.Compiled);
    private static readonly Regex PhoneRegex = new(@"\b0[35789]\d{8}\b", RegexOptions.Compiled);
    private static readonly Regex EmailRegex = new(@"[\w.+-]+@[\w-]+\.[\w]{2,}", RegexOptions.Compiled);

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
}
