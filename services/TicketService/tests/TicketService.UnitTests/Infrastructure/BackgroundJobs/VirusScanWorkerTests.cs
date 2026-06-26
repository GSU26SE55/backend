using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MockQueryable.Moq;
using Moq;
using SharedKernels.Interfaces;
using TicketService.Application.Common.Models;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Interfaces.Services;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;
using TicketService.Infrastructure.BackgroundJobs;
using Xunit;

namespace TicketService.UnitTests.Infrastructure.BackgroundJobs;

public class VirusScanWorkerTests
{
    // Subclass để override DownloadAndScanAsync trong tests
    private sealed class TestableVirusScanWorker : VirusScanWorker
    {
        private readonly Func<Guid, VirusScanStatusEnum> _scanResult;

        public TestableVirusScanWorker(
            IServiceProvider serviceProvider,
            IHttpClientFactory httpClientFactory,
            IOptions<ChatOptions> opts,
            Func<Guid, VirusScanStatusEnum> scanResult)
            : base(
                new Mock<ILogger<VirusScanWorker>>().Object,
                serviceProvider,
                httpClientFactory,
                opts)
        {
            _scanResult = scanResult;
        }

        protected override Task<VirusScanStatusEnum> DownloadAndScanAsync(
            IClamAvClient clamAv, Guid fileId, string fileName, CancellationToken ct)
            => Task.FromResult(_scanResult(fileId));
    }

    private static IOptions<ChatOptions> BuildOpts(bool enableVirusScan) =>
        Options.Create(new ChatOptions
        {
            Features = new ChatOptions.FeaturesSection { EnableVirusScan = enableVirusScan },
            VirusScan = new ChatOptions.VirusScanSection
            {
                Endpoint = "http://clamav:3000",
                BatchSize = 10,
                IntervalSeconds = 30,
                FileStorageBaseUrl = "http://file-storage"
            }
        });

    private static (IServiceProvider provider,
                    Mock<ITicketUnitOfWork> uow,
                    Mock<IGenericRepository<TicketAttachment>> attachments)
        BuildScope(IEnumerable<TicketAttachment> seed)
    {
        var attachmentsMock = seed.BuildMock();
        var attachmentsRepo = new Mock<IGenericRepository<TicketAttachment>>();
        attachmentsRepo.Setup(r => r.GetAllAsync()).Returns(attachmentsMock);

        var uow = new Mock<ITicketUnitOfWork>();
        uow.Setup(u => u.TicketAttachments).Returns(attachmentsRepo.Object);
        uow.Setup(u => u.CommitTransactionAsync()).Returns(Task.CompletedTask);

        var clamAv = new Mock<IClamAvClient>();

        var sp = new Mock<IServiceProvider>();
        var scope = new Mock<IServiceScope>();
        var scopeFactory = new Mock<IServiceScopeFactory>();

        scope.Setup(s => s.ServiceProvider).Returns(sp.Object);
        scopeFactory.Setup(f => f.CreateScope()).Returns(scope.Object);
        sp.Setup(p => p.GetService(typeof(IServiceScopeFactory))).Returns(scopeFactory.Object);
        sp.Setup(p => p.GetService(typeof(ITicketUnitOfWork))).Returns(uow.Object);
        sp.Setup(p => p.GetService(typeof(IClamAvClient))).Returns(clamAv.Object);

        return (sp.Object, uow, attachmentsRepo);
    }

    [Fact]
    public async Task When_VirusScanDisabled_ShouldNot_QueryAttachments()
    {
        // Arrange
        var attachment = new TicketAttachment
        {
            Id = Guid.NewGuid(),
            FileId = Guid.NewGuid(),
            FileName = "test.pdf",
            TicketId = Guid.NewGuid(),
            UploadedByUserId = Guid.NewGuid(),
            ContentType = "application/pdf",
            SizeBytes = 1024,
            VirusScanStatus = VirusScanStatusEnum.Pending,
            Ticket = null!
        };

        var (sp, uow, attachmentsRepo) = BuildScope(new[] { attachment });
        var scanCalled = false;

        var worker = new TestableVirusScanWorker(
            sp,
            new Mock<IHttpClientFactory>().Object,
            BuildOpts(enableVirusScan: false),
            _ => { scanCalled = true; return VirusScanStatusEnum.Clean; });

        // Act — khi disabled, ScanPendingAttachmentsAsync không được gọi từ ExecuteAsync
        // Verify trực tiếp bằng cách run một vòng lặp với CancellationToken timeout ngắn
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await worker.StartAsync(CancellationToken.None);
        try
        { await Task.Delay(200, cts.Token); }
        catch { }
        await worker.StopAsync(CancellationToken.None);

        // Assert
        scanCalled.Should().BeFalse();
        attachmentsRepo.Verify(r => r.GetAllAsync(), Times.Never);
    }

    [Fact]
    public async Task When_AttachmentIsClean_ShouldUpdateStatusToClean()
    {
        // Arrange
        var fileId = Guid.NewGuid();
        var attachment = new TicketAttachment
        {
            Id = Guid.NewGuid(),
            FileId = fileId,
            FileName = "clean.pdf",
            TicketId = Guid.NewGuid(),
            UploadedByUserId = Guid.NewGuid(),
            ContentType = "application/pdf",
            SizeBytes = 1024,
            VirusScanStatus = VirusScanStatusEnum.Pending,
            Ticket = null!
        };

        var (sp, uow, _) = BuildScope(new[] { attachment });

        var worker = new TestableVirusScanWorker(
            sp,
            new Mock<IHttpClientFactory>().Object,
            BuildOpts(enableVirusScan: true),
            _ => VirusScanStatusEnum.Clean);

        // Act
        await worker.ScanPendingAttachmentsAsync(CancellationToken.None);

        // Assert
        attachment.VirusScanStatus.Should().Be(VirusScanStatusEnum.Clean);
        uow.Verify(u => u.CommitTransactionAsync(), Times.Once);
    }

    [Fact]
    public async Task When_AttachmentIsInfected_ShouldUpdateStatusToInfected()
    {
        // Arrange
        var fileId = Guid.NewGuid();
        var attachment = new TicketAttachment
        {
            Id = Guid.NewGuid(),
            FileId = fileId,
            FileName = "virus.exe",
            TicketId = Guid.NewGuid(),
            UploadedByUserId = Guid.NewGuid(),
            ContentType = "application/octet-stream",
            SizeBytes = 512,
            VirusScanStatus = VirusScanStatusEnum.Pending,
            Ticket = null!
        };

        var (sp, uow, _) = BuildScope(new[] { attachment });

        var worker = new TestableVirusScanWorker(
            sp,
            new Mock<IHttpClientFactory>().Object,
            BuildOpts(enableVirusScan: true),
            _ => VirusScanStatusEnum.Infected);

        // Act
        await worker.ScanPendingAttachmentsAsync(CancellationToken.None);

        // Assert
        attachment.VirusScanStatus.Should().Be(VirusScanStatusEnum.Infected);
        uow.Verify(u => u.CommitTransactionAsync(), Times.Once);
    }

    [Fact]
    public async Task When_NoPendingAttachments_ShouldNotCommit()
    {
        // Arrange
        var (sp, uow, _) = BuildScope(Array.Empty<TicketAttachment>());

        var worker = new TestableVirusScanWorker(
            sp,
            new Mock<IHttpClientFactory>().Object,
            BuildOpts(enableVirusScan: true),
            _ => VirusScanStatusEnum.Clean);

        // Act
        await worker.ScanPendingAttachmentsAsync(CancellationToken.None);

        // Assert
        uow.Verify(u => u.CommitTransactionAsync(), Times.Never);
    }
}
