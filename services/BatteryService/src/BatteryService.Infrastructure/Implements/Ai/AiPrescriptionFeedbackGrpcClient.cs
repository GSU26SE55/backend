using AiModule.V1;
using BatteryService.Application.Interfaces;
using Grpc.Core;
using Microsoft.Extensions.Logging;

namespace BatteryService.Infrastructure.Implements.Ai;

/// <summary>
/// BE-AI — gRPC impl của phản hồi prescription (PRIMARY). Gọi <c>AiService.SubmitFeedback</c>.
/// </summary>
/// <remarks>
/// Ánh xạ status code sang <see cref="AiFeedbackOutcome"/> phải khớp đúng ngữ nghĩa của bản HTTP:
/// <list type="bullet">
/// <item><c>NotFound</c> → <see cref="AiFeedbackOutcome.NotFound"/> — AI không biết id này.
/// Retry vô ích, và quan trọng hơn: KHÔNG được fallback sang HTTP, vì HTTP sẽ trả 404 y hệt.</item>
/// <item><c>InvalidArgument</c> → cũng là <see cref="AiFeedbackOutcome.NotFound"/>: status sai
/// giá trị là lỗi của caller, thử lại bằng transport khác cũng cho cùng kết quả.</item>
/// <item>Còn lại (Unavailable/DeadlineExceeded/Internal) → ném ra để composite thử HTTP.</item>
/// </list>
/// </remarks>
public class AiPrescriptionFeedbackGrpcClient
{
    private readonly AiService.AiServiceClient _client;
    private readonly ILogger<AiPrescriptionFeedbackGrpcClient> _logger;

    public AiPrescriptionFeedbackGrpcClient(
        AiService.AiServiceClient client,
        ILogger<AiPrescriptionFeedbackGrpcClient> logger)
    {
        _client = client;
        _logger = logger;
    }

    // virtual: cho Moq override trong unit test composite (AsyncUnaryCall khó stub).
    public virtual async Task<AiFeedbackOutcome> SubmitFeedbackAsync(
        string prescriptionId,
        string status,
        IReadOnlyList<string>? editedSteps,
        string? note,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        var request = new SubmitFeedbackRequest
        {
            PrescriptionId = prescriptionId,
            Status = status,
            Note = note ?? string.Empty,
        };
        if (editedSteps is not null)
            request.EditedSteps.AddRange(editedSteps);

        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
            await _client.SubmitFeedbackAsync(
                request, deadline: deadline, cancellationToken: cancellationToken);
            return AiFeedbackOutcome.Recorded;
        }
        catch (RpcException ex) when (ex.StatusCode is StatusCode.NotFound or StatusCode.InvalidArgument)
        {
            _logger.LogWarning(
                "[AiPrescriptionFeedback] AI từ chối prescriptionId {Id} ({Status}) — không retry.",
                prescriptionId, ex.StatusCode);
            return AiFeedbackOutcome.NotFound;
        }
    }
}
