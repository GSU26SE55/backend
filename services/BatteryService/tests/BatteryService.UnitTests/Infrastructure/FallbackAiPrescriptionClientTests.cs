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
        grpc.Setup(g => g.PrescribeAsync("B1", Window, true, It.IsAny<AiPackConfig?>(), 30, It.IsAny<CancellationToken>(), It.IsAny<AiPrescriptionContext?>(), It.IsAny<bool>()))
            .ReturnsAsync(Sample("deepseek"));

        var result = await sut.PrescribeAsync("B1", Window);

        result!.LlmProvider.Should().Be("deepseek");
        http.Verify(h => h.PrescribeAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<double[]>>(),
            It.IsAny<bool>(), It.IsAny<AiPackConfig?>(), It.IsAny<CancellationToken>(), It.IsAny<AiPrescriptionContext?>(), It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public async Task Prescribe_Enriched_UsesWiderGrpcDeadline_NotThePredictTimeout()
    {
        // enrich=true chạy RAG + LLM nên mất vài giây. Bản HTTP đã được nới lên
        // Math.Max(30, TimeoutSeconds) từ lâu, nhưng đường gRPC thì chưa — hậu quả ĐO ĐƯỢC:
        // MỌI prescribe enrich=true đều DeadlineExceeded sau 5s rồi mới fallback sang HTTP.
        // Tốn thêm 5 giây mỗi lần, và log trông y hệt như AI đang hỏng.
        var (sut, grpc, _) = Build();
        grpc.Setup(g => g.PrescribeAsync("B1", Window, true, It.IsAny<AiPackConfig?>(), 30,
                It.IsAny<CancellationToken>(), It.IsAny<AiPrescriptionContext?>(), It.IsAny<bool>()))
            .ReturnsAsync(Sample("deepseek"));

        await sut.PrescribeAsync("B1", Window, enrich: true);

        // Deadline 5s (của Predict) KHÔNG được dùng cho đường enriched.
        grpc.Verify(g => g.PrescribeAsync("B1", Window, true, It.IsAny<AiPackConfig?>(), 5,
            It.IsAny<CancellationToken>(), It.IsAny<AiPrescriptionContext?>(), It.IsAny<bool>()),
            Times.Never);
    }

    [Fact]
    public async Task Prescribe_RuleBased_KeepsTightDeadline()
    {
        // enrich=false chạy <100ms. Nới deadline ở đây chỉ làm chậm việc phát hiện AI treo,
        // nên nhánh này phải giữ nguyên TimeoutSeconds gốc.
        var (sut, grpc, _) = Build();
        grpc.Setup(g => g.PrescribeAsync("B1", Window, false, It.IsAny<AiPackConfig?>(), 5,
                It.IsAny<CancellationToken>(), It.IsAny<AiPrescriptionContext?>(), It.IsAny<bool>()))
            .ReturnsAsync(Sample("none"));

        var result = await sut.PrescribeAsync("B1", Window, enrich: false);

        result!.LlmProvider.Should().Be("none");
    }

    [Fact]
    public async Task Prescribe_GrpcUnavailable_FallsBackToHttp()
    {
        var (sut, grpc, http) = Build();
        grpc.Setup(g => g.PrescribeAsync("B1", Window, true, It.IsAny<AiPackConfig?>(), 30, It.IsAny<CancellationToken>(), It.IsAny<AiPrescriptionContext?>(), It.IsAny<bool>()))
            .ThrowsAsync(new RpcException(new Status(StatusCode.Unavailable, "down")));
        http.Setup(h => h.PrescribeAsync("B1", Window, true, It.IsAny<AiPackConfig?>(), It.IsAny<CancellationToken>(), It.IsAny<AiPrescriptionContext?>(), It.IsAny<bool>()))
            .ReturnsAsync(Sample("http-gemini"));

        var result = await sut.PrescribeAsync("B1", Window);

        result!.LlmProvider.Should().Be("http-gemini");
    }

    [Fact]
    public async Task Prescribe_BothFail_ReturnsNull()
    {
        var (sut, grpc, http) = Build();
        grpc.Setup(g => g.PrescribeAsync("B1", Window, true, It.IsAny<AiPackConfig?>(), 30, It.IsAny<CancellationToken>(), It.IsAny<AiPrescriptionContext?>(), It.IsAny<bool>()))
            .ThrowsAsync(new RpcException(new Status(StatusCode.Unavailable, "down")));
        http.Setup(h => h.PrescribeAsync("B1", Window, true, It.IsAny<AiPackConfig?>(), It.IsAny<CancellationToken>(), It.IsAny<AiPrescriptionContext?>(), It.IsAny<bool>()))
            .ThrowsAsync(new HttpRequestException("down"));

        var result = await sut.PrescribeAsync("B1", Window);

        result.Should().BeNull(); // ticket vẫn tạo được, chỉ thiếu prescription
    }
}
