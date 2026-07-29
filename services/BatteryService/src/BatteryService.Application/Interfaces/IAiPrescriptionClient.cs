using BatteryService.Application.Common.Models;

namespace BatteryService.Application.Interfaces;

/// <summary>
/// BE-AI — abstraction gọi AI /prescribe/ (enrich=true), transport-neutral.
/// Impl thật là <c>FallbackAiPrescriptionClient</c> (gRPC primary → HTTP fallback).
/// Gọi khi Alert P1/P2 để đổ action_steps/PPE vào ticket.
/// </summary>
public interface IAiPrescriptionClient
{
    /// <summary>
    /// Sinh prescription cho 1 pin. readings cùng format Predict (30 × [v,i,t,time], time = giây/cycle).
    /// </summary>
    /// <returns>
    /// <see cref="AiPrescriptionResult"/> nếu thành công; <c>null</c> nếu cả gRPC lẫn HTTP fail
    /// (caller bỏ qua enrichment — ticket vẫn tạo được từ Alert, chỉ thiếu prescription text).
    /// </returns>
    Task<AiPrescriptionResult?> PrescribeAsync(
        string batteryId,
        IReadOnlyList<double[]> readings,
        bool enrich = true,
        AiPackConfig? packConfig = null,
        CancellationToken cancellationToken = default);
}
