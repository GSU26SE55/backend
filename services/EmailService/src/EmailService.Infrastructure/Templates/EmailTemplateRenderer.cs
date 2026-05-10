using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace EmailService.Infrastructure.Templates;

public class EmailTemplateRenderer : IEmailTemplateRenderer
{
    private static readonly Regex PlaceholderRegex =
        new(@"\{\{\s*(?<key>[A-Za-z0-9_]+)\s*\}\}", RegexOptions.Compiled);

    private readonly ConcurrentDictionary<string, string> _cache = new();
    private readonly string _templateRoot;
    private readonly bool _cacheEnabled;

    public EmailTemplateRenderer(IHostEnvironment env, IConfiguration configuration)
    {
        var configured = configuration["EmailTemplates:Path"];
        var relative = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine("wwwroot", "email-templates")
            : configured;

        _templateRoot = Path.IsPathRooted(relative)
            ? relative
            : Path.Combine(env.ContentRootPath, relative);

        _cacheEnabled = !bool.TryParse(configuration["EmailTemplates:CacheDisabled"], out var disabled) || !disabled;
    }

    public async Task<string> RenderAsync(
        string templateName,
        IReadOnlyDictionary<string, string?> values,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(templateName))
            throw new ArgumentException("Template name is required.", nameof(templateName));

        var template = await LoadTemplateAsync(templateName, cancellationToken);
        return Replace(template, values);
    }

    private async Task<string> LoadTemplateAsync(string templateName, CancellationToken cancellationToken)
    {
        if (_cacheEnabled && _cache.TryGetValue(templateName, out var cached))
            return cached;

        var path = Path.Combine(_templateRoot, $"{templateName}.html");
        if (!File.Exists(path))
            throw new FileNotFoundException($"Email template '{templateName}' not found.", path);

        var content = await File.ReadAllTextAsync(path, Encoding.UTF8, cancellationToken);

        if (_cacheEnabled)
            _cache[templateName] = content;

        return content;
    }

    private static string Replace(string template, IReadOnlyDictionary<string, string?> values)
    {
        return PlaceholderRegex.Replace(template, match =>
        {
            var key = match.Groups["key"].Value;
            return values.TryGetValue(key, out var value) ? value ?? string.Empty : match.Value;
        });
    }
}
