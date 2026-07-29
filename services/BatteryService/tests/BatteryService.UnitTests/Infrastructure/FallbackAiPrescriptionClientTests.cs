using BatteryService.Application.Common.Models;
using BatteryService.Infrastructure.Implements.Ai;
using FluentAssertions;
using Grpc.Core;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace BatteryService.UnitTests.Infrastructure;

/// <summary>BE-AI — fallback gRPC → HTTP cho Prescribe (same policy as Predict).</summary>
public class FallbackAiPrescriptionClientTests
{
    private static readonly IReadOnlyList<double[]> Window =
        Enumerable.Range(0, 30).Select(i => new[] { 3.7, -1.2, 30.0, (double)i * 13 }).ToArray();

    private static AiPrescriptionResult Sample(string provider) => new(
        Prescription: "Replace battery", ActionSteps: new[] { "LOTO", "Measure" },
        PpeRequired: new[] { "Gloves" }, SopReferences: new[] { "SOP-1" },
        SafetyWarnings: Array.Empty<string>(), HumanVerificationRequired: true,
        Enriched: true, LlmProvider: provider);

    private static (FallbackAiPrescriptionClient sut, Mock<AiPrescriptionGrpcClient> grpc, Mock<AiPrescriptionHttpClient> http)
        Build()
    {
        var grpc = new Mock<AiPrescriptionGrpcClient>(MockBehavior.Strict, (AiModule.V1.AiService.AiServiceClient)null!);
        var http = new Mock<AiPrescriptionHttpClient>(
            MockBehavior.Strict, (HttpClient)null!, NullLogger<AiPrescriptionHttpClient>.Instance);
        var opts = Options.Create(new AiOptions { TimeoutSeconds = 5 });
        var sut = new FallbackAiPrescriptionClient(
            grpc.Object, http.Object, opts, NullLogger<FallbackAiPrescriptionClient>.Instance);
        return (sut, grpc, http);
    }

    [Fact]
    public async Task Prescribe_GrpcSucceeds_ReturnsGrpc()
    {
        var (sut, grpc, http) = Build();
        grpc.Setup(g => g.PrescribeAsync("B1", Window, true, It.IsAny<AiPackConfig?>(), 5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Sample("deepseek"));

        var result = await sut.PrescribeAsync("B1", Window);

        result!.LlmProvider.Should().Be("deepseek");
        http.Verify(h => h.PrescribeAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<double[]>>(),
            It.IsAny<bool>(), It.IsAny<AiPackConfig?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Prescribe_GrpcUnavailable_FallsBackToHttp()
    {
        var (sut, grpc, http) = Build();
        grpc.Setup(g => g.PrescribeAsync("B1", Window, true, It.IsAny<AiPackConfig?>(), 5, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RpcException(new Status(StatusCode.Unavailable, "down")));
        http.Setup(h => h.PrescribeAsync("B1", Window, true, It.IsAny<AiPackConfig?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Sample("http-gemini"));

        var result = await sut.PrescribeAsync("B1", Window);

        result!.LlmProvider.Should().Be("http-gemini");
    }

    [Fact]
    public async Task Prescribe_BothFail_ReturnsNull()
    {
        var (sut, grpc, http) = Build();
        grpc.Setup(g => g.PrescribeAsync("B1", Window, true, It.IsAny<AiPackConfig?>(), 5, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RpcException(new Status(StatusCode.Unavailable, "down")));
        http.Setup(h => h.PrescribeAsync("B1", Window, true, It.IsAny<AiPackConfig?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("down"));

        var result = await sut.PrescribeAsync("B1", Window);

        result.Should().BeNull(); // ticket vẫn tạo được, chỉ thiếu prescription
    }
}
