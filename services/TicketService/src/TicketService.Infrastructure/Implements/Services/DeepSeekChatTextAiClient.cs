using System.Text.Json;
using Microsoft.Extensions.Options;
using TicketService.Application.Common.Models;
using TicketService.Application.Interfaces.Services;
using TicketService.Domain.Enums;

namespace TicketService.Infrastructure.Implements.Services;

/// <summary>
/// Gọi DeepSeek Chat Completions API (OpenAI-compatible) để phân tích sentiment,
/// tóm tắt, dịch và phát hiện ngôn ngữ.
/// Chọn provider bằng Chat:Provider = "DeepSeek" trong appsettings.
/// </summary>
public class DeepSeekChatTextAiClient : IChatTextAiClient
{
    private readonly DeepSeekChatAiClient _inner;
    private readonly ChatOptions _opts;

    public TranslationProviderEnum TranslationProvider => TranslationProviderEnum.DeepSeekAi;

    // Dùng lại HttpClient + CallAsync từ DeepSeekChatAiClient để tránh lặp HTTP logic.
    public DeepSeekChatTextAiClient(DeepSeekChatAiClient inner, IOptions<ChatOptions> opts)
    {
        _inner = inner;
        _opts = opts.Value;
    }

    public async Task<double> AnalyzeSentimentAsync(string chatContext, CancellationToken ct = default)
    {
        EnsureApiKey();
        var rawText = await _inner.CallAsync(_opts.DeepSeek.ApiKey, BuildSentimentPrompt(chatContext), temperature: 0.1, ct);

        using var doc = JsonDocument.Parse(rawText.Trim());
        return doc.RootElement.GetProperty("score").GetDouble();
    }

    public async Task<string> SummarizeAsync(string chatContext, int linesCount, CancellationToken ct = default)
    {
        EnsureApiKey();
        return (await _inner.CallAsync(_opts.DeepSeek.ApiKey, BuildSummarizePrompt(chatContext, linesCount), temperature: 0.3, ct)).Trim();
    }

    public async Task<string> TranslateAsync(string text, string targetLanguage, CancellationToken ct = default)
    {
        EnsureApiKey();
        return (await _inner.CallAsync(_opts.DeepSeek.ApiKey, BuildTranslatePrompt(text, targetLanguage), temperature: 0.1, ct)).Trim();
    }

    public async Task<string> DetectLanguageAsync(string text, CancellationToken ct = default)
    {
        EnsureApiKey();
        var rawText = (await _inner.CallAsync(_opts.DeepSeek.ApiKey, BuildDetectLanguagePrompt(text), temperature: 0.1, ct)).Trim();

        try
        {
            using var doc = JsonDocument.Parse(rawText);
            return doc.RootElement.GetProperty("lang").GetString() ?? "en";
        }
        catch
        {
            return "en";
        }
    }

    private void EnsureApiKey()
    {
        if (string.IsNullOrWhiteSpace(_opts.DeepSeek.ApiKey))
            throw new InvalidOperationException("DeepSeek API key is not configured.");
    }

    private static string BuildSentimentPrompt(string context)
        => $$"""
            Phân tích tone cảm xúc của các tin nhắn từ Customer dưới đây trong ngữ cảnh hỗ trợ kỹ thuật bảo trì pin lithium-ion.
            Trả về JSON theo định dạng chính xác: {"score": <float>}
            Trong đó <float> là số thực trong khoảng [-1.0, 1.0]:
            - 1.0: rất tích cực / hài lòng
            - 0.0: trung tính
            - -1.0: rất tiêu cực / tức giận / thất vọng
            Chỉ trả về JSON, không thêm bất kỳ văn bản nào khác.

            {{context}}
            """;

    private static string BuildSummarizePrompt(string context, int linesCount)
        => $$"""
            Tóm tắt nội dung cuộc hội thoại hỗ trợ kỹ thuật bảo trì pin lithium-ion dưới đây thành đúng {{linesCount}} dòng bullet ngắn gọn.
            Mỗi dòng bắt đầu bằng "- " và mô tả 1 ý chính (vấn đề, hành động đã thực hiện, kết quả, hoặc bước tiếp theo).
            Viết bằng tiếng Việt, súc tích, dễ đọc cho kỹ thuật viên mới tiếp nhận ticket.
            Chỉ trả về danh sách bullet, không thêm tiêu đề hay giải thích.

            {{context}}
            """;

    private static string BuildTranslatePrompt(string text, string targetLanguage)
        => $$"""
            Dịch đoạn văn bản dưới đây sang ngôn ngữ có mã ISO 639-1 là "{{targetLanguage}}".
            Chỉ trả về bản dịch, không thêm giải thích, ghi chú, hay tiêu đề.

            {{text}}
            """;

    private static string BuildDetectLanguagePrompt(string text)
        => $$"""
            Xác định ngôn ngữ của đoạn văn bản dưới đây và trả về mã ISO 639-1 (ví dụ: "vi", "en", "fr", "ja").
            Trả về JSON theo định dạng chính xác: {"lang": "<code>"}
            Chỉ trả về JSON, không thêm bất kỳ văn bản nào khác.

            {{text}}
            """;
}
