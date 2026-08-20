using BatteryService.Application.CQRS.Command.Import;
using BatteryService.Application.DTOs.Import;
using BatteryService.Application.Interfaces;
using BatteryService.Application.Mapping;
using BatteryService.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.CQRS.Handler.Import;

public class CommitImportBatchCommandHandler
    : IRequestHandler<CommitImportBatchCommand, CommonResponse<ImportBatchDto>>
{
    private readonly IBatteryUnitOfWork _unitOfWork;
    private readonly IPublisher _publisher;

    public CommitImportBatchCommandHandler(IBatteryUnitOfWork unitOfWork, IPublisher publisher)
    {
        _unitOfWork = unitOfWork;
        _publisher = publisher;
    }

    public async Task<CommonResponse<ImportBatchDto>> Handle(
        CommitImportBatchCommand request, CancellationToken cancellationToken)
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

        if (batch.Status != ImportBatchStatusEnum.ReadyToCommit)
        {
            return new CommonResponse<ImportBatchDto>
            {
                IsSuccess = false,
                StatusCode = 409,
                Message = $"Only a batch in the ReadyToCommit state can be committed. This batch is {batch.Status}."
            };
        }

        var validRows = await _unitOfWork.ImportRows
            .GetAllAsync()
            .CountAsync(row => row.ImportBatchId == batch.Id && !row.IsDeleted
                               && row.Status == ImportRowStatusEnum.Valid, cancellationToken);

        if (validRows == 0)
        {
            return new CommonResponse<ImportBatchDto>
            {
                IsSuccess = false,
                StatusCode = 409,
                Message = "This batch has no valid rows to write. Fix the file and upload it again."
            };
        }

        batch.Status = ImportBatchStatusEnum.Committing;
        batch.IsDryRun = false;
        // Mốc tính hạn chờ tài khoản đặt lại tại đây, không dùng lại mốc của lần kiểm định:
        // giữa hai lần đó người dùng có thể đã xem kết quả hàng giờ.
        batch.StartedAt = DateTime.UtcNow;
        batch.CompletedAt = null;
        _unitOfWork.ImportBatches.UpdateAsync(batch);

        await _publisher.Publish(Notification.Audit.BatteryAuditTrailNotification.For(
            BatteryAuditActionEnum.PartnerImportCommitted, batch.Id,
            targetDisplay: batch.FileName,
            metadata: new Dictionary<string, object?>
            {
                ["validRows"] = validRows
            }), cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new CommonResponse<ImportBatchDto>
        {
            IsSuccess = true,
            StatusCode = 202,
            Message = $"Writing {validRows} row(s). Poll this batch to follow progress.",
            Data = ImportMapper.ToDto(batch, rows: null)
        };
    }
}
