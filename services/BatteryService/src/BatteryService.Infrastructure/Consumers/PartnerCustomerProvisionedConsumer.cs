using System.Text.Json;
using BatteryService.Application.Interfaces;
using BatteryService.Domain.Entities;
using BatteryService.Domain.Enums;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SharedContracts.Common.Responses;
using SharedContracts.Events;
using SharedInfrastructure.Idempotency;

namespace BatteryService.Infrastructure.Consumers;

/// <summary>
/// Nhận kết quả cấp tài khoản từ AuthService và mở khoá cho các bậc còn lại của lô import.
/// </summary>
/// <remarks>
/// Đây là nửa sau của pha 2A. Không có nó thì dòng khách hàng nằm mãi ở trạng thái chờ và lô không
/// bao giờ sang được bậc site — vì BatteryService không tự biết định danh tài khoản, thứ chỉ
/// AuthService sinh ra.
/// </remarks>
public class PartnerCustomerProvisionedConsumer : IConsumer<PartnerCustomerProvisionedEvent>
{
    private readonly IBatteryUnitOfWork _unitOfWork;
    private readonly IInboxStore _inboxStore;
    private readonly ILogger<PartnerCustomerProvisionedConsumer> _logger;

    public PartnerCustomerProvisionedConsumer(
        IBatteryUnitOfWork unitOfWork,
        IInboxStore inboxStore,
        ILogger<PartnerCustomerProvisionedConsumer> logger)
    {
        _unitOfWork = unitOfWork;
        _inboxStore = inboxStore;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<PartnerCustomerProvisionedEvent> context)
    {
        await context.ProcessOnceAsync(_inboxStore, nameof(PartnerCustomerProvisionedConsumer), async () =>
        {
            var evt = context.Message;
            var ct = context.CancellationToken;

            var row = await _unitOfWork.ImportRows
                .GetAllAsync()
                .FirstOrDefaultAsync(r => r.Id == evt.RowId && !r.IsDeleted, ct);

            if (row is null)
            {
                // Lô đã bị xoá trong lúc chờ. Không có gì để cập nhật, và cũng không có gì sai.
                _logger.LogInformation(
                    "Import row {RowId} no longer exists; ignoring provisioning result.", evt.RowId);
                return;
            }

            await _unitOfWork.BeginTransactionAsync();
            try
            {
                if (evt.FailureReason is not null)
                {
                    AppendError(row, "provisioning", evt.FailureReason);
                    row.Status = ImportRowStatusEnum.Failed;
                    row.ProcessedAt = DateTime.UtcNow;
                    _unitOfWork.ImportRows.UpdateAsync(row);
                    await _unitOfWork.CommitTransactionAsync();

                    _logger.LogWarning(
                        "Provisioning failed for import row {RowId}: {Reason}", evt.RowId, evt.FailureReason);
                    return;
                }

                row.LinkedAccountId = evt.AccountId;
                row.CreatedEntityId = evt.AccountId;
                // Liên kết vào tài khoản có sẵn được ghi là "bỏ qua": không có bản ghi nào được
                // tạo mới, và hoàn tác về sau cũng không được phép đụng tới tài khoản đó.
                row.Status = evt.WasExisting ? ImportRowStatusEnum.Skipped : ImportRowStatusEnum.Created;
                row.ProcessedAt = DateTime.UtcNow;
                _unitOfWork.ImportRows.UpdateAsync(row);

                var linkExists = await _unitOfWork.ImportEntityLinks
                    .GetAllAsync()
                    .AnyAsync(link => !link.IsDeleted
                                      && link.EntityType == ImportEntityTypeEnum.Customer
                                      && link.ExternalRef == evt.ExternalCustomerCode, ct);

                if (!linkExists)
                {
                    await _unitOfWork.ImportEntityLinks.AddAsync(new ImportEntityLink
                    {
                        Id = Guid.NewGuid(),
                        EntityType = ImportEntityTypeEnum.Customer,
                        ExternalRef = evt.ExternalCustomerCode,
                        ExternalRefRaw = evt.ExternalCustomerCode,
                        InternalId = evt.AccountId,
                        CreatedByBatchId = evt.BatchId
                    });
                }

                await _unitOfWork.CommitTransactionAsync();
            }
            catch
            {
                await _unitOfWork.RollbackTransactionAsync();
                throw;
            }

            _logger.LogInformation(
                "Import row {RowId} linked to account {AccountId} (existing: {WasExisting}).",
                evt.RowId, evt.AccountId, evt.WasExisting);
        });
    }

    private static void AppendError(ImportRow row, string field, string detail)
    {
        var errors = string.IsNullOrEmpty(row.ErrorsJson)
            ? new List<Errors>()
            : JsonSerializer.Deserialize<List<Errors>>(row.ErrorsJson) ?? new List<Errors>();

        errors.Add(new Errors { Field = field, Detail = detail });
        row.ErrorsJson = JsonSerializer.Serialize(errors);
    }
}
