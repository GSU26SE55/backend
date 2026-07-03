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

    public async Task<(string TranslatedBody, string DetectedLanguage)> TranslateWithDetectAsync(
        string text, string targetLanguage, string? knownSourceLanguage = null, CancellationToken ct = default)
    {
        EnsureApiKey();

        // Source language known (from Lingua or chat.OriginalLanguage) — translate only, skip detection
        if (knownSourceLanguage != null && knownSourceLanguage != "und")
        {
            var translated = (await _inner.CallAsync(
                _opts.DeepSeek.ApiKey,
                BuildTranslateOnlyPrompt(text, knownSourceLanguage, targetLanguage),
                temperature: 0.1,
                ct)).Trim();
            return (translated, knownSourceLanguage);
        }

        // Source unknown — detect + translate in one call
        var rawText = (await _inner.CallAsync(
            _opts.DeepSeek.ApiKey,
            BuildTranslateWithDetectPrompt(text, targetLanguage),
            temperature: 0.1,
            ct)).Trim();

        try
        {
            using var doc = JsonDocument.Parse(rawText);
            var translated = doc.RootElement.GetProperty("translated").GetString() ?? text;
            var sourceLang = doc.RootElement.GetProperty("source_lang").GetString() ?? "und";
            return (translated, sourceLang);
        }
        catch
        {
            return (rawText, "und");
        }
    }

    private void EnsureApiKey()
    {
        if (string.IsNullOrWhiteSpace(_opts.DeepSeek.ApiKey))
            throw new InvalidOperationException("DeepSeek API key is not configured.");
    }

    private static string BuildSentimentPrompt(string context)
        => $$"""
            You are a sentiment analysis assistant for a solar lithium-ion battery maintenance support system.
            Analyze the emotional tone of the customer messages below and return a sentiment score.

            Return ONLY valid JSON in this exact format: {"score": <float>}
            Where <float> is a number in the range [-1.0, 1.0]:
            - 1.0 = very positive / highly satisfied
            - 0.0 = neutral
            - -1.0 = very negative / angry / frustrated

            Return ONLY the JSON object — no explanations, no extra text.

            {{context}}
            """;

    private static string BuildSummarizePrompt(string context, int linesCount)
        => $$"""
            You are a technical summarization assistant for a solar lithium-ion battery maintenance support system (ITIL-based service desk).
            Summarize the support conversation below into exactly {{linesCount}} concise bullet points.

            Rules:
            - Each bullet starts with "- " and describes one key point: the issue, action taken, outcome, or next step.
            - Write in Vietnamese (vi) — the summary is for a technician taking over the ticket.
            - Keep technical terms and abbreviations as-is (SOH, SLA, P1/P2/P3, BMS, etc.).
            - Be concise and factual — avoid filler words.

            Return ONLY the bullet list — no title, no preamble, no extra text.

            {{context}}
            """;

    private static string BuildTranslateOnlyPrompt(string text, string sourceLanguage, string targetLanguage)
        => $$"""
            You are a specialized translation assistant for a solar lithium-ion battery maintenance support system (ITIL-based service desk).
            Translate the text below FROM {{sourceLanguage}} INTO the language with ISO 639-1 code "{{targetLanguage}}".

            Rules:
            - Use domain-appropriate technical equivalents — do NOT translate word-by-word.
            - Preserve these abbreviations unchanged in all languages: SOH, SLA, P1, P2, P3, BMS, MPPT, kWh, kWp, DC, AC, Li-ion, V, A, °C.
            - Technical term glossary (vi ↔ en):
              • State of Health ↔ Trạng thái sức khỏe pin
              • Anomaly / Anomaly detected ↔ Dị thường / Phát hiện dị thường
              • Degrading ↔ Suy giảm hiệu suất
              • Alert threshold / Threshold ↔ Ngưỡng cảnh báo
              • Maintenance log ↔ Nhật ký bảo trì
              • Escalate / Escalation ↔ Chuyển cấp xử lý
              • Resolution ↔ Kết quả xử lý
              • Ticket ↔ Ticket (keep as-is in all languages)
              • Overheat ↔ Quá nhiệt
              • Overvoltage / Undervoltage ↔ Quá áp / Thấp áp
              • Cell imbalance ↔ Mất cân bằng cell
              • Short circuit ↔ Ngắn mạch
              • Charging cycle ↔ Chu kỳ sạc
              • Capacity fade ↔ Suy giảm dung lượng
            - Preserve the original tone exactly (formal stays formal, urgent stays urgent).
            - Return ONLY the translated text — no labels, no explanations, no extra formatting.

            {{text}}
            """;

    private static string BuildTranslateWithDetectPrompt(string text, string targetLanguage)
        => $$"""
            You are a specialized translation assistant for a solar lithium-ion battery maintenance support system (ITIL-based service desk).

            Task: Detect the source language of the text below, then translate it into the language with ISO 639-1 code "{{targetLanguage}}".

            Rules:
            - Use domain-appropriate technical equivalents — do NOT translate word-by-word.
            - Preserve these abbreviations unchanged in all languages: SOH, SLA, P1, P2, P3, BMS, MPPT, kWh, kWp, DC, AC, Li-ion, V, A, °C.
            - Technical term glossary (vi ↔ en):
              • State of Health ↔ Trạng thái sức khỏe pin
              • Anomaly / Anomaly detected ↔ Dị thường / Phát hiện dị thường
              • Degrading ↔ Suy giảm hiệu suất
              • Alert threshold / Threshold ↔ Ngưỡng cảnh báo
              • Maintenance log ↔ Nhật ký bảo trì
              • Escalate / Escalation ↔ Chuyển cấp xử lý
              • Resolution ↔ Kết quả xử lý
              • Ticket ↔ Ticket (keep as-is in all languages)
              • Overheat ↔ Quá nhiệt
              • Overvoltage / Undervoltage ↔ Quá áp / Thấp áp
              • Cell imbalance ↔ Mất cân bằng cell
              • Short circuit ↔ Ngắn mạch
              • Charging cycle ↔ Chu kỳ sạc
              • Capacity fade ↔ Suy giảm dung lượng
            - Preserve the original tone exactly (formal stays formal, urgent stays urgent).
            - If the source language is already "{{targetLanguage}}", set "translated" to the original text unchanged.

            Return ONLY valid JSON (no markdown, no extra text):
            {"translated": "<translated text>", "source_lang": "<ISO 639-1 code of source language>"}

            Text to translate:
            {{text}}
            """;
}
