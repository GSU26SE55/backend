using EmailService.Infrastructure.Templates;

namespace EmailService.IntegrationTests.Fixtures;

/// <summary>
/// Stub IEmailTemplateRenderer cho integration test: capture template name + values
/// và trả về một HTML đoán được để assert.
/// </summary>
public class RecordingTemplateRenderer : IEmailTemplateRenderer
{
    private readonly List<RenderCall> _calls = new();
    private readonly object _gate = new();

    public IReadOnlyList<RenderCall> Calls
    {
        get
        {
            lock (_gate)
            {
                return _calls.ToList();
            }
        }
    }

    public Func<string, IReadOnlyDictionary<string, string?>, string> RenderResultFactory { get; set; }
        = (template, values) => $"<html data-template=\"{template}\">OTP={values.GetValueOrDefault("Otp")}</html>";

    public RenderCall? LastCall
    {
        get
        {
            lock (_gate)
            {
                return _calls.LastOrDefault();
            }
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _calls.Clear();
        }
    }

    public Task<string> RenderAsync(
        string templateName,
        IReadOnlyDictionary<string, string?> values,
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            _calls.Add(new RenderCall
            {
                TemplateName = templateName,
                Values = new Dictionary<string, string?>(values)
            });
        }

        return Task.FromResult(RenderResultFactory(templateName, values));
    }
}

public class RenderCall
{
    public string TemplateName { get; init; } = string.Empty;
    public Dictionary<string, string?> Values { get; init; } = new();
}
