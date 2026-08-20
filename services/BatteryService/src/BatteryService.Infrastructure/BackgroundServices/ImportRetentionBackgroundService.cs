using BatteryService.Application.Import;
using BatteryService.Application.Interfaces;
using BatteryService.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BatteryService.Infrastructure.BackgroundServices;

/// <summary>
/// Dọn các lô import đã kết thúc từ lâu.
/// </summary>
/// <remarks>
/// Chỉ dọn lô ở trạng thái cuối. Lô đang chờ không bao giờ bị dọn dù cũ tới đâu — một lô kẹt là
/// dấu hiệu có sự cố cần người xem, xoá đi là xoá mất bằng chứng.
/// <para>
/// Xoá mềm, nên bản đồ liên kết và dữ liệu nghiệp vụ do lô tạo ra vẫn nguyên vẹn.
/// </para>
/// </remarks>
public class ImportRetentionBackgroundService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ImportOptions _options;
    private readonly ILogger<ImportRetentionBackgroundService> _logger;

    public ImportRetentionBackgroundService(
        IServiceScopeFactory scopeFactory,
        IOptions<ImportOptions> options,
        ILogger<ImportRetentionBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PurgeAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Import retention sweep failed.");
            }

            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>Tách riêng để test gọi thẳng được.</summary>
    protected virtual async Task PurgeAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IBatteryUnitOfWork>();

        var cutoff = DateTime.UtcNow.AddDays(-_options.RetentionDays);

        var stale = await unitOfWork.ImportBatches
            .GetAllAsync()
            .Where(batch => !batch.IsDeleted
                            && batch.CreatedAt < cutoff
                            && (batch.Status == ImportBatchStatusEnum.Completed
                                || batch.Status == ImportBatchStatusEnum.CompletedWithErrors
                                || batch.Status == ImportBatchStatusEnum.Reverted
                                || batch.Status == ImportBatchStatusEnum.ValidationFailed
                                || batch.Status == ImportBatchStatusEnum.Failed))
            .Take(50)
            .ToListAsync(cancellationToken);

        if (stale.Count == 0)
            return;

        var batchIds = stale.Select(batch => batch.Id).ToList();
        var rows = await unitOfWork.ImportRows
            .GetAllAsync()
            .Where(row => !row.IsDeleted && batchIds.Contains(row.ImportBatchId))
            .ToListAsync(cancellationToken);

        foreach (var row in rows)
            unitOfWork.ImportRows.DeleteAsync(row);

        foreach (var batch in stale)
            unitOfWork.ImportBatches.DeleteAsync(batch);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Import retention removed {Batches} batch(es) and {Rows} row(s) older than {Days} days.",
            stale.Count, rows.Count, _options.RetentionDays);
    }
}
