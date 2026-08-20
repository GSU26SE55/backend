using BatteryService.Domain.Enums;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.DTOs.Import;

/// <summary>Tóm tắt một lô import, dùng cho cả màn danh sách lẫn màn chi tiết.</summary>
public class ImportBatchDto
{
    public string Id { get; set; } = string.Empty;

    public string? FileName { get; set; }

    public ImportBatchStatusEnum Status { get; set; }

    public bool IsDryRun { get; set; }

    public string? RequestedBy { get; set; }

    public int TotalRows { get; set; }

    public int ValidRows { get; set; }

    public int InvalidRows { get; set; }

    public int CreatedRows { get; set; }

    public int UpdatedRows { get; set; }

    public int SkippedRows { get; set; }

    public int FailedRows { get; set; }

    /// <summary>Số dòng đã xử lý xong ở pha ghi — dùng vẽ thanh tiến độ.</summary>
    public int ProcessedRows => CreatedRows + UpdatedRows + SkippedRows + FailedRows;

    public DateTime? StartedAt { get; set; }

    public DateTime? CompletedAt { get; set; }

    public string? ErrorSummary { get; set; }

    public DateTime CreatedAt { get; set; }

    /// <summary>Số dòng theo từng loại dữ liệu có trong lô.</summary>
    public List<ImportEntityCountDto> EntityCounts { get; set; } = new();
}

public class ImportEntityCountDto
{
    public ImportEntityTypeEnum EntityType { get; set; }

    public int Total { get; set; }

    public int Invalid { get; set; }
}

/// <summary>Một dòng của lô, kèm lỗi nếu có.</summary>
public class ImportRowDto
{
    public string Id { get; set; } = string.Empty;

    public int RowNumber { get; set; }

    public ImportEntityTypeEnum EntityType { get; set; }

    public string ExternalRef { get; set; } = string.Empty;

    public ImportRowStatusEnum Status { get; set; }

    public List<Errors> Errors { get; set; } = new();

    public string? CreatedEntityId { get; set; }

    public DateTime? ProcessedAt { get; set; }
}
