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

    // Minimum confidence to trust Lingua's result — below this, fall back to AI detection.
    // With only 2 languages loaded, Lingua always picks one; high threshold avoids false positives
    // on short or ambiguous text (e.g. single words like "nguyên", non-EN/VI text).
    private const double ConfidenceThreshold = 0.90;
    private const int MinWordCount = 4;

    public string Detect(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "und";

        var wordCount = text.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        if (wordCount < MinWordCount)
            return "und";

        var confidences = _detector.ComputeLanguageConfidenceValues(text);

        if (confidences.Count == 0)
            return "und";

        var top = confidences.MaxBy(kv => kv.Value);
        if (top.Value < ConfidenceThreshold)
            return "und";

        return top.Key switch
        {
            Language.English => "en",
            Language.Vietnamese => "vi",
            _ => "und",
        };
    }
}
