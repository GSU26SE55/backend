using Lingua;
using TicketService.Application.Interfaces.Services;

namespace TicketService.Infrastructure.Implements.Services;

/// <summary>
/// Local language detector using Lingua — no API call, no network dependency.
/// Loads only English and Vietnamese models (~20 MB) instead of the full set (~400 MB).
/// Registered as Singleton so models are built once per process lifetime.
/// </summary>
public class LinguaLanguageDetectionService : ILanguageDetectionService
{
    private readonly LanguageDetector _detector =
        LanguageDetectorBuilder
            .FromLanguages(Language.English, Language.Vietnamese)
            .Build();

    public string Detect(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "und";

        var result = _detector.DetectLanguageOf(text);
        return result switch
        {
            Language.English => "en",
            Language.Vietnamese => "vi",
            _ => "und",
        };
    }
}
