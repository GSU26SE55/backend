using AiModule.V1;
using BatteryService.Application.Common.Models;
using BatteryService.Application.Interfaces;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BatteryService.Infrastructure.Implements.Ai;

/// <summary>
/// C10 — dự đoán nhiều pin trong một kết nối qua bidi stream <c>PredictStream</c>.
/// </summary>
/// <remarks>
/// <para>
/// KHÔNG có HTTP fallback: REST không có endpoint streaming tương ứng. Caller cần chắc chắn
/// có kết quả thì dùng <see cref="IAiPredictionClient"/> (unary, có fallback) cho từng pin.
/// </para>
/// <para>
/// Hợp đồng quan trọng nhất của đường này: <b>một cửa sổ sai làm abort CẢ stream</b>. Bidi
/// không có lỗi theo từng message, nên sau k−1 response hợp lệ, message thứ k sai sẽ kết thúc
/// stream bằng một status lỗi duy nhất. Vì vậy client này KHÔNG ném lỗi khi đứt giữa chừng —
/// nó trả về những gì đã nhận kèm <c>AbortReason</c>, để caller phân biệt "pin bình thường"
/// với "pin chưa được chấm".
/// </para>
/// </remarks>
public class AiPredictionStreamGrpcClient : IAiPredictionStreamClient
{
    private readonly AiService.AiServiceClient _client;
    private readonly AiOptions _options;
    private readonly ILogger<AiPredictionStreamGrpcClient> _logger;

    public AiPredictionStreamGrpcClient(
        AiService.AiServiceClient client,
        IOptions<AiOptions> options,
        ILogger<AiPredictionStreamGrpcClient> logger)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<AiPredictionStreamResult> PredictManyAsync(
        IReadOnlyList<AiPredictionBatchItem> items,
        CancellationToken cancellationToken = default)
    {
        if (items.Count == 0)
            return new AiPredictionStreamResult(Array.Empty<AiPredictionResult>(), 0, null);

        var results = new List<AiPredictionResult>(items.Count);
        string? abortReason = null;

        // Deadline theo SỐ LƯỢNG, không phải hằng số: timeout của một lượt unary nhân lên
        // cho cả lô sẽ cắt ngang stream đúng lúc nó đang làm việc bình thường.
        var deadline = DateTime.UtcNow.AddSeconds(_options.TimeoutSeconds * Math.Max(1, items.Count));

        try
        {
            using var call = _client.PredictStream(
                deadline: deadline, cancellationToken: cancellationToken);

            // Đọc song song với ghi. Đọc tuần tự sau khi ghi hết sẽ chặn ở buffer khi lô lớn:
            // server không thể đẩy response nếu không ai đọc, mà client thì đang chờ ghi xong.
            var reader = Task.Run(async () =>
            {
                await foreach (var resp in call.ResponseStream.ReadAllAsync(cancellationToken))
                    results.Add(Map(resp));
            }, cancellationToken);

            foreach (var item in items)
            {
                var request = new PredictRequest { BatteryId = item.BatteryId };
                foreach (var row in item.Readings)
                {
                    var reading = new Reading();
                    reading.Values.AddRange(row);
                    request.Readings.Add(reading);
                }
                if (item.PackConfig is not null)
                {
                    request.PackConfig = new PackConfig { NSeries = item.PackConfig.NSeries };
                    if (item.PackConfig.Chemistry is not null)
                        request.PackConfig.Chemistry = item.PackConfig.Chemistry;
                    if (item.PackConfig.CapacityAh is not null)
                        request.PackConfig.CapacityAh = item.PackConfig.CapacityAh.Value;
                }
                await call.RequestStream.WriteAsync(request, cancellationToken);
            }

            await call.RequestStream.CompleteAsync();
            await reader;
        }
        catch (RpcException ex)
        {
            abortReason = $"{ex.StatusCode}: {ex.Status.Detail}";
            _logger.LogWarning(
                "PredictStream đứt sau {Got}/{Want} kết quả ({Code}). Những pin còn lại CHƯA "
                + "được chấm — không được hiểu là bình thường.",
                results.Count, items.Count, ex.StatusCode);
        }
        catch (Exception ex)
        {
            abortReason = ex.Message;
            _logger.LogWarning(ex, "PredictStream lỗi sau {Got}/{Want} kết quả.", results.Count, items.Count);
        }

        return new AiPredictionStreamResult(results, items.Count, abortReason);
    }

    private static AiPredictionResult Map(PredictResponse resp) => new(
        SohPercent: (decimal)resp.SohPercent,
        Confidence: (decimal)resp.Confidence,
        Classification: AiPredictionResult.ParseClassification(resp.Classification),
        AnomalyScore: (decimal)resp.AnomalyScore,
        AnomalyConfidence: (decimal)(resp.Anomaly?.AnomalyConfidence ?? 0d),
        RulCyclesEstimate: resp.RulCyclesEstimate,
        Priority: resp.Risk?.Priority ?? "None",
        ModelVersion: resp.Metadata?.ModelVersion ?? string.Empty,
        LatencyMs: (int)Math.Round(resp.InferenceMs),
        RawResponse: Google.Protobuf.JsonFormatter.Default.Format(resp),
        HealthStage: resp.Prediction?.HealthStage,
        StageConfidence: resp.Prediction is null ? null : (decimal)resp.Prediction.StageConfidence,
        IsBorderline: resp.Prediction?.IsBorderline ?? false,
        SohStd: resp.Prediction is null ? null : (decimal)resp.Prediction.SohStd,
        RiskLevel: resp.Risk?.RiskLevel,
        ActionCode: resp.Risk?.ActionCode,
        SohTrend: resp.SohTrend,
        DegradationRatePerCycle: (decimal)resp.DegradationRatePerCycle,
        CyclesToMaintenance: resp.CyclesToMaintenance,
        IsTemperatureOod: resp.Metadata?.IsTemperatureOod ?? false);
}
