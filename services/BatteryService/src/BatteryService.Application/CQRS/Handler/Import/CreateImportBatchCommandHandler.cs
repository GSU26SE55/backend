using System.Security.Cryptography;
using System.Text.Json;
using BatteryService.Application.CQRS.Command.Import;
using BatteryService.Application.DTOs.Import;
using BatteryService.Application.Import;
using BatteryService.Application.Interfaces;
using BatteryService.Application.Mapping;
using BatteryService.Domain.Entities;
using BatteryService.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.CQRS.Handler.Import;

/// <summary>
/// Đọc và kiểm định file nhập, dựng lô cùng các dòng trung chuyển.
/// </summary>
public class CreateImportBatchCommandHandler
    : IRequestHandler<CreateImportBatchCommand, CommonResponse<ImportBatchDto>>
{
    private readonly IBatteryUnitOfWork _unitOfWork;
    private readonly IImportFileParser _parser;
    private readonly IImportRowValidator _validator;
    private readonly ImportOptions _options;

    public CreateImportBatchCommandHandler(
        IBatteryUnitOfWork unitOfWork,
        IImportFileParser parser,
        IImportRowValidator validator,
        IOptions<ImportOptions> options)
    {
        _unitOfWork = unitOfWork;
        _parser = parser;
        _validator = validator;
        _options = options.Value;
    }

    public async Task<CommonResponse<ImportBatchDto>> Handle(
        CreateImportBatchCommand request, CancellationToken cancellationToken)
    {
        var fileHash = ComputeHash(request);

        // Chặn nạp lại đúng file đã nạp. So theo nội dung chứ không theo tên: đối tác hay gửi lại
        // cùng một file dưới tên khác, và đó là ca dễ nhân đôi dữ liệu nhất.
        var duplicate = await _unitOfWork.ImportBatches
            .GetAllAsync()
            .AsNoTracking()
            .Where(batch => !batch.IsDeleted
                            && batch.FileSha256 == fileHash
                            && batch.Status != ImportBatchStatusEnum.Reverted
                            && batch.Status != ImportBatchStatusEnum.ValidationFailed
                            && batch.Status != ImportBatchStatusEnum.Failed)
            .FirstOrDefaultAsync(cancellationToken);

        if (duplicate is not null)
        {

            return new CommonResponse<ImportBatchDto>
            {
                IsSuccess = false,
                StatusCode = 409,
                Message = $"This exact file was already imported on {duplicate.CreatedAt:yyyy-MM-dd HH:mm} UTC (batch {duplicate.Id}). Revert that batch first if you need to import it again."
            };
        }

        var batchEntity = new ImportBatch
        {
            Id = Guid.NewGuid(),
            FileName = request.FileName,
            FileSha256 = fileHash,
            Status = ImportBatchStatusEnum.Validating,
            IsDryRun = request.DryRun,
            RequestedBy = request.RequestedBy,
            StartedAt = DateTime.UtcNow
        };

        var rows = new List<ImportRow>();
        var fatal = new List<string>();

        // Mã đã biết trong CHÍNH lô này — dùng để kiểm tham chiếu chéo giữa các file.
        var customerCodes = new HashSet<string>(StringComparer.Ordinal);
        var siteCodes = new HashSet<string>(StringComparer.Ordinal);

        ParseCustomers(request, batchEntity, rows, fatal, customerCodes);
        ParseSites(request, batchEntity, rows, fatal, siteCodes);
        ParseAssets(request, batchEntity, rows, fatal);

        if (fatal.Count > 0)
        {
            batchEntity.Status = ImportBatchStatusEnum.ValidationFailed;
            batchEntity.ErrorSummary = string.Join(" | ", fatal);
            batchEntity.CompletedAt = DateTime.UtcNow;
            await PersistAsync(batchEntity, rows, cancellationToken);

            return new CommonResponse<ImportBatchDto>
            {
                IsSuccess = false,
                StatusCode = 422,
                Message = batchEntity.ErrorSummary,
                Data = ImportMapper.ToDto(batchEntity, rows)
            };
        }

        // Tham chiếu chéo chỉ kiểm được sau khi đã đọc hết các file, vì file site có thể nằm trước
        // file khách hàng trong cùng một lần gửi.
        await ApplyCrossReferenceChecksAsync(rows, customerCodes, siteCodes, cancellationToken);
        ApplyDuplicateChecks(rows);

        batchEntity.TotalRows = rows.Count;
        batchEntity.InvalidRows = rows.Count(row => row.Status == ImportRowStatusEnum.Invalid);
        batchEntity.ValidRows = rows.Count - batchEntity.InvalidRows;
        batchEntity.Status = ImportBatchStatusEnum.ReadyToCommit;
        batchEntity.CompletedAt = DateTime.UtcNow;

        await PersistAsync(batchEntity, rows, cancellationToken);

        return new CommonResponse<ImportBatchDto>
        {
            IsSuccess = true,
            StatusCode = 201,
            Message = batchEntity.InvalidRows == 0
                ? $"Validated {batchEntity.TotalRows} rows, all valid. Nothing has been written yet."
                : $"Validated {batchEntity.TotalRows} rows: {batchEntity.ValidRows} valid, {batchEntity.InvalidRows} invalid. Nothing has been written yet.",
            Data = ImportMapper.ToDto(batchEntity, rows)
        };
    }

    private void ParseCustomers(
        CreateImportBatchCommand request, ImportBatch batch, List<ImportRow> rows,
        List<string> fatal, HashSet<string> customerCodes)
    {
        if (request.CustomersCsv is not { Length: > 0 })
            return;

        using var stream = new MemoryStream(request.CustomersCsv);
        var parsed = _parser.ParseCustomers(stream, _options.MaxRowsPerBatch);
        if (parsed.IsFatal)
        {
            fatal.Add($"customers.csv: {parsed.FatalError}");
            return;
        }

        batch.EntityTypesMask |= 1 << (int)ImportEntityTypeEnum.Customer;

        foreach (var parsedRow in parsed.Rows)
        {
            var validation = _validator.ValidateCustomer(parsedRow.Value);
            if (validation.IsValid)
                customerCodes.Add(validation.Value!.ExternalCode);

            rows.Add(BuildRow(batch, ImportEntityTypeEnum.Customer, parsedRow.RowNumber, parsedRow.RawJson,
                validation.Value?.ExternalCode ?? ImportSerialNormalizer.NormalizeReference(parsedRow.Value.ExternalCustomerCode),
                validation.Errors));
        }
    }

    private void ParseSites(
        CreateImportBatchCommand request, ImportBatch batch, List<ImportRow> rows,
        List<string> fatal, HashSet<string> siteCodes)
    {
        if (request.SitesCsv is not { Length: > 0 })
            return;

        using var stream = new MemoryStream(request.SitesCsv);
        var parsed = _parser.ParseSites(stream, _options.MaxRowsPerBatch);
        if (parsed.IsFatal)
        {
            fatal.Add($"sites.csv: {parsed.FatalError}");
            return;
        }

        batch.EntityTypesMask |= 1 << (int)ImportEntityTypeEnum.Site;

        foreach (var parsedRow in parsed.Rows)
        {
            var validation = _validator.ValidateSite(parsedRow.Value);
            if (validation.IsValid)
                siteCodes.Add(validation.Value!.ExternalCode);

            rows.Add(BuildRow(batch, ImportEntityTypeEnum.Site, parsedRow.RowNumber, parsedRow.RawJson,
                validation.Value?.ExternalCode ?? ImportSerialNormalizer.NormalizeReference(parsedRow.Value.ExternalSiteCode),
                validation.Errors));
        }
    }

    private void ParseAssets(
        CreateImportBatchCommand request, ImportBatch batch, List<ImportRow> rows, List<string> fatal)
    {
        if (request.AssetsCsv is not { Length: > 0 })
            return;

        using var stream = new MemoryStream(request.AssetsCsv);
        var parsed = _parser.ParseAssets(stream, _options.MaxRowsPerBatch);
        if (parsed.IsFatal)
        {
            fatal.Add($"assets.csv: {parsed.FatalError}");
            return;
        }

        batch.EntityTypesMask |= 1 << (int)ImportEntityTypeEnum.BatteryAsset;

        foreach (var parsedRow in parsed.Rows)
        {
            var validation = _validator.ValidateAsset(parsedRow.Value);
            rows.Add(BuildRow(batch, ImportEntityTypeEnum.BatteryAsset, parsedRow.RowNumber, parsedRow.RawJson,
                validation.Value?.ExternalCode ?? ImportSerialNormalizer.NormalizeReference(parsedRow.Value.ExternalAssetCode),
                validation.Errors));
        }
    }


    /// <summary>
    /// Kiểm tra site trỏ tới khách hàng có thật, và pin/thiết bị trỏ tới site có thật.
    /// </summary>
    /// <remarks>
    /// "Có thật" gồm hai nguồn: mã nằm trong chính lô này, hoặc mã đã được nạp ở lô trước và còn
    /// trong bản đồ liên kết. Không tính nguồn thứ hai thì việc nạp bổ sung (tháng này thêm 20 quả
    /// pin vào site cũ) sẽ luôn bị từ chối.
    /// </remarks>
    private async Task ApplyCrossReferenceChecksAsync(
        List<ImportRow> rows,
        HashSet<string> customerCodes, HashSet<string> siteCodes,
        CancellationToken cancellationToken)
    {
        var siteRows = rows.Where(r => r.EntityType == ImportEntityTypeEnum.Site).ToList();
        var childRows = rows.Where(r => r.EntityType == ImportEntityTypeEnum.BatteryAsset).ToList();

        if (siteRows.Count == 0 && childRows.Count == 0)
            return;

        var knownCustomerCodes = await LoadKnownRefsAsync(ImportEntityTypeEnum.Customer, cancellationToken);
        var knownSiteCodes = await LoadKnownRefsAsync(ImportEntityTypeEnum.Site, cancellationToken);

        foreach (var row in siteRows)
        {
            if (row.Status == ImportRowStatusEnum.Invalid)
                continue;

            var customerCode = ReadReference(row.RawJson, "external_customer_code");
            if (customerCodes.Contains(customerCode) || knownCustomerCodes.Contains(customerCode))
                continue;

            MarkInvalid(row, "ExternalCustomerCode",
                $"No customer with code \"{customerCode}\" was found in this batch or in previously imported data.");
        }

        foreach (var row in childRows)
        {
            if (row.Status == ImportRowStatusEnum.Invalid)
                continue;

            var siteCode = ReadReference(row.RawJson, "external_site_code");
            if (siteCodes.Contains(siteCode) || knownSiteCodes.Contains(siteCode))
                continue;

            MarkInvalid(row, "ExternalSiteCode",
                $"No site with code \"{siteCode}\" was found in this batch or in previously imported data.");
        }
    }

    /// <summary>Hai dòng cùng mã trong một lô là lỗi dữ liệu nguồn, phải báo chứ không im lặng chọn một.</summary>
    private static void ApplyDuplicateChecks(List<ImportRow> rows)
    {
        foreach (var group in rows.GroupBy(row => new { row.EntityType, row.ExternalRef }))
        {
            if (group.Key.ExternalRef.Length == 0 || group.Count() < 2)
                continue;

            var lineNumbers = string.Join(", ", group.Select(row => row.RowNumber));
            foreach (var row in group)
            {
                MarkInvalid(row, "ExternalRef",
                    $"Reference code \"{group.Key.ExternalRef}\" appears more than once in this batch (rows {lineNumbers}).");
            }
        }
    }

    private async Task<HashSet<string>> LoadKnownRefsAsync(
        ImportEntityTypeEnum entityType, CancellationToken cancellationToken)
    {
        var refs = await _unitOfWork.ImportEntityLinks
            .GetAllAsync()
            .AsNoTracking()
            .Where(link => !link.IsDeleted && link.EntityType == entityType)
            .Select(link => link.ExternalRef)
            .ToListAsync(cancellationToken);

        return new HashSet<string>(refs, StringComparer.Ordinal);
    }

    // ImportRow.ExternalRef là varchar(128) (ImportRowConfiguration). Khi một dòng hỏng CHÍNH VÌ mã
    // tham chiếu dài hơn 128 ký tự, validator trả về Value=null nên nơi gọi phải tự chuẩn hoá lại mã
    // gốc chưa cắt — giá trị đó vẫn dài hơn cột cho phép, và INSERT sẽ ném PostgresException 22001
    // đánh sập TOÀN BỘ lô thay vì chỉ đánh hỏng đúng 1 dòng. Dòng này đã chắc chắn Invalid (lỗi "quá
    // 128 ký tự" đã có trong errors) nên cắt bớt giá trị lưu trữ không đổi kết quả kiểm định.
    private const int MaxStorableExternalRefLength = 128;

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];

    private static ImportRow BuildRow(
        ImportBatch batch, ImportEntityTypeEnum entityType, int rowNumber,
        string rawJson, string externalRef, IReadOnlyList<Errors> errors)
    {
        return new ImportRow
        {
            Id = Guid.NewGuid(),
            ImportBatchId = batch.Id,
            RowNumber = rowNumber,
            EntityType = entityType,
            RawJson = rawJson,
            ExternalRef = Truncate(externalRef, MaxStorableExternalRefLength),
            Status = errors.Count > 0 ? ImportRowStatusEnum.Invalid : ImportRowStatusEnum.Valid,
            ErrorsJson = errors.Count > 0 ? JsonSerializer.Serialize(errors) : null
        };
    }

    private static void MarkInvalid(ImportRow row, string field, string detail)
    {
        var errors = string.IsNullOrEmpty(row.ErrorsJson)
            ? new List<Errors>()
            : JsonSerializer.Deserialize<List<Errors>>(row.ErrorsJson) ?? new List<Errors>();

        errors.Add(new Errors { Field = field, Detail = detail });
        row.ErrorsJson = JsonSerializer.Serialize(errors);
        row.Status = ImportRowStatusEnum.Invalid;
    }

    private static string ReadReference(string rawJson, string column)
    {
        var cells = JsonSerializer.Deserialize<Dictionary<string, string>>(rawJson);
        return cells is not null && cells.TryGetValue(column, out var value)
            ? ImportSerialNormalizer.NormalizeReference(value)
            : string.Empty;
    }

    private async Task PersistAsync(ImportBatch batch, List<ImportRow> rows, CancellationToken cancellationToken)
    {
        await _unitOfWork.BeginTransactionAsync();
        try
        {
            await _unitOfWork.ImportBatches.AddAsync(batch);
            foreach (var row in rows)
                await _unitOfWork.ImportRows.AddAsync(row);

            await _unitOfWork.CommitTransactionAsync();
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync();
            throw;
        }
    }

    /// <summary>Băm toàn bộ nội dung đã gửi theo thứ tự cố định để hai lần gửi giống nhau ra cùng một mã.</summary>
    private static string ComputeHash(CreateImportBatchCommand request)
    {
        using var sha = SHA256.Create();
        using var buffer = new MemoryStream();

        AppendPart(buffer, request.CustomersCsv);
        AppendPart(buffer, request.SitesCsv);
        AppendPart(buffer, request.AssetsCsv);

        buffer.Position = 0;
        return Convert.ToHexString(sha.ComputeHash(buffer)).ToLowerInvariant();
    }

    private static void AppendPart(Stream target, byte[]? content)
    {
        // Ghi độ dài trước nội dung để hai bộ file khác nhau không thể ghép thành cùng một chuỗi byte.
        var length = BitConverter.GetBytes(content?.Length ?? 0);
        target.Write(length, 0, length.Length);
        if (content is { Length: > 0 })
            target.Write(content, 0, content.Length);
    }
}
