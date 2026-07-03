namespace TicketService.Application.Interfaces.Services;

public interface ILanguageDetectionService
{
    /// <summary>
    /// Detects the language of <paramref name="text"/> locally (no API call).
    /// Returns an ISO 639-1 code ("en", "vi") or "und" when confidence is too low to determine.
    /// </summary>
    string Detect(string text);
}
