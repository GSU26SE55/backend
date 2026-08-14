using AuditAggregatorService.Application.Consumers;
using AuditAggregatorService.Application.CQRS.Handler.Audit;
using AuditAggregatorService.Application.CQRS.Query.Audit;
using AuditAggregatorService.Application.Interfaces;
using AuditAggregatorService.Domain.Entities;
using FluentAssertions;
using MassTransit;
using Microsoft.Extensions.Logging.Abstractions;
using MockQueryable.Moq;
using Moq;
using SharedContracts.Audit;
using SharedContracts.Events.Audit;
using SharedKernels.Interfaces;
using Xunit;

namespace AuditAggregatorService.IntegrationTests.Audit;

/// <summary>
/// GH-728 — cộng dồn tiến độ replay + endpoint tra cứu.
/// </summary>
public class AuditReplayProgressGh728Tests
{
    private static AuditReplayJob Job(int expected, string? service = null) => new()
    {
        Id = Guid.NewGuid(),
        ServiceName = service,
        Status = AuditReplayJobStatus.Requested,
        ExpectedResponders = expected,
        RespondedServices = string.Empty,
        RequestedAtUtc = DateTime.UtcNow
    };

    private static (AuditReplayCompletedConsumer Sut, Mock<IAuditAggregatorUnitOfWork> Uow)
        BuildConsumer(params AuditReplayJob[] jobs)
    {
        var repo = new Mock<IGenericRepository<AuditReplayJob>>();
        repo.Setup(r => r.GetAllAsync()).Returns(jobs.AsQueryable().BuildMock());
        repo.Setup(r => r.UpdateAsync(It.IsAny<AuditReplayJob>()));

        var uow = new Mock<IAuditAggregatorUnitOfWork>();
        uow.SetupGet(u => u.AuditReplayJobs).Returns(repo.Object);
        uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        return (new AuditReplayCompletedConsumer(
            uow.Object, NullLogger<AuditReplayCompletedConsumer>.Instance), uow);
    }

    private static ConsumeContext<AuditReplayCompletedEvent> Ctx(AuditReplayCompletedEvent evt)
    {
        var ctx = new Mock<ConsumeContext<AuditReplayCompletedEvent>>();
        ctx.SetupGet(c => c.Message).Returns(evt);
        ctx.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);
        return ctx.Object;
    }

    private static AuditReplayCompletedEvent Done(
        Guid jobId, string service, int count = 5, bool ok = true, bool truncated = false, string? error = null)
        => new(jobId, service, count, ok, error, truncated, DateTime.UtcNow);

    [Fact]
    public async Task PartialResponses_KeepJobInProgress()
    {
        var job = Job(expected: AuditServiceNames.All.Count);
        var (sut, _) = BuildConsumer(job);

        await sut.Consume(Ctx(Done(job.Id, AuditServiceNames.Auth, count: 3)));

        job.Status.Should().Be(AuditReplayJobStatus.InProgress);
        job.RespondedCount.Should().Be(1);
        job.RepublishedCount.Should().Be(3);
        job.CompletedAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task AllServicesRespond_JobCompleted_WithSummedCount()
    {
        var job = Job(expected: AuditServiceNames.All.Count);
        var (sut, _) = BuildConsumer(job);

        foreach (var svc in AuditServiceNames.All)
            await sut.Consume(Ctx(Done(job.Id, svc, count: 2)));

        job.Status.Should().Be(AuditReplayJobStatus.Completed);
        job.RespondedCount.Should().Be(AuditServiceNames.All.Count);
        job.RepublishedCount.Should().Be(2 * AuditServiceNames.All.Count);
        job.CompletedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task DuplicateReportFromSameService_IsIgnored()
    {
        // MassTransit có thể giao lại message. Chỉ tăng biến đếm là sẽ đếm trùng và đóng
        // job sớm khi các service khác chưa hề chạy.
        var job = Job(expected: AuditServiceNames.All.Count);
        var (sut, _) = BuildConsumer(job);

        await sut.Consume(Ctx(Done(job.Id, AuditServiceNames.Auth, count: 4)));
        await sut.Consume(Ctx(Done(job.Id, AuditServiceNames.Auth, count: 4)));

        job.RespondedCount.Should().Be(1);
        job.RepublishedCount.Should().Be(4, "không được cộng dồn hai lần");
        job.Status.Should().Be(AuditReplayJobStatus.InProgress);
    }

    [Fact]
    public async Task ServiceReportsFailure_JobCompletedWithErrors()
    {
        var job = Job(expected: 1, service: AuditServiceNames.Battery);
        var (sut, _) = BuildConsumer(job);

        await sut.Consume(Ctx(Done(job.Id, AuditServiceNames.Battery, count: 0, ok: false, error: "DB timeout")));

        job.Status.Should().Be(AuditReplayJobStatus.CompletedWithErrors);
        job.Error.Should().Contain("BatteryService").And.Contain("DB timeout");
    }

    [Fact]
    public async Task TruncatedResponse_NeverReportsCleanCompletion()
    {
        // Quan trọng: dữ liệu chưa đầy đủ mà báo "Completed" sẽ khiến người vận hành tin nhầm.
        var job = Job(expected: 1, service: AuditServiceNames.Ticket);
        var (sut, _) = BuildConsumer(job);

        await sut.Consume(Ctx(Done(job.Id, AuditServiceNames.Ticket, count: 50_000, truncated: true)));

        job.Truncated.Should().BeTrue();
        job.Status.Should().Be(AuditReplayJobStatus.CompletedWithErrors);
    }

    [Fact]
    public async Task UnknownJob_IsIgnoredWithoutThrowing()
    {
        // Ném sẽ khiến MassTransit retry vô hạn một job không tồn tại.
        var (sut, uow) = BuildConsumer();

        var act = async () => await sut.Consume(Ctx(Done(Guid.NewGuid(), AuditServiceNames.Sms)));

        await act.Should().NotThrowAsync();
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // ───────── endpoint tra cứu ─────────

    [Fact]
    public async Task GetJob_ReportsPendingServices()
    {
        var job = Job(expected: AuditServiceNames.All.Count);
        job.RespondedServices = $"{AuditServiceNames.Auth},{AuditServiceNames.Battery}";
        job.RespondedCount = 2;
        job.Status = AuditReplayJobStatus.InProgress;

        var repo = new Mock<IGenericRepository<AuditReplayJob>>();
        repo.Setup(r => r.GetAllAsync()).Returns(new[] { job }.AsQueryable().BuildMock());
        var uow = new Mock<IAuditAggregatorUnitOfWork>();
        uow.SetupGet(u => u.AuditReplayJobs).Returns(repo.Object);

        var result = await new AuditReplayJobGetByIdQueryHandler(uow.Object)
            .Handle(new AuditReplayJobGetByIdQuery { JobId = job.Id }, CancellationToken.None);

        result.StatusCode.Should().Be(200);
        result.Data!.RespondedServices.Should().BeEquivalentTo(
            new[] { AuditServiceNames.Auth, AuditServiceNames.Battery });
        // Câu hỏi đầu tiên khi job treo: đang chờ ai?
        result.Data.PendingServices.Should().BeEquivalentTo(
            new[] { AuditServiceNames.Ticket, AuditServiceNames.Notification, AuditServiceNames.Sms, AuditServiceNames.FileStorage });
    }

    [Fact]
    public async Task GetJob_NotFound_404()
    {
        var repo = new Mock<IGenericRepository<AuditReplayJob>>();
        repo.Setup(r => r.GetAllAsync()).Returns(Array.Empty<AuditReplayJob>().AsQueryable().BuildMock());
        var uow = new Mock<IAuditAggregatorUnitOfWork>();
        uow.SetupGet(u => u.AuditReplayJobs).Returns(repo.Object);

        var result = await new AuditReplayJobGetByIdQueryHandler(uow.Object)
            .Handle(new AuditReplayJobGetByIdQuery { JobId = Guid.NewGuid() }, CancellationToken.None);

        result.StatusCode.Should().Be(404);
    }
}
