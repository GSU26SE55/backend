using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedContracts.Grpc.FileInternal;
using TicketService.Application.Common.Models;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Interfaces.Services;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;

namespace TicketService.Infrastructure.BackgroundJobs;

/// <summary>
/// Quét virus cho đính kèm: lấy bản ghi tới lượt → tải file qua kênh nội bộ → ClamAV → ghi kết quả.
/// Mặc định tắt — bật qua <c>Chat:Features:EnableVirusScan=true</c> khi ClamAV đã deploy.
/// </summary>
/// <remarks>
/// <para>
/// GH-790 — trước đây worker tải file bằng <c>GET /api/files/{id}/download</c> mà KHÔNG gắn token,
/// trong khi <c>FilesController</c> có <c>[Authorize]</c>. Mọi lần tải đều 401, worker ghi
/// <c>Failed</c>, và vì nó chỉ quét bản ghi <c>Pending</c> nên bản ghi đó không bao giờ được thử
/// lại. Kết quả: đính kèm mãi mãi trả 202 "đang quét, thử lại sau" và không ai tải được — không có
/// lỗi nào nổi lên để ai đó đi tìm.
/// </para>
/// <para>
/// Hai thay đổi: (1) tải qua kênh gRPC nội bộ <c>FileInternal</c> — đường service-to-service ĐÃ CÓ,
/// dùng sẵn cho voice transcription, chạy trên cổng riêng không qua tầng JWT người dùng cuối;
/// (2) hỏng tạm thời thì thử lại có giãn cách, chỉ vào <c>Failed</c> khi hết số lần cho phép.
/// </para>
/// </remarks>
public class VirusScanWorker : BackgroundService
{
    private readonly ILogger<VirusScanWorker> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly ChatOptions _opts;

    public VirusScanWorker(
        ILogger<VirusScanWorker> logger,
        IServiceProvider serviceProvider,
        IOptions<ChatOptions> opts)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _opts = opts.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("VirusScanWorker starting. EnableVirusScan={Enabled}", _opts.Features.EnableVirusScan);

        while (!stoppingToken.IsCancellationRequested)
        {
            if (_opts.Features.EnableVirusScan)
                await ScanPendingAttachmentsAsync(stoppingToken);

            try
            { await Task.Delay(TimeSpan.FromSeconds(_opts.VirusScan.IntervalSeconds), stoppingToken); }
            catch (OperationCanceledException) { break; }
        }

        _logger.LogInformation("VirusScanWorker stopped.");
    }

    /// <summary>Chạy đúng MỘT lượt quét. Public để test gọi thẳng, không phải chờ timer.</summary>
    public async Task ScanPendingAttachmentsAsync(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<ITicketUnitOfWork>();
        var clamAv = scope.ServiceProvider.GetRequiredService<IClamAvClient>();
        var files = scope.ServiceProvider.GetRequiredService<FileInternal.FileInternalClient>();

        try
        {
            await ReclaimStaleScansAsync(unitOfWork, ct);

            var now = DateTime.UtcNow;
            var due = await unitOfWork.TicketAttachments
                .GetAllAsync()
                .Where(a => !a.IsDeleted
                            && a.VirusScanStatus == VirusScanStatusEnum.Pending
                            && a.VirusScanAttempts < _opts.VirusScan.MaxAttempts)
                .OrderBy(a => a.CreatedAt)
                .Take(_opts.VirusScan.BatchSize)
                .ToListAsync(ct);

            // Lọc giãn cách ở bộ nhớ: biểu thức backoff luỹ thừa không dịch được sang SQL, và lô đã
            // bị giới hạn bởi BatchSize nên chi phí không đáng kể.
            var pending = due.Where(a => IsDue(a, now)).ToList();

            if (pending.Count == 0)
                return;

            _logger.LogInformation("VirusScanWorker: scanning {Count} attachments.", pending.Count);

            foreach (var attachment in pending)
            {
                if (ct.IsCancellationRequested)
                    break;

                await ScanOneAsync(unitOfWork, clamAv, files, attachment, ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "VirusScanWorker: error during scan cycle.");
        }
    }

    /// <summary>Bản ghi đã tới lượt thử lại chưa (giãn cách luỹ thừa theo số lần đã thử).</summary>
    private bool IsDue(TicketAttachment attachment, DateTime nowUtc)
    {
        if (attachment.VirusScanLastAttemptAt is null || attachment.VirusScanAttempts == 0)
            return true;

        var backoff = BackoffFor(attachment.VirusScanAttempts);
        return attachment.VirusScanLastAttemptAt.Value.Add(backoff) <= nowUtc;
    }

    /// <summary>Giãn cách trước lần thử thứ <paramref name="attempts"/>+1, nhân đôi mỗi lần, trần 1 giờ.</summary>
    public TimeSpan BackoffFor(int attempts)
    {
        var seconds = Math.Max(1, _opts.VirusScan.RetryBackoffSeconds) * Math.Pow(2, Math.Max(0, attempts - 1));
        return TimeSpan.FromSeconds(Math.Min(seconds, TimeSpan.FromHours(1).TotalSeconds));
    }

    /// <summary>
    /// Trả các bản ghi kẹt ở <c>Scanning</c> về hàng đợi.
    /// </summary>
    /// <remarks>
    /// Bản ghi vào <c>Scanning</c> ngay trước khi tải file. Tiến trình chết giữa chừng thì nó nằm lại
    /// đó vĩnh viễn: không khớp bộ lọc <c>Pending</c> nên không lượt nào nhặt, và đính kèm không bao
    /// giờ tải được — im lặng, không lỗi. KHÔNG đặt lại số lần thử: một lượt bị bỏ dở vẫn là một lần
    /// thử, nếu không thì sự cố lặp lại sẽ quay vòng mãi mà không chạm tới <c>MaxAttempts</c>.
    /// </remarks>
    private async Task ReclaimStaleScansAsync(ITicketUnitOfWork unitOfWork, CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow.AddSeconds(-Math.Max(60, _opts.VirusScan.ScanTimeoutSeconds));

        var stale = await unitOfWork.TicketAttachments
            .GetAllAsync()
            .Where(a => !a.IsDeleted
                        && a.VirusScanStatus == VirusScanStatusEnum.Scanning
                        && (a.VirusScanLastAttemptAt == null || a.VirusScanLastAttemptAt <= cutoff))
            .Take(_opts.VirusScan.BatchSize)
            .ToListAsync(ct);

        if (stale.Count == 0)
            return;

        foreach (var attachment in stale)
        {
            attachment.VirusScanStatus = VirusScanStatusEnum.Pending;
            unitOfWork.TicketAttachments.UpdateAsync(attachment);
        }

        await unitOfWork.SaveChangesAsync(ct);

        _logger.LogWarning(
            "VirusScanWorker: thu hồi {Count} đính kèm kẹt ở Scanning quá {Seconds}s.",
            stale.Count, _opts.VirusScan.ScanTimeoutSeconds);
    }

    private async Task ScanOneAsync(
        ITicketUnitOfWork unitOfWork,
        IClamAvClient clamAv,
        FileInternal.FileInternalClient files,
        TicketAttachment attachment,
        CancellationToken ct)
    {
        // CHIẾM trước khi tải: nhiều replica không cùng quét một đính kèm, và sau sự cố bản ghi nằm
        // ở Scanning chứ không rơi lại hàng đợi để bị quét lại ngay.
        attachment.VirusScanStatus = VirusScanStatusEnum.Scanning;
        attachment.VirusScanAttempts += 1;
        attachment.VirusScanLastAttemptAt = DateTime.UtcNow;
        unitOfWork.TicketAttachments.UpdateAsync(attachment);
        await unitOfWork.SaveChangesAsync(ct);

        VirusScanStatusEnum outcome;
        try
        {
            var bytes = await DownloadAsync(files, attachment.FileId, ct);
            using var stream = new MemoryStream(bytes, writable: false);
            outcome = await clamAv.ScanAsync(stream, attachment.FileName, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Dừng máy chủ giữa chừng: để nguyên ở Scanning, vòng sau sẽ thu hồi.
            return;
        }
        catch (Exception ex)
        {
            // Hết lượt thử mới coi là hỏng hẳn; còn lượt thì trả về hàng đợi và chờ giãn cách.
            var exhausted = attachment.VirusScanAttempts >= _opts.VirusScan.MaxAttempts;
            attachment.VirusScanStatus = exhausted ? VirusScanStatusEnum.Failed : VirusScanStatusEnum.Pending;
            unitOfWork.TicketAttachments.UpdateAsync(attachment);
            await unitOfWork.SaveChangesAsync(ct);

            _logger.Log(exhausted ? LogLevel.Error : LogLevel.Warning, ex,
                "VirusScanWorker: quét {FileId} ({FileName}) hỏng lần {Attempt}/{Max}.",
                attachment.FileId, attachment.FileName,
                attachment.VirusScanAttempts, _opts.VirusScan.MaxAttempts);
            return;
        }

        attachment.VirusScanStatus = outcome;
        unitOfWork.TicketAttachments.UpdateAsync(attachment);
        await unitOfWork.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Tải file qua kênh gRPC nội bộ.
    /// </summary>
    /// <remarks>
    /// <c>protected virtual</c> để test lớp con thay được đường tải mà không cần dựng cả máy chủ gRPC;
    /// bản production KHÔNG có ai override.
    /// </remarks>
    protected virtual async Task<byte[]> DownloadAsync(
        FileInternal.FileInternalClient files, Guid fileId, CancellationToken ct)
    {
        using var call = files.DownloadFile(
            new DownloadFileRequest { FileId = fileId.ToString() }, cancellationToken: ct);

        using var buffer = new MemoryStream();
        await foreach (var reply in call.ResponseStream.ReadAllAsync(ct))
            buffer.Write(reply.Chunk.Span);

        var bytes = buffer.ToArray();
        if (bytes.Length == 0)
        {
            // Tải về 0 byte rồi báo "sạch" nghĩa là đính kèm được đánh dấu an toàn mà chưa ai quét
            // nội dung thật của nó.
            throw new RpcException(new Status(StatusCode.DataLoss,
                $"Tải file {fileId} qua kênh nội bộ trả về 0 byte."));
        }

        return bytes;
    }
}
