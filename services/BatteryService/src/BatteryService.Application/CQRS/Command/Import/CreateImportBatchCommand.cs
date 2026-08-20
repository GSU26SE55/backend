using BatteryService.Application.DTOs.Import;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;

namespace BatteryService.Application.CQRS.Command.Import;

/// <summary>
/// Nhận file dữ liệu của bên thứ ba, đọc và kiểm định, ghi kết quả vào bảng trung chuyển.
/// </summary>
/// <remarks>
/// Lệnh này <b>không bao giờ</b> ghi dữ liệu nghiệp vụ, kể cả khi <see cref="DryRun"/> là false.
/// Nó chỉ dựng lô và các dòng; việc ghi thật do lệnh ghi và tiến trình nền đảm nhiệm. Tách như vậy
/// để một file lớn không giữ kết nối HTTP suốt quá trình ghi, và để người dùng luôn có cơ hội xem
/// trước kết quả kiểm định.
/// </remarks>
public class CreateImportBatchCommand
    : IRequest<CommonResponse<ImportBatchDto>>, IValidatable<CommonResponse<ImportBatchDto>>
{
    /// <summary>Nội dung file khách hàng. Null khi lô không có phần khách hàng.</summary>
    public byte[]? CustomersCsv { get; set; }

    public byte[]? SitesCsv { get; set; }

    public byte[]? AssetsCsv { get; set; }

    /// <summary>Tên file để hiển thị, ghép từ các file đã gửi.</summary>
    public string? FileName { get; set; }

    /// <summary>True = chỉ kiểm định. False = kiểm định xong thì sẵn sàng cho lệnh ghi.</summary>
    public bool DryRun { get; set; } = true;

    /// <summary>Tài khoản Admin bấm nạp. Controller đặt từ phiên đăng nhập.</summary>
    public Guid? RequestedBy { get; set; }

    public Task<CommonResponse<ImportBatchDto>> ValidateAsync()
    {
        var response = new CommonResponse<ImportBatchDto>();

        var hasAnyFile = HasContent(CustomersCsv) || HasContent(SitesCsv) || HasContent(AssetsCsv);

        if (!hasAnyFile)
        {
            response.ListErrors.Add(new Errors
            {
                Field = "files",
                Detail = "Provide at least one CSV file: customers, sites, or assets."
            });
        }

        // Pin phải thuộc về một site, và site phải thuộc về một khách hàng. Nạp riêng lẻ từng loại
        // vẫn hợp lệ — miễn là các loại phía trên đã được nạp ở lô trước và có bản đồ liên kết.
        if (FileName?.Length > 255)
        {
            response.ListErrors.Add(new Errors
            {
                Field = nameof(FileName),
                Detail = "File name must not exceed 255 characters."
            });
        }

        if (response.ListErrors.Count > 0)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Invalid import request.";
        }

        return Task.FromResult(response);
    }

    private static bool HasContent(byte[]? content) => content is { Length: > 0 };
}
