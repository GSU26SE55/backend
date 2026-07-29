using BatteryService.Application.Common.Models;

namespace BatteryService.Application.Interfaces;

/// <summary>
/// BE-AI — abstraction gọi AI /predict, transport-neutral.
/// Impl thật là <c>FallbackAiPredictionClient</c> (gRPC primary → HTTP fallback).
/// </summary>
public interface IAiPredictionClient
{
    /// <summary>
    /// Chạy 1 prediction cho 1 pin.
    /// </summary>
    /// <param name="batteryId">ID pin (dùng cho AI battery_history + trace).</param>
    /// <param name="readings">
    /// Đúng 30 timestep, mỗi timestep = [voltage, current, temperature, time].
    /// ⚠️ <c>time</c> = GIÂY tương đối trong 1 cycle (KHÔNG phải DateTime/epoch) —
    /// caller phải convert trước. Voltage/current/temperature = double (từ decimal).
    /// </param>
    /// <returns>
    /// <see cref="AiPredictionResult"/> nếu thành công; <c>null</c> nếu cả gRPC lẫn
    /// HTTP đều fail, hoặc input bị AI reject (INVALID_ARGUMENT) — caller no-op.
    /// </returns>
    Task<AiPredictionResult?> PredictAsync(
        string batteryId,
        IReadOnlyList<double[]> readings,
        AiPackConfig? packConfig = null,
        CancellationToken cancellationToken = default);
}
