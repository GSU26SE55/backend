using System.Text;
using BatteryService.Application.CQRS.Query.Import;
using BatteryService.Application.DTOs.Import;
using BatteryService.Application.Import;
using BatteryService.Application.Interfaces;
using BatteryService.Application.Mapping;
using BatteryService.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using SharedInfrastructure.Extensions;

namespace BatteryService.Application.CQRS.Handler.Import;

public class GetImportBatchListQueryHandler
    : IRequestHandler<GetImportBatchListQuery, CommonResponse<PaginationResponse<ImportBatchDto>>>
{
    private readonly IBatteryUnitOfWork _unitOfWork;

    public GetImportBatchListQueryHandler(IBatteryUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task<CommonResponse<PaginationResponse<ImportBatchDto>>> Handle(
        GetImportBatchListQuery request, CancellationToken cancellationToken)
    {
        var query = _unitOfWork.ImportBatches
            .GetAllAsync()
            .AsNoTracking()
            .Where(batch => !batch.IsDeleted);

        if (request.Status.HasValue)
            query = query.Where(batch => batch.Status == request.Status.Value);


        var page = await query
            .OrderByDescending(batch => batch.CreatedAt)
            .Select(batch => new ImportBatchDto
            {
                Id = batch.Id.ToString(),
                FileName = batch.FileName,
                Status = batch.Status,
                IsDryRun = batch.IsDryRun,
                RequestedBy = batch.RequestedBy != null ? batch.RequestedBy.ToString() : null,
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
            })
            .ToPagedEntityListAsync(request.PageNumber, request.PageSize, cancellationToken);

        return new CommonResponse<PaginationResponse<ImportBatchDto>>
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = page
        };
    }
}

public class GetImportBatchByIdQueryHandler
    : IRequestHandler<GetImportBatchByIdQuery, CommonResponse<ImportBatchDto>>
{
    private readonly IBatteryUnitOfWork _unitOfWork;

    public GetImportBatchByIdQueryHandler(IBatteryUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task<CommonResponse<ImportBatchDto>> Handle(
        GetImportBatchByIdQuery request, CancellationToken cancellationToken)
    {
        var batch = await _unitOfWork.ImportBatches
            .GetAllAsync()
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == request.Id && !b.IsDeleted, cancellationToken);

        if (batch is null)
        {
            return new CommonResponse<ImportBatchDto>
            {
                IsSuccess = false,
                StatusCode = 404,
                Message = "Import batch not found."
            };
        }

        var rows = await _unitOfWork.ImportRows
            .GetAllAsync()
            .AsNoTracking()
            .Where(row => row.ImportBatchId == batch.Id && !row.IsDeleted)
            .ToListAsync(cancellationToken);

        return new CommonResponse<ImportBatchDto>
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = ImportMapper.ToDto(batch, rows)
        };
    }
}

public class GetImportRowsQueryHandler
    : IRequestHandler<GetImportRowsQuery, CommonResponse<PaginationResponse<ImportRowDto>>>
{
    private readonly IBatteryUnitOfWork _unitOfWork;

    public GetImportRowsQueryHandler(IBatteryUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task<CommonResponse<PaginationResponse<ImportRowDto>>> Handle(
        GetImportRowsQuery request, CancellationToken cancellationToken)
    {
        var query = _unitOfWork.ImportRows
            .GetAllAsync()
            .AsNoTracking()
            .Where(row => row.ImportBatchId == request.BatchId && !row.IsDeleted);

        if (request.Status.HasValue)
            query = query.Where(row => row.Status == request.Status.Value);

        if (request.EntityType.HasValue)
            query = query.Where(row => row.EntityType == request.EntityType.Value);

        // Sắp theo loại rồi tới số dòng để thứ tự khớp với thứ tự trong file gốc.
        var entities = await query
            .OrderBy(row => row.EntityType)
            .ThenBy(row => row.RowNumber)
            .ToPagedEntityListAsync(request.PageNumber, request.PageSize, cancellationToken);

        return new CommonResponse<PaginationResponse<ImportRowDto>>
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = entities.Map(ImportMapper.ToDto)
        };
    }
}

public class GetImportErrorsCsvQueryHandler
    : IRequestHandler<GetImportErrorsCsvQuery, CommonResponse<ImportFileDownloadDto>>
{
    private readonly IBatteryUnitOfWork _unitOfWork;

    public GetImportErrorsCsvQueryHandler(IBatteryUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task<CommonResponse<ImportFileDownloadDto>> Handle(
        GetImportErrorsCsvQuery request, CancellationToken cancellationToken)
    {
        var exists = await _unitOfWork.ImportBatches
            .GetAllAsync()
            .AnyAsync(batch => batch.Id == request.BatchId && !batch.IsDeleted, cancellationToken);

        if (!exists)
        {
            return new CommonResponse<ImportFileDownloadDto>
            {
                IsSuccess = false,
                StatusCode = 404,
                Message = "Import batch not found."
            };
        }

        var rows = await _unitOfWork.ImportRows
            .GetAllAsync()
            .AsNoTracking()
            .Where(row => row.ImportBatchId == request.BatchId && !row.IsDeleted
                          && (row.Status == ImportRowStatusEnum.Invalid || row.Status == ImportRowStatusEnum.Failed))
            .OrderBy(row => row.EntityType)
            .ThenBy(row => row.RowNumber)
            .ToListAsync(cancellationToken);

        var builder = new StringBuilder();
        builder.AppendLine("entity_type,row_number,external_ref,error_field,error_detail");

        foreach (var row in rows)
        {
            foreach (var error in ImportMapper.ParseErrors(row.ErrorsJson))
            {
                builder
                    .Append(Escape(row.EntityType.ToString())).Append(',')
                    .Append(row.RowNumber).Append(',')
                    .Append(Escape(row.ExternalRef)).Append(',')
                    .Append(Escape(error.Field)).Append(',')
                    .Append(Escape(error.Detail))
                    .AppendLine();
            }
        }

        return new CommonResponse<ImportFileDownloadDto>
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = new ImportFileDownloadDto
            {
                FileName = $"import-errors-{request.BatchId}.csv",
                // BOM để Excel mở file bằng bảng mã UTF-8; thiếu nó thì tiếng Việt trong phần mô tả
                // lỗi hiển thị sai và người dùng tưởng dữ liệu hỏng.
                Content = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(builder.ToString())).ToArray()
            }
        };
    }

    private static string Escape(string? value)
    {
        var text = value ?? string.Empty;
        return text.Contains(',') || text.Contains('"') || text.Contains('\n')
            ? "\"" + text.Replace("\"", "\"\"") + "\""
            : text;
    }
}

public class GetImportTemplateQueryHandler
    : IRequestHandler<GetImportTemplateQuery, CommonResponse<ImportFileDownloadDto>>
{
    private readonly IImportFileParser _parser;

    public GetImportTemplateQueryHandler(IImportFileParser parser) => _parser = parser;

    public Task<CommonResponse<ImportFileDownloadDto>> Handle(
        GetImportTemplateQuery request, CancellationToken cancellationToken)
    {
        var columns = _parser.TemplateColumns(request.EntityType);
        if (columns.Count == 0)
        {
            return Task.FromResult(new CommonResponse<ImportFileDownloadDto>
            {
                IsSuccess = false,
                StatusCode = 400,
                Message = "Unknown import entity type."
            });
        }

        var required = _parser.RequiredColumns(request.EntityType);
        var builder = new StringBuilder();
        builder.AppendLine(string.Join(",", columns));
        // Dòng thứ hai là chú thích cột nào bắt buộc. Người dùng xoá dòng này trước khi nhập liệu;
        // bộ đọc cũng bỏ qua nó vì mọi ô bắt buộc đều rỗng nên dòng sẽ báo lỗi rõ ràng nếu bị bỏ sót.
        builder.AppendLine(string.Join(",", columns.Select(column =>
            required.Contains(column, StringComparer.Ordinal) ? "REQUIRED" : "optional")));

        return Task.FromResult(new CommonResponse<ImportFileDownloadDto>
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = new ImportFileDownloadDto
            {
                FileName = $"import-template-{request.EntityType.ToString().ToLowerInvariant()}.csv",
                Content = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(builder.ToString())).ToArray()
            }
        });
    }
}
