using System.Text;
using BatteryService.Application.CQRS.Query.Import;
using BatteryService.Application.DTOs.Import;
using BatteryService.Application.Import;
using BatteryService.Application.Interfaces;
using BatteryService.Application.Mapping;
using BatteryService.Domain.Enums;
using ClosedXML.Excel;
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
    private const string XlsxContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private readonly IImportFileParser _parser;

    public GetImportTemplateQueryHandler(IImportFileParser parser) => _parser = parser;

    public Task<CommonResponse<ImportFileDownloadDto>> Handle(
        GetImportTemplateQuery request, CancellationToken cancellationToken)
    {
        using var workbook = new XLWorkbook();

        // Đúng ba tên sheet, đúng thứ tự, mà ImportWorkbookSplitter tra khi nhận file nộp lên — lệch
        // tên/thứ tự thì bộ tách vẫn dò được qua vị trí (0/1/2), nhưng khớp tên là đường chắc nhất.
        AddSheet(workbook, ImportWorkbookSplitter.CustomersSheetName, ImportEntityTypeEnum.Customer);
        AddSheet(workbook, ImportWorkbookSplitter.SitesSheetName, ImportEntityTypeEnum.Site);
        AddSheet(workbook, ImportWorkbookSplitter.AssetsSheetName, ImportEntityTypeEnum.BatteryAsset);

        using var buffer = new MemoryStream();
        workbook.SaveAs(buffer);

        return Task.FromResult(new CommonResponse<ImportFileDownloadDto>
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = new ImportFileDownloadDto
            {
                FileName = "import-template.xlsx",
                ContentType = XlsxContentType,
                Content = buffer.ToArray()
            }
        });
    }

    private void AddSheet(XLWorkbook workbook, string sheetName, ImportEntityTypeEnum entityType)
    {
        var columns = _parser.TemplateColumns(entityType);
        var required = _parser.RequiredColumns(entityType);
        var example = ExampleRow(entityType, columns).ToList();
        var marker = columns.Select(column =>
            required.Contains(column, StringComparer.Ordinal) ? "REQUIRED" : "optional").ToList();

        var sheet = workbook.Worksheets.Add(sheetName);

        for (var i = 0; i < columns.Count; i++)
            sheet.Cell(1, i + 1).SetValue(columns[i]);
        sheet.Row(1).Style.Font.SetBold();
        sheet.Row(1).Style.Fill.BackgroundColor = XLColor.FromHtml("#D9E1F2");

        // Dòng bắt buộc/tuỳ chọn và dòng ví dụ đều bắt đầu bằng '#' ở ô đầu tiên — cùng quy ước
        // comment mà ImportWorkbookSplitter dùng để coi hai dòng này KHÔNG phải dữ liệu thật. Nếu
        // người dùng quên xoá trước khi nộp, chúng không bao giờ biến thành khách/site/pin giả.
        WriteReferenceRow(sheet, 2, marker);
        WriteReferenceRow(sheet, 3, example);
        sheet.Rows("2:3").Style.Font.SetItalic();
        sheet.Rows("2:3").Style.Font.FontColor = XLColor.Gray;

        sheet.SheetView.FreezeRows(1);
        sheet.Columns().AdjustToContents(1, 3);
    }

    private static void WriteReferenceRow(IXLWorksheet sheet, int rowNumber, IReadOnlyList<string> values)
    {
        for (var i = 0; i < values.Count; i++)
            sheet.Cell(rowNumber, i + 1).SetValue(i == 0 ? "#" + values[0] : values[i]);
    }

    /// <summary>Một dòng dữ liệu hợp lệ mẫu, đúng thứ tự cột của <paramref name="columns"/>.</summary>
    private static IEnumerable<string> ExampleRow(ImportEntityTypeEnum entityType, IReadOnlyList<string> columns)
    {
        var values = entityType switch
        {
            ImportEntityTypeEnum.Customer => new Dictionary<string, string>
            {
                ["external_customer_code"] = "VIDU-KH-001",
                ["full_name"] = "Cong Ty Vi Du TNHH (XOA DONG NAY)",
                ["email"] = "vidu@xoa-dong-nay.example",
                ["phone"] = "0900000000",
            },
            ImportEntityTypeEnum.Site => new Dictionary<string, string>
            {
                ["external_site_code"] = "VIDU-SITE-001",
                ["external_customer_code"] = "VIDU-KH-001",
                ["site_name"] = "Nha May Vi Du (XOA DONG NAY)",
                ["address"] = "KCN Vi Du - Tinh ABC",
                ["latitude"] = "10.7769",
                ["longitude"] = "106.7009",
                ["install_date"] = "2021-03-15",
                ["contact_person_name"] = "Nguyen Van Vi Du",
                ["contact_person_phone"] = "0912345678",
            },
            ImportEntityTypeEnum.BatteryAsset => new Dictionary<string, string>
            {
                ["external_asset_code"] = "VIDU-PIN-001",
                ["external_site_code"] = "VIDU-SITE-001",
                ["serial_number"] = "PYL-US3000C-88A21",
                ["battery_type_name"] = "US3000C",
                ["manufacturer"] = "Pylontech",
                ["nominal_capacity_ah"] = "74",
                ["nominal_voltage"] = "51.2",
                ["chemistry"] = "LiFePO4",
                ["install_date"] = "2021-06-01",
                ["warranty_end_date"] = "2029-06-01",
                ["location"] = "Rack A-01",
                ["notes"] = "Vi du ghi chu (XOA DONG NAY)",
            },
            _ => new Dictionary<string, string>(),
        };

        return columns.Select(column => values.GetValueOrDefault(column, string.Empty));
    }
}
