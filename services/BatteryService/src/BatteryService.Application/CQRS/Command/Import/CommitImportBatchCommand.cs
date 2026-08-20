using BatteryService.Application.DTOs.Import;
using MediatR;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.CQRS.Command.Import;

/// <summary>
/// Chuyển một lô đã kiểm định sang chế độ ghi thật.
/// </summary>
/// <remarks>
/// Lệnh này chỉ đổi trạng thái rồi trả về ngay. Việc ghi do tiến trình nền làm, vì lô có thể phải
/// chờ tài khoản khách hàng đồng bộ qua message bus — giữ kết nối HTTP suốt quãng đó là cách chắc
/// chắn nhất để nhận một lỗi hết thời gian chờ ở giữa chừng.
/// </remarks>
public class CommitImportBatchCommand : IRequest<CommonResponse<ImportBatchDto>>
{
    public Guid Id { get; set; }
}
