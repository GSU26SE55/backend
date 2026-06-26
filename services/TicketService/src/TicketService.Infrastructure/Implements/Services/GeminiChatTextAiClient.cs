using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using TicketService.Application.Common.Models;
using TicketService.Application.Interfaces.Services;

namespace TicketService.Infrastructure.Implements.Services;

/// <summary>
/// Gọi Gemini generateContent API để phân tích sentiment và tóm tắt chat thread (#560).
/// API key lấy từ Chat:Ai:ApiKey trong appsettings — dùng chung với GeminiChatAiClient.
/// </summary>
public class GeminiChatTextAiClient : IChatTextAiClient
{
    private readonly HttpClient _http;
    private readonly ChatOptions _opts;

    public GeminiChatTextAiClient(HttpClient http, IOptions<ChatOptions> opts)
    {
        _http = http;
        _opts = opts.Value;
    }

    public async Task<double> AnalyzeSentimentAsync(string chatContext, CancellationToken ct = default)
    {
        var apiKey = _opts.Ai.ApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("Gemini API key is not configured.");

        var prompt = BuildSentimentPrompt(chatContext);
        var url = $"{_opts.Ai.SuggestModelEndpoint}?key={apiKey}";

        var requestBody = new
        {
            contents = new[] { new { parts = new[] { new { text = prompt } } } }
        };

        using var response = await _http.PostAsJsonAsync(url, requestBody, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        var rawText = ExtractText(json);

        // Gemini trả JSON dạng {"score": 0.85} — parse score
        using var scoreJson = JsonDocument.Parse(rawText.Trim());
        return scoreJson.RootElement.GetProperty("score").GetDouble();
    }

    public async Task<string> SummarizeAsync(string chatContext, int linesCount, CancellationToken ct = default)
    {
        var apiKey = _opts.Ai.ApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("Gemini API key is not configured.");

        var prompt = BuildSummarizePrompt(chatContext, linesCount);
        var url = $"{_opts.Ai.SuggestModelEndpoint}?key={apiKey}";

        var requestBody = new
        {
            contents = new[] { new { parts = new[] { new { text = prompt } } } }
        };

        using var response = await _http.PostAsJsonAsync(url, requestBody, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        return ExtractText(json).Trim();
    }

    private static string ExtractText(JsonElement json)
        => json
            .GetProperty("candidates")[0]
            .GetProperty("content")
            .GetProperty("parts")[0]
            .GetProperty("text")
            .GetString() ?? string.Empty;

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
}
