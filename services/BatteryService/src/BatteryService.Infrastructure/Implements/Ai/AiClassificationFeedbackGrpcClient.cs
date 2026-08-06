using AiModule.V1;
using BatteryService.Application.Interfaces;
using BatteryService.Domain.Enums;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using AiOptionsModel = BatteryService.Application.Common.Models.AiOptions;

namespace BatteryService.Infrastructure.Implements.Ai;

/// <summary>
/// F4 — gửi phản hồi phân loại về AI qua gRPC <c>SubmitClassificationFeedback</c>.
/// </summary>
/// <remarks>
/// Không có nhánh HTTP fallback ở đây, và đó là chủ ý: phản hồi KHÔNG nằm trên đường nghiệp vụ.
/// Nó đã được lưu trong <c>anomaly_classifications</c> của BE trước khi gọi, nên mất một lần
/// gửi chỉ làm chậm vòng học. Thêm fallback để cứu một thao tác không ai chờ là phức tạp thừa;
/// thứ đáng làm hơn là một job đồng bộ lại các bản ghi chưa gửi được — việc của issue khác.
/// </remarks>
public class AiClassificationFeedbackGrpcClient : IAiClassificationFeedbackClient
{
    private readonly AiService.AiServiceClient _client;
    private readonly AiOptionsModel _options;
    private readonly ILogger<AiClassificationFeedbackGrpcClient> _logger;

    public AiClassificationFeedbackGrpcClient(
        AiService.AiServiceClient client,
        IOptions<AiOptionsModel> options,
        ILogger<AiClassificationFeedbackGrpcClient> logger)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<bool> SubmitAsync(
        Guid batteryAssetId,
        AnomalyClassificationEnum classification,
        StaffFeedbackEnum feedback,
        string modelVersion,
        DateTime classifiedAt,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
            return false;

        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(_options.TimeoutSeconds);
            await _client.SubmitClassificationFeedbackAsync(
                new ClassificationFeedbackRequest
                {
                    BatteryId = batteryAssetId.ToString(),
                    Classification = ToAiClassification(classification),
                    Verdict = ToAiVerdict(feedback),
                    ModelVersion = modelVersion ?? string.Empty,
                    ClassifiedAt = classifiedAt.ToUniversalTime().ToString("o"),
                },
                deadline: deadline,
                cancellationToken: cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            // Warning chứ không Error: thao tác của người dùng ĐÃ thành công, đây chỉ là
            // phần học thêm. Nâng lên Error sẽ làm alert vận hành kêu vì một việc không gấp.
            _logger.LogWarning(
                ex, "Không gửi được phản hồi phân loại cho asset {AssetId} về AI.", batteryAssetId);
            return false;
        }
    }

    /// <summary>Enum BE → chuỗi hợp đồng AI. Ánh xạ tường minh, không dựa vào <c>ToString()</c>.</summary>
    /// <remarks>
    /// Dùng <c>ToString()</c> sẽ khiến việc đổi tên một thành viên enum ở BE lặng lẽ phá hợp
    /// đồng với AI — không lỗi biên dịch, không lỗi chạy, chỉ có nhãn rác trong file retrain.
    /// </remarks>
    private static string ToAiClassification(AnomalyClassificationEnum c) => c switch
    {
        AnomalyClassificationEnum.Normal => "Normal",
        AnomalyClassificationEnum.Degrading => "Degrading",
        AnomalyClassificationEnum.Failed => "Failed",
        _ => throw new ArgumentOutOfRangeException(nameof(c), c, "Nhãn phân loại lạ."),
    };

    private static string ToAiVerdict(StaffFeedbackEnum f) => f switch
    {
        StaffFeedbackEnum.Correct => "correct",
        StaffFeedbackEnum.FalsePositive => "false_positive",
        StaffFeedbackEnum.FalseNegative => "false_negative",
        _ => throw new ArgumentOutOfRangeException(nameof(f), f, "Giá trị feedback lạ."),
    };
}
