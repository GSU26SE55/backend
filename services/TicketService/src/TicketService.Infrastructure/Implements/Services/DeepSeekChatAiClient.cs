using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using TicketService.Application.Common.Models;
using TicketService.Application.Interfaces.Services;
using TicketService.Domain.Enums;

namespace TicketService.Infrastructure.Implements.Services;

/// <summary>
/// Gọi DeepSeek Chat Completions API (OpenAI-compatible) để sinh gợi ý chat.
/// Chọn provider bằng Chat:Provider = "DeepSeek" trong appsettings.
/// </summary>
public class DeepSeekChatAiClient : IChatAiSuggestionClient
{
    private readonly HttpClient _http;
    private readonly ChatOptions _opts;

    public DeepSeekChatAiClient(HttpClient http, IOptions<ChatOptions> opts)
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
        var apiKey = _opts.DeepSeek.ApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
            return Array.Empty<string>();

        var prompt = BuildPrompt(context, intent, category, count);
        var rawText = await CallAsync(apiKey, prompt, temperature: 0.7, ct);

        return rawText
            .Split("---", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Take(count)
            .ToList();
    }

    internal async Task<string> CallAsync(string apiKey, string userContent, double temperature, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, _opts.DeepSeek.ChatEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = JsonContent.Create(new
        {
            model = _opts.DeepSeek.Model,
            messages = new[] { new { role = "user", content = userContent } },
            temperature
        });

        using var response = await _http.SendAsync(request, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            throw new InvalidOperationException("RATE_LIMITED");
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        return json
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? string.Empty;
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
