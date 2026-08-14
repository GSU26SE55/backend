using BatteryService.Application.DTOs;
using MediatR;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.CQRS.Query.SohPrediction;

/// <summary>
/// Dự đoán NHIỀU pin trong MỘT kết nối gRPC (bidi stream) — cho màn hình giám sát.
/// </summary>
/// <remarks>
/// N lần gọi unary tốn N round-trip; stream chỉ tốn một. Nhưng đánh đổi thật sự nằm ở chỗ
/// khác: bidi stream KHÔNG có lỗi theo từng message, nên một pin có cửa sổ sai sẽ làm đứt cả
/// lượt. Vì vậy response luôn kèm <c>isComplete</c> — pin thiếu kết quả là pin CHƯA ĐƯỢC CHẤM,
/// không phải pin bình thường.
/// </remarks>
public class GetBatchPredictionQuery : IRequest<CommonResponse<BatchPredictionDto>>
{
    /// <summary>Số pin tối đa mỗi lượt. Kẹp lại ở handler để một lượt không kéo dài vô hạn.</summary>
    public int Limit { get; set; } = 10;
}
