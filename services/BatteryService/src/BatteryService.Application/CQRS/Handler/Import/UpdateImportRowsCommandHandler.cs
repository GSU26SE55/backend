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
using SharedContracts.Common.Responses;

namespace BatteryService.Application.CQRS.Handler.Import;

/// <summary>
/// Áp dụng sửa đổi lên các dòng của một lô rồi kiểm định lại TOÀN BỘ lô — coi như vừa đọc lại một
/// file .xlsx đã được vá đúng những ô người dùng sửa trên giao diện.
/// </summary>
public class UpdateImportRowsCommandHandler
    : IRequestHandler<UpdateImportRowsCommand, CommonResponse<ImportBatchDto>>
{
    // ImportRow.ExternalRef là varchar(128) — cùng lý do cắt như CreateImportBatchCommandHandler.
    private const int MaxStorableExternalRefLength = 128;

    private readonly IBatteryUnitOfWork _unitOfWork;
    private readonly IImportRowValidator _validator;

    public UpdateImportRowsCommandHandler(IBatteryUnitOfWork unitOfWork, IImportRowValidator validator)
    {
        _unitOfWork = unitOfWork;
        _validator = validator;
    }

    public async Task<CommonResponse<ImportBatchDto>> Handle(
        UpdateImportRowsCommand request, CancellationToken cancellationToken)
    {
        var batch = await _unitOfWork.ImportBatches
            .GetAllAsync()
            .FirstOrDefaultAsync(b => b.Id == request.BatchId && !b.IsDeleted, cancellationToken);

        if (batch is null)
        {
            return new CommonResponse<ImportBatchDto>
            {
                IsSuccess = false,
                StatusCode = 404,
                Message = "Import batch not found."
            };
        }

        // Chỉ sửa được khi lô đang đứng ở bước xem trước — sau khi Ghi thật, các dòng Valid đã biến
        // thành khách hàng/site/pin thật; sửa lại RawJson lúc đó sẽ không phản ánh đúng những gì đã
        // ghi, và dễ gây hiểu nhầm là có thể "sửa rồi ghi lại" một bản ghi đã tồn tại.
        if (batch.Status != ImportBatchStatusEnum.ReadyToCommit)
        {
            return new CommonResponse<ImportBatchDto>
            {
                IsSuccess = false,
                StatusCode = 409,
                Message = $"Rows can only be corrected while the batch is in the ReadyToCommit state. This batch is {batch.Status}."
            };
        }

        var rows = await _unitOfWork.ImportRows
            .GetAllAsync()
            .Where(row => row.ImportBatchId == batch.Id && !row.IsDeleted)
            .ToListAsync(cancellationToken);

        var rowsById = rows.ToDictionary(row => row.Id);
        var unknownRowIds = request.Rows.Select(edit => edit.RowId).Where(id => !rowsById.ContainsKey(id)).ToList();
        if (unknownRowIds.Count > 0)
        {
            return new CommonResponse<ImportBatchDto>
            {
                IsSuccess = false,
                StatusCode = 400,
                Message = $"These row ids do not belong to this batch: {string.Join(", ", unknownRowIds)}."
            };
        }

        // Ghi đè RawJson bằng giá trị mới TRƯỚC khi kiểm định lại — dòng chưa sửa vẫn dùng RawJson
        // cũ, dòng vừa sửa dùng giá trị mới, rồi TẤT CẢ đi qua đúng một vòng kiểm định như nhau.
        foreach (var edit in request.Rows)
            rowsById[edit.RowId].RawJson = JsonSerializer.Serialize(edit.Fields);

        // Kiểm định lại từng dòng theo đúng luật của loại nó — giống hệt bước đọc file .xlsx, chỉ
        // khác nguồn dữ liệu là RawJson đã lưu (và vừa được vá) thay vì một dòng CSV mới đọc.
        var customerCodes = new HashSet<string>(StringComparer.Ordinal);
        var siteCodes = new HashSet<string>(StringComparer.Ordinal);

        foreach (var row in rows)
        {
            var cells = ImportRowPayload.FromRawJson(row.RawJson);

            switch (row.EntityType)
            {
                case ImportEntityTypeEnum.Customer:
                {
                    var validation = _validator.ValidateCustomer(ImportRowPayload.ToCustomer(cells));
                    if (validation.IsValid)
                        customerCodes.Add(validation.Value!.ExternalCode);
                    ApplyValidation(row, validation.Value?.ExternalCode
                        ?? ImportSerialNormalizer.NormalizeReference(cells.GetValueOrDefault("external_customer_code")),
                        validation.Errors);
                    break;
                }
                case ImportEntityTypeEnum.Site:
                {
                    var validation = _validator.ValidateSite(ImportRowPayload.ToSite(cells));
                    if (validation.IsValid)
                        siteCodes.Add(validation.Value!.ExternalCode);
                    ApplyValidation(row, validation.Value?.ExternalCode
                        ?? ImportSerialNormalizer.NormalizeReference(cells.GetValueOrDefault("external_site_code")),
                        validation.Errors);
                    break;
                }
                case ImportEntityTypeEnum.BatteryAsset:
                {
                    var validation = _validator.ValidateAsset(ImportRowPayload.ToAsset(cells));
                    ApplyValidation(row, validation.Value?.ExternalCode
                        ?? ImportSerialNormalizer.NormalizeReference(cells.GetValueOrDefault("external_asset_code")),
                        validation.Errors);
                    break;
                }
            }
        }

        await ApplyCrossReferenceChecksAsync(rows, customerCodes, siteCodes, cancellationToken);
        ApplyDuplicateChecks(rows);

        batch.InvalidRows = rows.Count(row => row.Status == ImportRowStatusEnum.Invalid);
        batch.ValidRows = rows.Count - batch.InvalidRows;

        await PersistAsync(batch, rows, cancellationToken);

        return new CommonResponse<ImportBatchDto>
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = batch.InvalidRows == 0
                ? $"Re-validated {rows.Count} rows, all valid. Nothing has been written yet."
                : $"Re-validated {rows.Count} rows: {batch.ValidRows} valid, {batch.InvalidRows} invalid. Nothing has been written yet.",
            Data = ImportMapper.ToDto(batch, rows)
        };
    }

    private static void ApplyValidation(ImportRow row, string externalRef, IReadOnlyList<Errors> errors)
    {
        row.ExternalRef = Truncate(externalRef, MaxStorableExternalRefLength);
        row.Status = errors.Count > 0 ? ImportRowStatusEnum.Invalid : ImportRowStatusEnum.Valid;
        row.ErrorsJson = errors.Count > 0 ? JsonSerializer.Serialize(errors) : null;
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];

    /// <summary>Giống hệt CreateImportBatchCommandHandler.ApplyCrossReferenceChecksAsync.</summary>
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

    /// <summary>Giống hệt CreateImportBatchCommandHandler.ApplyDuplicateChecks.</summary>
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
            _unitOfWork.ImportBatches.UpdateAsync(batch);
            foreach (var row in rows)
                _unitOfWork.ImportRows.UpdateAsync(row);

            await _unitOfWork.CommitTransactionAsync();
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync();
            throw;
        }
    }
}
