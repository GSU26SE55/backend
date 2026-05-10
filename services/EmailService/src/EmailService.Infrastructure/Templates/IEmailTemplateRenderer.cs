namespace EmailService.Infrastructure.Templates;

public interface IEmailTemplateRenderer
{
    Task<string> RenderAsync(
        string templateName,
        IReadOnlyDictionary<string, string?> values,
        CancellationToken cancellationToken = default);
}
