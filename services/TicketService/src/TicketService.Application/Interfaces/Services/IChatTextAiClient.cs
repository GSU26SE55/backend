using TicketService.Domain.Enums;

namespace TicketService.Application.Interfaces.Services;

public interface IChatTextAiClient
{
    /// <summary>Provider enum dùng để ghi vào DB — mỗi implementation tự khai báo.</summary>
    TranslationProviderEnum TranslationProvider { get; }

    /// <summary>
    /// Phân tích tone của <paramref name="chatContext"/> và trả về score ∈ [-1.0, 1.0].
    /// Âm = tiêu cực, dương = tích cực. Throw exception nếu AI service không phản hồi.
    /// </summary>
    Task<double> AnalyzeSentimentAsync(string chatContext, CancellationToken ct = default);

    /// <summary>
    /// Tóm tắt <paramref name="chatContext"/> thành <paramref name="linesCount"/> dòng bullet.
    /// Throw exception nếu AI service không phản hồi.
    /// </summary>
    Task<string> SummarizeAsync(string chatContext, int linesCount, CancellationToken ct = default);

    /// <summary>
    /// Dịch <paramref name="text"/> sang <paramref name="targetLanguage"/> (ISO 639-1, ví dụ "en", "vi").
    /// Trả về bản dịch. Throw exception nếu AI service không phản hồi.
    /// </summary>
    Task<string> TranslateAsync(string text, string targetLanguage, CancellationToken ct = default);

    /// <summary>
    /// Detect ngôn ngữ của <paramref name="text"/> và trả về ISO 639-1 code (ví dụ "vi", "en").
    /// Throw exception nếu AI service không phản hồi.
    /// </summary>
    Task<string> DetectLanguageAsync(string text, CancellationToken ct = default);
}
