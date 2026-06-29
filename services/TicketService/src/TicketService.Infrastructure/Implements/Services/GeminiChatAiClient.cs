using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using TicketService.Application.Common.Models;
using TicketService.Application.Interfaces.Services;
using TicketService.Domain.Enums;

namespace TicketService.Infrastructure.Implements.Services;

/// <summary>
/// Gọi Gemini generateContent API để sinh gợi ý chat (#559).
/// API key lấy từ Chat:Ai:ApiKey trong appsettings — chỉ dùng free tier khi test.
/// </summary>
public class GeminiChatAiClient : IChatAiSuggestionClient
{
    private readonly HttpClient _http;
    private readonly ChatOptions _opts;

    public GeminiChatAiClient(HttpClient http, IOptions<ChatOptions> opts)
    {
        _http = http;
        _opts = opts.Value;
    }

    public async Task<IReadOnlyList<string>> SuggestAsync(
        string context,
        ChatAiIntentEnum intent,
        string? category,
        int count,
        CancellationToken ct = default)
    {
        var apiKey = _opts.Ai.ApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
            return Array.Empty<string>();

        var prompt = BuildPrompt(context, intent, category, count);

        var requestBody = new
        {
            contents = new[]
            {
                new { parts = new[] { new { text = prompt } } }
            }
        };

        var url = $"{_opts.Ai.SuggestModelEndpoint}?key={apiKey}";
        using var response = await _http.PostAsJsonAsync(url, requestBody, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);

        var rawText = json
            .GetProperty("candidates")[0]
            .GetProperty("content")
            .GetProperty("parts")[0]
            .GetProperty("text")
            .GetString() ?? string.Empty;

        return rawText
            .Split("---", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Take(count)
            .ToList();
    }

    private static string BuildPrompt(string context, ChatAiIntentEnum intent, string? category, int count)
    {
        var intentLabel = intent switch
        {
            ChatAiIntentEnum.RequestInfo => "yêu cầu thêm thông tin",
            ChatAiIntentEnum.TechnicalAnswer => "trả lời kỹ thuật",
            ChatAiIntentEnum.Resolution => "đề xuất giải pháp xử lý",
            ChatAiIntentEnum.FollowUp => "theo dõi tiến độ",
            _ => "hỗ trợ kỹ thuật"
        };

        var categoryNote = string.IsNullOrWhiteSpace(category) ? "" : $" thuộc danh mục {category}";

        return $"""
                Bạn là nhân viên kỹ thuật hỗ trợ bảo trì pin lithium-ion{categoryNote}.
                Dựa vào thông tin ticket dưới đây, hãy đề xuất {count} câu trả lời chat ngắn gọn (1–3 câu mỗi gợi ý) theo phong cách {intentLabel}.
                Phân tách mỗi gợi ý bằng dòng chứa đúng 3 dấu gạch ngang: ---
                Chỉ trả về các gợi ý, không thêm tiêu đề hay giải thích.

                {context}
                """;
    }
}
