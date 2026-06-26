namespace TicketService.Application.Interfaces.Services;

public interface IChatTextAiClient
{
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
}
