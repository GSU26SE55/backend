using BatteryService.Application.DTOs;
using MediatR;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.CQRS.Query.SohPrediction;

/// <summary>
/// SOH từ chuỗi dài cho một pin — phân tích lịch sử, KHÔNG dùng để tạo ticket.
/// </summary>
/// <remarks>
/// Đường này không có anomaly/risk (phía AI cố ý bỏ: IsolationForest fit trên window=30,
/// chấm chuỗi 4096 bước bằng nó sẽ ra số vô nghĩa). Ai gọi để quyết định tạo ticket là dùng sai.
/// </remarks>
public class GetLongSohQuery : IRequest<CommonResponse<LongSohDto>>
{
    public Guid BatteryAssetId { get; set; }

    /// <summary>
    /// Số timestep lấy về. AI nhận 31..4096; giá trị ngoài dải bị kẹp lại ở handler thay vì
    /// để AI từ chối — người dùng gõ 5000 không nên nhận lỗi, mà nên nhận 4096.
    /// </summary>
    public int Limit { get; set; } = 512;
}
