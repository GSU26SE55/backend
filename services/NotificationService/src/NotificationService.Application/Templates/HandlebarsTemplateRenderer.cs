using System.Collections.Concurrent;
using HandlebarsDotNet;

namespace NotificationService.Application.Templates;

public class HandlebarsTemplateRenderer : ITemplateRenderer
{
    // NoEscape = true: email templates use UTF-8 — HTML-entity encoding of Vietnamese diacritics is not wanted
    private static readonly IHandlebars _handlebars = Handlebars.Create(new HandlebarsConfiguration { NoEscape = true });
    private static readonly ConcurrentDictionary<string, HandlebarsTemplate<object, object>> _cache = new();

    public string Render(string templateName, object data)
    {
        var compiled = _cache.GetOrAdd(templateName, LoadAndCompile);
        return compiled(data);
    }

    private static HandlebarsTemplate<object, object> LoadAndCompile(string templateName)
    {
        var assembly = typeof(HandlebarsTemplateRenderer).Assembly;
        var resourceName = $"NotificationService.Application.Templates.{templateName}.hbs";

        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"Email template '{templateName}.hbs' không tìm thấy. " +
                $"Kiểm tra EmbeddedResource trong csproj và tên file có đúng không. " +
                $"Resource name expected: '{resourceName}'");

        using var reader = new StreamReader(stream);
        return _handlebars.Compile(reader.ReadToEnd());
    }
}
