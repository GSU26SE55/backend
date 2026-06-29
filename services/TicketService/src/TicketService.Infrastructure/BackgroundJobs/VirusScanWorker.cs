using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TicketService.Application.Common.Models;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Interfaces.Services;
using TicketService.Domain.Enums;

namespace TicketService.Infrastructure.BackgroundJobs;

/// <summary>
/// Poll TicketAttachment với VirusScanStatus=Pending → download file → scan qua ClamAV REST → cập nhật status.
/// Mặc định disabled — bật qua Chat:Features:EnableVirusScan=true khi ClamAV đã deploy.
/// </summary>
public class VirusScanWorker : BackgroundService
{
    private readonly ILogger<VirusScanWorker> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ChatOptions _opts;

    public VirusScanWorker(
        ILogger<VirusScanWorker> logger,
        IServiceProvider serviceProvider,
        IHttpClientFactory httpClientFactory,
        IOptions<ChatOptions> opts)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _httpClientFactory = httpClientFactory;
        _opts = opts.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("VirusScanWorker starting. EnableVirusScan={Enabled}", _opts.Features.EnableVirusScan);

        while (!stoppingToken.IsCancellationRequested)
        {
            if (_opts.Features.EnableVirusScan)
                await ScanPendingAttachmentsAsync(stoppingToken);

            await Task.Delay(TimeSpan.FromSeconds(_opts.VirusScan.IntervalSeconds), stoppingToken);
        }

        _logger.LogInformation("VirusScanWorker stopped.");
    }

    public async Task ScanPendingAttachmentsAsync(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<ITicketUnitOfWork>();
        var clamAv = scope.ServiceProvider.GetRequiredService<IClamAvClient>();

        try
        {
            var pending = await unitOfWork.TicketAttachments
                .GetAllAsync()
                .Where(a => !a.IsDeleted && a.VirusScanStatus == VirusScanStatusEnum.Pending)
                .OrderBy(a => a.CreatedAt)
                .Take(_opts.VirusScan.BatchSize)
                .ToListAsync(ct);

            if (pending.Count == 0)
                return;

            _logger.LogInformation("VirusScanWorker: scanning {Count} pending attachments.", pending.Count);

            foreach (var attachment in pending)
            {
                attachment.VirusScanStatus = await DownloadAndScanAsync(clamAv, attachment.FileId, attachment.FileName, ct);
                unitOfWork.TicketAttachments.UpdateAsync(attachment);
            }

            await unitOfWork.CommitTransactionAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "VirusScanWorker: error during scan cycle.");
            try
            { await unitOfWork.RollbackTransactionAsync(); }
            catch { /* ignore secondary */ }
        }
    }

    protected virtual async Task<VirusScanStatusEnum> DownloadAndScanAsync(
        IClamAvClient clamAv,
        Guid fileId,
        string fileName,
        CancellationToken ct)
    {
        var downloadUrl = $"{_opts.VirusScan.FileStorageBaseUrl.TrimEnd('/')}/api/files/{fileId}/download";
        try
        {
            var httpClient = _httpClientFactory.CreateClient("FileDownload");
            await using var fileStream = await httpClient.GetStreamAsync(downloadUrl, ct);
            return await clamAv.ScanAsync(fileStream, fileName, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "VirusScanWorker: failed to download/scan file {FileId} ({FileName})", fileId, fileName);
            return VirusScanStatusEnum.Failed;
        }
    }
}
