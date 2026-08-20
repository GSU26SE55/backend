using BatteryService.Application.CQRS.Command.Import;
using BatteryService.Application.DTOs.Import;
using BatteryService.Application.Interfaces;
using BatteryService.Application.Mapping;
using BatteryService.Domain.Entities;
using BatteryService.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.CQRS.Handler.Import;

/// <summary>Gỡ bỏ dữ liệu do một lô tạo ra, theo đúng thứ tự ngược với lúc tạo.</summary>
public class RevertImportBatchCommandHandler
    : IRequestHandler<RevertImportBatchCommand, CommonResponse<ImportBatchDto>>
{
    private readonly IBatteryUnitOfWork _unitOfWork;
    private readonly IPublisher _publisher;

    public RevertImportBatchCommandHandler(IBatteryUnitOfWork unitOfWork, IPublisher publisher)
    {
        _unitOfWork = unitOfWork;
        _publisher = publisher;
    }

    public async Task<CommonResponse<ImportBatchDto>> Handle(
        RevertImportBatchCommand request, CancellationToken cancellationToken)
    {
        var batch = await _unitOfWork.ImportBatches
            .GetAllAsync()
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

        if (batch.Status is not (ImportBatchStatusEnum.Completed
            or ImportBatchStatusEnum.CompletedWithErrors
            or ImportBatchStatusEnum.Failed))
        {
            return new CommonResponse<ImportBatchDto>
            {
                IsSuccess = false,
                StatusCode = 409,
                Message = $"Only a finished batch can be reverted. This batch is {batch.Status}."
            };
        }

        var rows = await _unitOfWork.ImportRows
            .GetAllAsync()
            .Where(row => row.ImportBatchId == batch.Id && !row.IsDeleted && row.CreatedEntityId != null)
            .ToListAsync(cancellationToken);

        var links = await _unitOfWork.ImportEntityLinks
            .GetAllAsync()
            .Where(link => !link.IsDeleted && link.CreatedByBatchId == batch.Id)
            .ToListAsync(cancellationToken);

        await _unitOfWork.BeginTransactionAsync();
        try
        {
            // Thứ tự ngược với lúc tạo: pin trước, rồi site. Đi xuôi sẽ vấp khoá ngoại vì site vẫn
            // còn pin trỏ tới.
            var removed = 0;
            removed += await RemoveAssetsAsync(rows, cancellationToken);
            removed += await RemoveSitesAsync(rows, cancellationToken);

            foreach (var row in rows)
            {
                row.Status = ImportRowStatusEnum.Reverted;
                _unitOfWork.ImportRows.UpdateAsync(row);
            }

            // Gỡ bản đồ liên kết để lần nạp sau tạo lại từ đầu thay vì trỏ vào bản ghi đã xoá.
            foreach (var link in links)
                _unitOfWork.ImportEntityLinks.DeleteAsync(link);

            batch.Status = ImportBatchStatusEnum.Reverted;
            batch.CompletedAt = DateTime.UtcNow;
            batch.CreatedRows = 0;
            batch.UpdatedRows = 0;
            _unitOfWork.ImportBatches.UpdateAsync(batch);

            await _publisher.Publish(Notification.Audit.BatteryAuditTrailNotification.For(
                BatteryAuditActionEnum.PartnerImportReverted, batch.Id,
                targetDisplay: batch.FileName,
                metadata: new Dictionary<string, object?>
                {
                    ["removedEntities"] = removed,
                    ["removedLinks"] = links.Count
                }), cancellationToken);

            await _unitOfWork.CommitTransactionAsync();

            return new CommonResponse<ImportBatchDto>
            {
                IsSuccess = true,
                StatusCode = 200,
                Message = $"Reverted {removed} record(s) created by this batch. Customer accounts were intentionally kept: they may already have been used to sign in or attached to tickets.",
                Data = ImportMapper.ToDto(batch, rows: null)
            };
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync();
            throw;
        }
    }


    private async Task<int> RemoveAssetsAsync(List<ImportRow> rows, CancellationToken cancellationToken)
    {
        var ids = CollectIds(rows, ImportEntityTypeEnum.BatteryAsset);
        if (ids.Count == 0)
            return 0;

        var entities = await _unitOfWork.BatteryAssets
            .GetAllAsync()
            .Where(asset => !asset.IsDeleted && ids.Contains(asset.Id))
            .ToListAsync(cancellationToken);

        foreach (var entity in entities)
            _unitOfWork.BatteryAssets.DeleteAsync(entity);

        return entities.Count;
    }

    private async Task<int> RemoveSitesAsync(List<ImportRow> rows, CancellationToken cancellationToken)
    {
        var ids = CollectIds(rows, ImportEntityTypeEnum.Site);
        if (ids.Count == 0)
            return 0;

        var entities = await _unitOfWork.Sites
            .GetAllAsync()
            .Where(site => !site.IsDeleted && ids.Contains(site.Id))
            .ToListAsync(cancellationToken);

        foreach (var entity in entities)
            _unitOfWork.Sites.DeleteAsync(entity);

        return entities.Count;
    }

    /// <summary>
    /// Chỉ lấy bản ghi do lô này <b>tạo mới</b>. Dòng ở trạng thái cập nhật đã ghi đè lên bản ghi
    /// có sẵn từ trước; xoá chúng đi là xoá cả dữ liệu không thuộc về lô.
    /// </summary>
    private static List<Guid> CollectIds(List<ImportRow> rows, ImportEntityTypeEnum entityType) =>
        rows.Where(row => row.EntityType == entityType
                          && row.Status == ImportRowStatusEnum.Created
                          && row.CreatedEntityId.HasValue)
            .Select(row => row.CreatedEntityId!.Value)
            .Distinct()
            .ToList();
}
