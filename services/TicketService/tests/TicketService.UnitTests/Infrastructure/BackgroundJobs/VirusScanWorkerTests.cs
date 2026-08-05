using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MockQueryable.Moq;
using Moq;
using SharedContracts.Grpc.FileInternal;
using SharedKernels.Interfaces;
using TicketService.Application.Common.Models;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Interfaces.Services;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;
using TicketService.Infrastructure.BackgroundJobs;
using Xunit;

namespace TicketService.UnitTests.Infrastructure.BackgroundJobs;

/// <summary>
/// GH-790 — worker quét virus không xác thực được, đính kèm kẹt vĩnh viễn.
/// </summary>
/// <remarks>
/// <para>
/// Worker cũ tải file bằng <c>GET /api/files/{id}/download</c> mà không gắn token, trong khi
/// <c>FilesController</c> có <c>[Authorize]</c> ⇒ mọi lần tải đều 401. Nó ghi thẳng <c>Failed</c>,
/// mà bộ lọc lại chỉ nhặt <c>Pending</c> ⇒ bản ghi đó không bao giờ được thử lại. Đính kèm mãi mãi
/// trả 202 "đang quét, thử lại sau" và không ai tải được — không có lỗi nào nổi lên để đi tìm.
/// </para>
/// <para>
/// Lớp này kiểm máy trạng thái và đường thử lại. Việc tải qua kênh gRPC nội bộ được kiểm ở
/// <c>TicketService.IntegrationTests</c> với máy chủ gRPC thật.
/// </para>
/// </remarks>
public class VirusScanWorkerTests
{
    /// <summary>Lớp con thay đường tải để kiểm máy trạng thái mà không cần dựng máy chủ gRPC.</summary>
    private sealed class TestableVirusScanWorker : VirusScanWorker
    {
        private readonly Func<Guid, byte[]> _download;

        public TestableVirusScanWorker(
            IServiceProvider serviceProvider,
            IOptions<ChatOptions> opts,
            Func<Guid, byte[]> download)
            : base(new Mock<ILogger<VirusScanWorker>>().Object, serviceProvider, opts)
        {
            _download = download;
        }

        /// <summary>Trạng thái bản ghi ĐÚNG LÚC bắt đầu tải — dùng để kiểm việc chiếm việc.</summary>
        public List<VirusScanStatusEnum> StatusAtDownload { get; } = [];

        public TicketAttachment? Observed { get; set; }

        protected override Task<byte[]> DownloadAsync(
            FileInternal.FileInternalClient files, Guid fileId, CancellationToken ct)
        {
            if (Observed is not null)
                StatusAtDownload.Add(Observed.VirusScanStatus);

            return Task.FromResult(_download(fileId));
        }
    }

    /// <summary>Tái hiện đúng phép canh 0 byte của bản thật.</summary>
    private sealed class EmptyDownloadWorker : VirusScanWorker
    {
        public EmptyDownloadWorker(IServiceProvider sp, IOptions<ChatOptions> opts)
            : base(new Mock<ILogger<VirusScanWorker>>().Object, sp, opts) { }

        protected override Task<byte[]> DownloadAsync(
            FileInternal.FileInternalClient files, Guid fileId, CancellationToken ct)
            => throw new Grpc.Core.RpcException(new Grpc.Core.Status(
                Grpc.Core.StatusCode.DataLoss, $"Tải file {fileId} qua kênh nội bộ trả về 0 byte."));
    }

    private static IOptions<ChatOptions> BuildOpts(
        bool enableVirusScan = true, int maxAttempts = 3, int backoffSeconds = 60, int scanTimeout = 600) =>
        Options.Create(new ChatOptions
        {
            Features = new ChatOptions.FeaturesSection { EnableVirusScan = enableVirusScan },
            VirusScan = new ChatOptions.VirusScanSection
            {
                Endpoint = "http://clamav:3000",
                BatchSize = 10,
                IntervalSeconds = 30,
                MaxAttempts = maxAttempts,
                RetryBackoffSeconds = backoffSeconds,
                ScanTimeoutSeconds = scanTimeout,
            }
        });

    private static TicketAttachment Attachment(
        VirusScanStatusEnum status = VirusScanStatusEnum.Pending,
        int attempts = 0,
        DateTime? lastAttempt = null) => new()
        {
            Id = Guid.NewGuid(),
            FileId = Guid.NewGuid(),
            FileName = "tai-lieu.pdf",
            TicketId = Guid.NewGuid(),
            UploadedByUserId = Guid.NewGuid(),
            ContentType = "application/pdf",
            SizeBytes = 1024,
            VirusScanStatus = status,
            VirusScanAttempts = attempts,
            VirusScanLastAttemptAt = lastAttempt,
            CreatedAt = DateTime.UtcNow.AddMinutes(-10),
            Ticket = null!,
        };

    private static (IServiceProvider Provider,
                    Mock<ITicketUnitOfWork> Uow,
                    Mock<IGenericRepository<TicketAttachment>> Attachments,
                    Mock<IClamAvClient> ClamAv)
        BuildScope(IEnumerable<TicketAttachment> seed, VirusScanStatusEnum scanResult = VirusScanStatusEnum.Clean)
    {
        var list = seed.ToList();
        var attachmentsRepo = new Mock<IGenericRepository<TicketAttachment>>();
        // Dựng lại queryable ở MỖI lần gọi để test quan sát được thay đổi trạng thái giữa các bước
        // (thu hồi rồi mới quét là hai truy vấn khác nhau trong cùng một lượt).
        attachmentsRepo.Setup(r => r.GetAllAsync()).Returns(() => list.AsQueryable().BuildMock());
        attachmentsRepo.Setup(r => r.UpdateAsync(It.IsAny<TicketAttachment>()));

        var uow = new Mock<ITicketUnitOfWork>();
        uow.Setup(u => u.TicketAttachments).Returns(attachmentsRepo.Object);
        uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var clamAv = new Mock<IClamAvClient>();
        clamAv.Setup(c => c.ScanAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(scanResult);

        var sp = new Mock<IServiceProvider>();
        var scope = new Mock<IServiceScope>();
        var scopeFactory = new Mock<IServiceScopeFactory>();

        scope.Setup(s => s.ServiceProvider).Returns(sp.Object);
        scopeFactory.Setup(f => f.CreateScope()).Returns(scope.Object);
        sp.Setup(p => p.GetService(typeof(IServiceScopeFactory))).Returns(scopeFactory.Object);
        sp.Setup(p => p.GetService(typeof(ITicketUnitOfWork))).Returns(uow.Object);
        sp.Setup(p => p.GetService(typeof(IClamAvClient))).Returns(clamAv.Object);
        sp.Setup(p => p.GetService(typeof(FileInternal.FileInternalClient)))
          .Returns(new Mock<FileInternal.FileInternalClient>().Object);

        return (sp.Object, uow, attachmentsRepo, clamAv);
    }

    private static byte[] SomeBytes() => [1, 2, 3, 4, 5];

    // ── Đường chạy thành công ────────────────────────────────────────────────

    [Fact]
    public async Task CleanFile_GoesPendingThenScanningThenClean()
    {
        // Tiêu chí nghiệm thu: "Clean file đi từ Pending → Scanning → Clean".
        // Bản ghi phải RỜI hàng đợi trước khi tải, nếu không hai replica cùng quét một đính kèm.
        var attachment = Attachment();
        var (sp, _, _, _) = BuildScope([attachment]);

        var worker = new TestableVirusScanWorker(sp, BuildOpts(), _ => SomeBytes())
        {
            Observed = attachment
        };

        await worker.ScanPendingAttachmentsAsync(CancellationToken.None);

        worker.StatusAtDownload.Should().ContainSingle("chỉ được tải đúng một lần")
            .Which.Should().Be(VirusScanStatusEnum.Scanning, "phải chiếm bản ghi TRƯỚC khi tải file");
        attachment.VirusScanStatus.Should().Be(VirusScanStatusEnum.Clean);
        attachment.VirusScanAttempts.Should().Be(1);
        attachment.VirusScanLastAttemptAt.Should().NotBeNull();
    }

    [Fact]
    public async Task InfectedFile_IsMarkedInfected()
    {
        var attachment = Attachment();
        var (sp, _, _, _) = BuildScope([attachment], VirusScanStatusEnum.Infected);

        var worker = new TestableVirusScanWorker(sp, BuildOpts(), _ => SomeBytes());
        await worker.ScanPendingAttachmentsAsync(CancellationToken.None);

        attachment.VirusScanStatus.Should().Be(VirusScanStatusEnum.Infected);
    }

    [Fact]
    public async Task WhenDisabled_NothingIsQueried()
    {
        var (sp, _, attachments, _) = BuildScope([Attachment()]);

        var worker = new TestableVirusScanWorker(sp, BuildOpts(enableVirusScan: false), _ => SomeBytes());
        await worker.StartAsync(CancellationToken.None);
        await Task.Delay(150);
        await worker.StopAsync(CancellationToken.None);

        attachments.Verify(r => r.GetAllAsync(), Times.Never);
    }

    // ── Hỏng tạm thời không được kẹt vĩnh viễn ──────────────────────────────

    [Fact]
    public async Task DownloadFailure_ReturnsToQueue_NotStraightToFailed()
    {
        // ĐÂY là lỗi gốc: một lần hỏng (401) là ghi thẳng Failed, mà bộ lọc chỉ nhặt Pending ⇒
        // không bao giờ thử lại.
        var attachment = Attachment();
        var (sp, _, _, _) = BuildScope([attachment]);

        var worker = new TestableVirusScanWorker(sp, BuildOpts(maxAttempts: 3),
            _ => throw new InvalidOperationException("401 Unauthorized"));

        await worker.ScanPendingAttachmentsAsync(CancellationToken.None);

        attachment.VirusScanStatus.Should().Be(VirusScanStatusEnum.Pending,
            "còn lượt thử thì phải quay lại hàng đợi, không phải Failed");
        attachment.VirusScanAttempts.Should().Be(1);
    }

    [Fact]
    public async Task RepeatedFailures_EventuallyReachFailed()
    {
        // Chiều ngược lại: thử lại vô hạn cũng sai — file thật sự hỏng sẽ quay vòng mãi mãi.
        var attachment = Attachment(attempts: 2);
        var (sp, _, _, _) = BuildScope([attachment]);

        var worker = new TestableVirusScanWorker(sp, BuildOpts(maxAttempts: 3),
            _ => throw new InvalidOperationException("clamav down"));

        await worker.ScanPendingAttachmentsAsync(CancellationToken.None);

        attachment.VirusScanAttempts.Should().Be(3);
        attachment.VirusScanStatus.Should().Be(VirusScanStatusEnum.Failed);
    }

    [Fact]
    public async Task ExhaustedAttachment_IsNotPickedUpAgain()
    {
        // Hết lượt rồi thì thôi — nếu không, mỗi vòng quét lại tốn một lần gọi ClamAV vô ích.
        var attachment = Attachment(attempts: 3);
        var (sp, _, _, clamAv) = BuildScope([attachment]);

        var worker = new TestableVirusScanWorker(sp, BuildOpts(maxAttempts: 3), _ => SomeBytes());
        await worker.ScanPendingAttachmentsAsync(CancellationToken.None);

        clamAv.Verify(c => c.ScanAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RecentlyFailedAttachment_WaitsForItsBackoff()
    {
        // Thử lại ngay lập tức sẽ nện liên tục vào một service đang sự cố — chính là thứ làm nó lâu
        // hồi phục hơn.
        var attachment = Attachment(attempts: 1, lastAttempt: DateTime.UtcNow.AddSeconds(-5));
        var (sp, _, _, clamAv) = BuildScope([attachment]);

        var worker = new TestableVirusScanWorker(sp, BuildOpts(backoffSeconds: 60), _ => SomeBytes());
        await worker.ScanPendingAttachmentsAsync(CancellationToken.None);

        clamAv.Verify(c => c.ScanAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        attachment.VirusScanStatus.Should().Be(VirusScanStatusEnum.Pending);
    }

    [Fact]
    public async Task AttachmentPastItsBackoff_IsRetried()
    {
        var attachment = Attachment(attempts: 1, lastAttempt: DateTime.UtcNow.AddMinutes(-30));
        var (sp, _, _, _) = BuildScope([attachment]);

        var worker = new TestableVirusScanWorker(sp, BuildOpts(backoffSeconds: 60), _ => SomeBytes());
        await worker.ScanPendingAttachmentsAsync(CancellationToken.None);

        attachment.VirusScanStatus.Should().Be(VirusScanStatusEnum.Clean);
    }

    [Fact]
    public void Backoff_GrowsWithEachAttempt_ButIsCapped()
    {
        var worker = new TestableVirusScanWorker(
            BuildScope([]).Provider, BuildOpts(backoffSeconds: 60), _ => SomeBytes());

        worker.BackoffFor(1).Should().Be(TimeSpan.FromSeconds(60));
        worker.BackoffFor(2).Should().Be(TimeSpan.FromSeconds(120));
        worker.BackoffFor(3).Should().Be(TimeSpan.FromSeconds(240));
        worker.BackoffFor(20).Should().Be(TimeSpan.FromHours(1), "phải có trần, không hoãn tới vô tận");
    }

    [Fact]
    public async Task EmptyDownload_IsTreatedAsFailure_NotAsClean()
    {
        // Tải về 0 byte rồi báo "sạch" nghĩa là đính kèm được đánh dấu an toàn mà chưa ai quét nội
        // dung thật của nó — nguy hiểm hơn hẳn việc báo hỏng.
        var attachment = Attachment();
        var (sp, _, _, clamAv) = BuildScope([attachment]);

        var worker = new EmptyDownloadWorker(sp, BuildOpts());
        await worker.ScanPendingAttachmentsAsync(CancellationToken.None);

        clamAv.Verify(c => c.ScanAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never, "không được đưa 0 byte cho ClamAV rồi nhận kết quả 'sạch'");
        attachment.VirusScanStatus.Should().Be(VirusScanStatusEnum.Pending);
        attachment.VirusScanAttempts.Should().Be(1);
    }

    // ── Thu hồi lượt quét bị bỏ dở ───────────────────────────────────────────

    [Fact]
    public async Task StaleScanningRow_IsReturnedToTheQueue()
    {
        // Tiến trình chết giữa lúc quét thì bản ghi nằm mãi ở Scanning: không khớp bộ lọc Pending
        // nên không lượt nào nhặt, và đính kèm không bao giờ tải được — im lặng, không lỗi.
        var stuck = Attachment(VirusScanStatusEnum.Scanning, attempts: 1,
            lastAttempt: DateTime.UtcNow.AddHours(-2));
        var (sp, _, _, _) = BuildScope([stuck]);

        var worker = new TestableVirusScanWorker(sp, BuildOpts(scanTimeout: 600), _ => SomeBytes());
        await worker.ScanPendingAttachmentsAsync(CancellationToken.None);

        stuck.VirusScanStatus.Should().Be(VirusScanStatusEnum.Clean, "thu hồi rồi quét lại ngay trong lượt này");
        stuck.VirusScanAttempts.Should().Be(2, "một lượt bị bỏ dở vẫn là một lần thử");
    }

    [Fact]
    public async Task FreshScanningRow_IsLeftAlone()
    {
        // Thu hồi sớm sẽ khiến hai worker cùng quét một đính kèm — chính điều mà trạng thái Scanning
        // sinh ra để ngăn.
        var inFlight = Attachment(VirusScanStatusEnum.Scanning, attempts: 1, lastAttempt: DateTime.UtcNow);
        var (sp, _, _, clamAv) = BuildScope([inFlight]);

        var worker = new TestableVirusScanWorker(sp, BuildOpts(scanTimeout: 600), _ => SomeBytes());
        await worker.ScanPendingAttachmentsAsync(CancellationToken.None);

        inFlight.VirusScanStatus.Should().Be(VirusScanStatusEnum.Scanning);
        clamAv.Verify(c => c.ScanAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ScanTimeout_HasAFloor_SoAConfigTypoCannotCauseDoubleScans()
    {
        var inFlight = Attachment(VirusScanStatusEnum.Scanning, attempts: 1, lastAttempt: DateTime.UtcNow);
        var (sp, _, _, _) = BuildScope([inFlight]);

        var worker = new TestableVirusScanWorker(sp, BuildOpts(scanTimeout: 0), _ => SomeBytes());
        await worker.ScanPendingAttachmentsAsync(CancellationToken.None);

        inFlight.VirusScanStatus.Should().Be(VirusScanStatusEnum.Scanning);
    }

    [Fact]
    public async Task NoWork_DoesNothing()
    {
        var (sp, uow, _, _) = BuildScope([]);

        var worker = new TestableVirusScanWorker(sp, BuildOpts(), _ => SomeBytes());
        await worker.ScanPendingAttachmentsAsync(CancellationToken.None);

        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SoftDeletedAttachment_IsIgnored()
    {
        var deleted = Attachment();
        deleted.IsDeleted = true;
        var (sp, _, _, clamAv) = BuildScope([deleted]);

        var worker = new TestableVirusScanWorker(sp, BuildOpts(), _ => SomeBytes());
        await worker.ScanPendingAttachmentsAsync(CancellationToken.None);

        clamAv.Verify(c => c.ScanAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
