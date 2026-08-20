using System.Text.Json;
using BatteryService.Application.DTOs.Import;
using BatteryService.Domain.Entities;
using BatteryService.Domain.Enums;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.Mapping;

/// <summary>Ánh xạ thực thể nhập dữ liệu sang DTO. Không dùng thư viện ánh xạ, theo quy ước dự án.</summary>
public static class ImportMapper
{
    public static ImportBatchDto ToDto(ImportBatch batch, IReadOnlyList<ImportRow>? rows)
    {
        var dto = new ImportBatchDto
        {
            Id = batch.Id.ToString(),
            FileName = batch.FileName,
            Status = batch.Status,
            IsDryRun = batch.IsDryRun,
            RequestedBy = batch.RequestedBy?.ToString(),
            TotalRows = batch.TotalRows,
            ValidRows = batch.ValidRows,
            InvalidRows = batch.InvalidRows,
            CreatedRows = batch.CreatedRows,
            UpdatedRows = batch.UpdatedRows,
            SkippedRows = batch.SkippedRows,
            FailedRows = batch.FailedRows,
            StartedAt = batch.StartedAt,
            CompletedAt = batch.CompletedAt,
            ErrorSummary = batch.ErrorSummary,
            CreatedAt = batch.CreatedAt
        };

        if (rows is { Count: > 0 })
        {
            dto.EntityCounts = rows
                .GroupBy(row => row.EntityType)
                .OrderBy(group => group.Key)
                .Select(group => new ImportEntityCountDto
                {
                    EntityType = group.Key,
                    Total = group.Count(),
                    Invalid = group.Count(row => row.Status == ImportRowStatusEnum.Invalid)
                })
                .ToList();
        }

        return dto;
    }

    public static ImportRowDto ToDto(ImportRow row) => new()
    {
        Id = row.Id.ToString(),
        RowNumber = row.RowNumber,
        EntityType = row.EntityType,
        ExternalRef = row.ExternalRef,
        Status = row.Status,
        Errors = ParseErrors(row.ErrorsJson),
        CreatedEntityId = row.CreatedEntityId?.ToString(),
        ProcessedAt = row.ProcessedAt
    };


    /// <summary>
    /// Đọc danh sách lỗi đã lưu. Nội dung hỏng thì trả về một lỗi mô tả đúng chuyện đó thay vì ném
    /// ngoại lệ — một dòng lưu sai không được phép làm sập cả màn xem kết quả.
    /// </summary>
    public static List<Errors> ParseErrors(string? errorsJson)
    {
        if (string.IsNullOrWhiteSpace(errorsJson))
            return new List<Errors>();

        try
        {
            return JsonSerializer.Deserialize<List<Errors>>(errorsJson) ?? new List<Errors>();
        }
        catch (JsonException)
        {
            return new List<Errors>
            {
                new() { Field = "errors", Detail = "The stored error detail could not be read." }
            };
        }
    }
}
