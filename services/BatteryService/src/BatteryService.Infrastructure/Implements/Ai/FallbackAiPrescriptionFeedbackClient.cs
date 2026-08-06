using BatteryService.Application.Common.Models;
using BatteryService.Application.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BatteryService.Infrastructure.Implements.Ai;

/// <summary>
/// BE-AI — composite phản hồi prescription: gRPC PRIMARY → HTTP FALLBACK.
/// </summary>
/// <remarks>
/// <para>
/// Trước đây đường phản hồi CHỈ có HTTP vì proto chưa khai RPC nào cho nó. Nay proto đã có
/// <c>rpc SubmitFeedback</c>, nên nó theo đúng khuôn fallback của Predict/Prescribe.
/// </para>
/// <para>
/// Điểm khác Predict đáng lưu ý: <see cref="AiFeedbackOutcome.NotFound"/> KHÔNG được fallback.
/// Nó nghĩa là AI không biết <c>prescriptionId</c> đó — HTTP cũng đọc cùng một history store nên
/// sẽ trả 404 y hệt. Fallback ở đây chỉ tốn thêm một round-trip để nhận cùng câu trả lời, và tệ
/// hơn là làm log trông như hạ tầng trục trặc trong khi thực ra dữ liệu đã hết hạn.
/// </para>
/// </remarks>
public class FallbackAiPrescriptionFeedbackClient : IAiPrescriptionFeedbackClient
{
    private readonly AiPrescriptionFeedbackGrpcClient _grpc;
    private readonly AiPrescriptionHttpClient _http;
    private readonly AiOptions _options;
    private readonly ILogger<FallbackAiPrescriptionFeedbackClient> _logger;

    public FallbackAiPrescriptionFeedbackClient(
        AiPrescriptionFeedbackGrpcClient grpc,
        AiPrescriptionHttpClient http,
        IOptions<AiOptions> options,
        ILogger<FallbackAiPrescriptionFeedbackClient> logger)
    {
        _grpc = grpc;
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<AiFeedbackOutcome> SubmitFeedbackAsync(
        string prescriptionId,
        string status,
        IReadOnlyList<string>? editedSteps,
        string? note,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var outcome = await _grpc.SubmitFeedbackAsync(
                prescriptionId, status, editedSteps, note,
                _options.TimeoutSeconds, cancellationToken);

            // NotFound là câu trả lời DỨT KHOÁT của AI, không phải sự cố truyền tải.
            if (outcome is AiFeedbackOutcome.Recorded or AiFeedbackOutcome.NotFound)
                return outcome;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex, "[AiPrescriptionFeedback] gRPC lỗi cho {Id} — thử HTTP.", prescriptionId);
        }

        return await _http.SubmitFeedbackAsync(
            prescriptionId, status, editedSteps, note, cancellationToken);
    }
}
