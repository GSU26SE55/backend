using System.Net;
using System.Text;
using BatteryService.Application.Common.Models;
using BatteryService.Domain.Enums;
using BatteryService.Infrastructure.Implements.Ai;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace BatteryService.UnitTests.Infrastructure;

/// <summary>
/// GH-805 — parse <c>warnings[]</c> + <c>risk.risk_level</c> + <c>risk.action_code</c> từ response
/// /predict. Trước đây <c>PredictAsync</c> không có test nào (0% coverage) nên phần parse mới sẽ
/// không được bảo vệ: nếu AI đổi shape response, alert im lặng mất AnomalyType đúng.
/// </summary>
public class AiPredictionHttpClientTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        public HttpStatusCode Status { get; init; } = HttpStatusCode.OK;
        public string ResponseBody { get; init; } = "";

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(Status)
            {
                Content = new StringContent(ResponseBody, Encoding.UTF8, "application/json"),
            });
        }
    }

    private static AiPredictionHttpClient Make(string body, HttpStatusCode status = HttpStatusCode.OK)
    {
        var http = new HttpClient(new StubHandler { Status = status, ResponseBody = body })
        {
            BaseAddress = new Uri("http://ai-module-http:8000"),
        };
        return new AiPredictionHttpClient(http, NullLogger<AiPredictionHttpClient>.Instance);
    }

    private static Task<AiPredictionResult?> PredictAsync(AiPredictionHttpClient client)
        => client.PredictAsync("battery-1", new[] { new[] { 3.9, -1.0, 25.0, 0.0 } }, null, CancellationToken.None);

    /// <summary>Đúng payload repro của issue #805: Normal + P1 + TEMP_CRITICAL.</summary>
    [Fact]
    public async Task PredictAsync_NormalWithCriticalRisk_ParsesRiskAndWarnings()
    {
        var json = """
        {
          "soh_percent": 95.0,
          "classification": "Normal",
          "confidence": 0.91,
          "anomaly_score": 0.0,
          "rul_cycles_estimate": 400,
          "inference_ms": 42.0,
          "anomaly": { "anomaly_confidence": 0.12 },
          "risk": { "risk_level": "Critical", "priority": "P1", "action_code": "INSPECT_THERMAL" },
          "metadata": { "model_version": "1.6" },
          "warnings": [
            { "code": "TEMP_CRITICAL", "severity": "critical", "message": "Temperature 50C exceeds limit" }
          ]
        }
        """;

        var result = await PredictAsync(Make(json));

        result.Should().NotBeNull();
        result!.Classification.Should().Be(AnomalyClassificationEnum.Normal);
        result.Priority.Should().Be("P1");
        result.RiskLevel.Should().Be("Critical");
        result.ActionCode.Should().Be("INSPECT_THERMAL");

        result.Warnings.Should().ContainSingle();
        result.Warnings[0].Code.Should().Be("TEMP_CRITICAL");
        result.Warnings[0].Severity.Should().Be("critical");
        result.Warnings[0].Message.Should().Be("Temperature 50C exceeds limit");

        // Chuỗi nối end-to-end: response này PHẢI dẫn tới alert Overheat Critical.
        AiPredictionResult.ResolveSeverity(result.Classification, result.Priority)
            .Should().Be(AlertSeverityEnum.Critical);
        AiPredictionResult.MapWarningToAnomalyType(result.Warnings)
            .Should().Be(AnomalyTypeEnum.Overheat);
    }

    [Fact]
    public async Task PredictAsync_ResponseWithoutRiskOrWarnings_FallsBackSafely()
    {
        // Response cũ (trước khi AI thêm risk/warnings) — không được ném lỗi, không được đoán bừa.
        var json = """
        {
          "soh_percent": 88.0,
          "classification": "Degrading",
          "confidence": 0.8,
          "anomaly_score": -0.2,
          "rul_cycles_estimate": 120,
          "inference_ms": 30.0
        }
        """;

        var result = await PredictAsync(Make(json));

        result.Should().NotBeNull();
        result!.Priority.Should().Be("None");
        result.RiskLevel.Should().BeNull();
        result.ActionCode.Should().BeNull();
        result.Warnings.Should().BeEmpty("không có warnings → list rỗng, KHÔNG null");

        AiPredictionResult.MapWarningToAnomalyType(result.Warnings)
            .Should().Be(AnomalyTypeEnum.SohDegradation);
    }

    [Fact]
    public async Task PredictAsync_WarningsNotAnArray_DoesNotThrow()
    {
        // Phòng thủ shape lạ: warnings là object thay vì array → bỏ qua, không làm chết tick.
        var json = """
        {
          "soh_percent": 70.0,
          "classification": "Failed",
          "inference_ms": 25.0,
          "risk": { "risk_level": "Critical", "priority": "P1" },
          "warnings": { "code": "TEMP_CRITICAL" }
        }
        """;

        var result = await PredictAsync(Make(json));

        result.Should().NotBeNull();
        result!.Warnings.Should().BeEmpty();
        result.RiskLevel.Should().Be("Critical");
    }

    [Fact]
    public async Task PredictAsync_NonSuccessStatus_ReturnsNull()
    {
        var result = await PredictAsync(Make("boom", HttpStatusCode.InternalServerError));

        result.Should().BeNull("AI lỗi → skip pin, không làm chết tick");
    }
}
