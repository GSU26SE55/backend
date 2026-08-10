using BatteryService.Application.DTOs;
using MediatR;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.CQRS.Command.Alert;

/// <summary>
/// Kỹ thuật viên bấm "AI gợi ý chi tiết" trên một alert — chạy lại <c>Prescribe</c> ở chế độ
/// đầy đủ (<c>enrich=true</c>, tuỳ chọn <c>agentic=true</c>) và trả thẳng kết quả cho UI.
/// </summary>
/// <remarks>
/// <para>
/// Vì sao cần endpoint riêng thay vì dùng lại prescription của alert: prescription tự động được
/// sinh trên đường event, nơi phải giữ ngân sách LLM cho MỌI pin nên luôn chạy
/// <c>agentic=false</c>. Đây là thao tác THỦ CÔNG cho đúng một pin, nên mới đáng bật chain
/// agentic (2 lượt LLM: sinh truy vấn → truy hồi đa truy vấn).
/// </para>
/// <para>
/// Trước đó <c>IAiPrescriptionClient</c> CHỈ được gọi từ background job — không có đường nào để
/// người dùng chủ động hỏi AI, dù <c>enrich</c>/<c>agentic</c> sinh ra chính cho việc đó.
/// </para>
/// </remarks>
public class RegenerateAiPrescriptionCommand : IRequest<CommonResponse<AiPrescriptionDto>>
{
    /// <summary>Gán từ route, không nhận từ body.</summary>
    public Guid AlertId { get; set; }

    /// <summary>
    /// Bật chain agentic (2 lượt LLM). Mặc định <c>false</c> — bật khi kết quả thường không đủ sát.
    /// </summary>
    public bool Agentic { get; set; }
}
