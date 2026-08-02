using TicketService.Domain.Enums;

namespace TicketService.Application.Interfaces.Services;

public interface IChatTextAiClient
{
    /// <summary>Provider enum dùng để ghi vào DB — mỗi implementation tự khai báo.</summary>
    TranslationProviderEnum TranslationProvider { get; }

    /// <summary>
    /// Tóm tắt <paramref name="chatContext"/> thành <paramref name="linesCount"/> dòng bullet.
    /// Throw exception nếu AI service không phản hồi.
    /// </summary>
    Task<string> SummarizeAsync(string chatContext, int linesCount, CancellationToken ct = default);

    /// <summary>
    /// Detects the source language and translates <paramref name="text"/> into <paramref name="targetLanguage"/>
    /// in a single API call. Returns (TranslatedBody, DetectedLanguage as ISO 639-1).
    /// Throw exception nếu AI service không phản hồi.
    /// </summary>
    Task<(string TranslatedBody, string DetectedLanguage)> TranslateWithDetectAsync(
        string text, string targetLanguage, string? knownSourceLanguage = null, CancellationToken ct = default);
}
