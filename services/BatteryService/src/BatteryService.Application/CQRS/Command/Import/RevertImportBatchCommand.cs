using BatteryService.Application.DTOs.Import;
using MediatR;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.CQRS.Command.Import;

/// <summary>Gỡ bỏ những gì một lô đã tạo ra.</summary>
/// <remarks>
/// Hoàn tác <b>không</b> đụng tới tài khoản khách hàng ở AuthService. Tài khoản có thể đã được dùng
/// để đăng nhập hoặc đã gắn với phiếu bảo trì; xoá đi là gây thiệt hại lớn hơn cái sai mà thao tác
/// hoàn tác định sửa. Phần này được nói rõ trong thông điệp trả về để người bấm không hiểu nhầm.
/// </remarks>
public class RevertImportBatchCommand : IRequest<CommonResponse<ImportBatchDto>>
{
    public Guid Id { get; set; }
}
