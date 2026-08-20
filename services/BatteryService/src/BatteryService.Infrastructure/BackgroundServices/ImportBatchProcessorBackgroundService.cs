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
/// Đẩy các lô import đang ở pha ghi đi tiếp, từng nhịp một.
/// </summary>
/// <remarks>
/// <para>
/// Việc ghi nằm ở đây chứ không nằm trong lời gọi HTTP vì lô phải chờ tài khoản khách hàng đồng bộ
/// qua message bus. Khoảng chờ đó không đoán trước được, và giữ một kết nối HTTP mở suốt quãng đó
/// là cách chắc chắn nhất để nhận lỗi hết thời gian chờ ở giữa chừng — lúc dữ liệu đã ghi được một
/// nửa.
/// </para>
/// <para>
/// Mỗi vòng duyệt <b>tất cả</b> lô đang dở, lần lượt từ cũ tới mới. Bản đầu chỉ lấy lô cũ nhất, và
/// điều đó sai: một lô đang chờ tài khoản sẽ chặn đứng mọi lô nạp sau nó cho tới khi hết hạn chờ —
/// người dùng thứ hai thấy lô của mình đứng im mà không có lý do nào hiện ra. Lô đang chờ trả về
/// ngay nên duyệt hết vẫn rẻ.
/// </para>
/// </remarks>
public class ImportBatchProcessorBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ImportOptions _options;
    private readonly ILogger<ImportBatchProcessorBackgroundService> _logger;

    public ImportBatchProcessorBackgroundService(
        IServiceScopeFactory scopeFactory,
        IOptions<ImportOptions> options,
        ILogger<ImportBatchProcessorBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(1, _options.PollIntervalSeconds));

        if (!await DelayAsync(interval, stoppingToken))
            return;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessOneBatchAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Import batch processing tick failed.");
            }

            if (!await DelayAsync(interval, stoppingToken))
                return;
        }
    }

    /// <summary>Số lô xử lý tối đa mỗi nhịp — chặn trần để một hàng đợi dài không giữ vòng lặp quá lâu.</summary>
    private const int MaxBatchesPerTick = 20;

    /// <summary>Tách riêng để test gọi thẳng được mà không phải dựng cả vòng lặp nền.</summary>
    protected virtual async Task ProcessOneBatchAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IBatteryUnitOfWork>();

        var batchIds = await unitOfWork.ImportBatches
            .GetAllAsync()
            .AsNoTracking()
            .Where(batch => !batch.IsDeleted
                            && (batch.Status == ImportBatchStatusEnum.Committing
                                || batch.Status == ImportBatchStatusEnum.AwaitingAccountSync))
            .OrderBy(batch => batch.CreatedAt)
            .Take(MaxBatchesPerTick)
            .Select(batch => batch.Id)
            .ToListAsync(cancellationToken);

        if (batchIds.Count == 0)
            return;

        var commitService = scope.ServiceProvider.GetRequiredService<IImportCommitService>();

        foreach (var batchId in batchIds)
        {
            if (cancellationToken.IsCancellationRequested)
                return;

            try
            {
                if (await commitService.AdvanceAsync(batchId, cancellationToken))
                    _logger.LogInformation("Import batch {BatchId} finished.", batchId);
            }
            catch (Exception ex)
            {
                // Một lô hỏng không được kéo theo các lô còn lại trong cùng nhịp.
                _logger.LogError(ex, "Advancing import batch {BatchId} failed.", batchId);
            }
        }
    }

    private static async Task<bool> DelayAsync(TimeSpan interval, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(interval, cancellationToken);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
