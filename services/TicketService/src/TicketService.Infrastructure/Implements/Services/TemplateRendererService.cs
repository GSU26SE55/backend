using System.Text.RegularExpressions;
using TicketService.Application.Interfaces.Services;

namespace TicketService.Infrastructure.Implements.Services;

/// <summary>
/// Simple mustache-style template renderer: {{key}} placeholders replaced from context dictionary.
/// </summary>
public class TemplateRendererService : ITemplateRenderer
{
    private static readonly Regex PlaceholderRegex = new(@"\{\{(\w+)\}\}", RegexOptions.Compiled);

    public string Render(string template, object? context = null)
    {
        if (string.IsNullOrEmpty(template))
            return template;

        if (context == null)
            return template;

        var dict = context as IDictionary<string, string>
            ?? ConvertToDictionary(context);

        return PlaceholderRegex.Replace(template, match =>
        {
            var key = match.Groups[1].Value;
            return dict.TryGetValue(key, out var value) ? value : match.Value;
        });
    }

    private static IDictionary<string, string> ConvertToDictionary(object obj)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in obj.GetType().GetProperties())
        {
            var val = prop.GetValue(obj)?.ToString();
            if (val != null)
                dict[prop.Name] = val;
        }
        return dict;
    }
}
