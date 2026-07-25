using BatteryService.Application.Common.Models;
using BatteryService.Domain.Enums;
using BatteryService.Infrastructure.Implements.Ai;
using FluentAssertions;
using Grpc.Core;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace BatteryService.UnitTests.Infrastructure;

/// <summary>
/// BE-AI — unit tests cho logic fallback gRPC → HTTP của FallbackAiPredictionClient.
/// Mock 2 client con (virtual PredictAsync) để kiểm chứng đúng transport được gọi.
/// </summary>
public class FallbackAiPredictionClientTests
{
    private static readonly IReadOnlyList<double[]> Window =
        Enumerable.Range(0, 30).Select(i => new[] { 3.9, -1.0, 25.0, (double)i * 13 }).ToArray();

    private static AiPredictionResult SampleResult(string src) => new(
        SohPercent: 84.5m, Confidence: 0.82m, Classification: AnomalyClassificationEnum.Degrading,
        AnomalyScore: -0.12m, AnomalyConfidence: 0.85m, RulCyclesEstimate: 30, Priority: "P2", ModelVersion: src, LatencyMs: 40);

    private static (FallbackAiPredictionClient sut, Mock<AiPredictionGrpcClient> grpc, Mock<AiPredictionHttpClient> http)
        Build()
    {
        var grpc = new Mock<AiPredictionGrpcClient>(MockBehavior.Strict, (AiModule.V1.AiService.AiServiceClient)null!);
        var http = new Mock<AiPredictionHttpClient>(
            MockBehavior.Strict, (HttpClient)null!, NullLogger<AiPredictionHttpClient>.Instance);
        var opts = Options.Create(new AiOptions { TimeoutSeconds = 5 });
        var sut = new FallbackAiPredictionClient(
            grpc.Object, http.Object, opts, NullLogger<FallbackAiPredictionClient>.Instance);
        return (sut, grpc, http);
    }

    [Fact]
    public async Task Predict_GrpcSucceeds_ReturnsGrpcResult_DoesNotCallHttp()
    {
        var (sut, grpc, http) = Build();
        grpc.Setup(g => g.PredictAsync("B1", Window, It.IsAny<AiPackConfig?>(), 5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(SampleResult("grpc"));

        var result = await sut.PredictAsync("B1", Window);

        result!.ModelVersion.Should().Be("grpc");
        http.Verify(h => h.PredictAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<double[]>>(),
            It.IsAny<AiPackConfig?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Predict_GrpcUnavailable_FallsBackToHttp()
    {
        var (sut, grpc, http) = Build();
        grpc.Setup(g => g.PredictAsync("B1", Window, It.IsAny<AiPackConfig?>(), 5, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RpcException(new Status(StatusCode.Unavailable, "down")));
        http.Setup(h => h.PredictAsync("B1", Window, It.IsAny<AiPackConfig?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SampleResult("http"));

        var result = await sut.PredictAsync("B1", Window);

        result!.ModelVersion.Should().Be("http"); // đã fallback
    }

    [Fact]
    public async Task Predict_GrpcInvalidArgument_DoesNotFallback_ReturnsNull()
    {
        var (sut, grpc, http) = Build();
        grpc.Setup(g => g.PredictAsync("B1", Window, It.IsAny<AiPackConfig?>(), 5, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RpcException(new Status(StatusCode.InvalidArgument, "bad window")));

        var result = await sut.PredictAsync("B1", Window);

        result.Should().BeNull();
        http.Verify(h => h.PredictAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<double[]>>(),
            It.IsAny<AiPackConfig?>(), It.IsAny<CancellationToken>()), Times.Never); // KHÔNG fallback khi input sai
    }

    [Fact]
    public async Task Predict_BothTransportsFail_ReturnsNull()
    {
        var (sut, grpc, http) = Build();
        grpc.Setup(g => g.PredictAsync("B1", Window, It.IsAny<AiPackConfig?>(), 5, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RpcException(new Status(StatusCode.Unavailable, "down")));
        http.Setup(h => h.PredictAsync("B1", Window, It.IsAny<AiPackConfig?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("http down too"));

        var result = await sut.PredictAsync("B1", Window);

        result.Should().BeNull(); // no-op — caller skip pin, threshold rule vẫn chạy
    }
}
