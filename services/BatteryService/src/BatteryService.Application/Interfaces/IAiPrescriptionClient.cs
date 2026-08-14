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
    /// <param name="context">
    /// Lịch sử pin (tuổi chu kỳ, lần bảo trì gần nhất, các lần sửa trước). CHỈ có tác dụng khi
    /// <paramref name="enrich"/>=true. <c>null</c> = không gửi, giữ nguyên hành vi cũ.
    /// </param>
    /// <param name="agentic">
    /// Bật chain agentic của AI (LLM tự sinh 3–5 truy vấn trước khi retrieve — 2 lượt LLM).
    /// Chỉ dùng cho thao tác THỦ CÔNG của người dùng; luồng auto-ticket phải để <c>false</c>
    /// vì nó tốn thêm một lượt LLM trong cùng ngân sách giờ.
    /// </param>
    Task<AiPrescriptionResult?> PrescribeAsync(
        string batteryId,
        IReadOnlyList<double[]> readings,
        bool enrich = true,
        AiPackConfig? packConfig = null,
        CancellationToken cancellationToken = default,
        AiPrescriptionContext? context = null,
        bool agentic = false);
}
