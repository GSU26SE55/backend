using BatteryService.Application.DTOs.Import;
using BatteryService.Domain.Enums;
using MediatR;
using SharedContracts.Common.Requests;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.CQRS.Query.Import;

/// <summary>Danh sách lô import, mới nhất trước.</summary>
public class GetImportBatchListQuery : PaginationRequest, IRequest<CommonResponse<PaginationResponse<ImportBatchDto>>>
{
    public ImportBatchStatusEnum? Status { get; set; }
}

/// <summary>Chi tiết một lô, kèm số dòng theo từng loại dữ liệu.</summary>
public class GetImportBatchByIdQuery : IRequest<CommonResponse<ImportBatchDto>>
{
    public Guid Id { get; set; }
}

/// <summary>Các dòng của một lô, lọc được theo trạng thái và loại.</summary>
public class GetImportRowsQuery : PaginationRequest, IRequest<CommonResponse<PaginationResponse<ImportRowDto>>>
{
    public Guid BatchId { get; set; }

    public ImportRowStatusEnum? Status { get; set; }

    public ImportEntityTypeEnum? EntityType { get; set; }
}

/// <summary>Kết xuất các dòng hỏng thành CSV để người dùng sửa rồi nạp lại.</summary>
public class GetImportErrorsCsvQuery : IRequest<CommonResponse<ImportFileDownloadDto>>
{
    public Guid BatchId { get; set; }
}

/// <summary>File CSV mẫu cho một loại dữ liệu.</summary>
public class GetImportTemplateQuery : IRequest<CommonResponse<ImportFileDownloadDto>>
{
    public ImportEntityTypeEnum EntityType { get; set; }
}

/// <summary>Nội dung file trả về cho người dùng tải xuống.</summary>
public class ImportFileDownloadDto
{
    public string FileName { get; set; } = string.Empty;

    public string ContentType { get; set; } = "text/csv";

    public byte[] Content { get; set; } = Array.Empty<byte>();
}
