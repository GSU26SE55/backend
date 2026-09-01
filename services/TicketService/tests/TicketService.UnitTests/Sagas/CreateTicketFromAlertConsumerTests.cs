using MassTransit;
using MassTransit.Testing;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SharedContracts.Common.Responses;
using SharedContracts.Saga.AlertTicket;
using SharedKernels.Interfaces;
using TicketService.Application.CQRS.Command.Tickets;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;
using TicketService.Infrastructure.Consumers;

namespace TicketService.UnitTests.Sagas;

/// <summary>
/// Sprint 5B #238 — idempotency tests cho CreateTicketFromAlertConsumer.
/// </summary>
public class CreateTicketFromAlertConsumerTests
{
    private static (Mock<IMediator> Mediator, Mock<ITicketUnitOfWork> Uow, Mock<IGenericRepository<Ticket>> TicketRepo)
        BuildMocks(List<Ticket> tickets)
    {
        var mediator = new Mock<IMediator>();
        var uow = new Mock<ITicketUnitOfWork>();
        var ticketRepo = new Mock<IGenericRepository<Ticket>>();
        ticketRepo.Setup(r => r.GetAllAsync()).Returns(tickets.AsQueryable().BuildMock());
        uow.SetupGet(u => u.Tickets).Returns(ticketRepo.Object);
        return (mediator, uow, ticketRepo);
    }

    private static CreateTicketFromAlertCommand MakeMsg(Guid alertId, Guid assetId, Guid? siteId = null)
        => new(
            CorrelationId: alertId, AlertId: alertId, BatteryAssetId: assetId,
            CustomerId: Guid.NewGuid(), AssetSerialNumber: "BMS-1",
            AnomalyType: 1, Severity: 3,
            ThresholdValue: 60m, ActualValue: 75m, Unit: "C",
            DetectedAt: DateTime.UtcNow,
            AnomalyCategory: "Overheat", Title: "Overheat", Description: "Detected",
            SiteId: siteId);

    /// <summary>Lenh cho alert CAP SITE: khong thuoc vien pin nao, mang SiteId + loai su co.</summary>
    private static CreateTicketFromAlertCommand MakeSiteMsg(Guid alertId, Guid siteId, int anomalyType)
        => new(
            CorrelationId: alertId, AlertId: alertId, BatteryAssetId: Guid.Empty,
            CustomerId: Guid.NewGuid(), AssetSerialNumber: "DEMO-SITE",
            AnomalyType: anomalyType, Severity: 3,
            ThresholdValue: 43m, ActualValue: 69m, Unit: "C",
            DetectedAt: DateTime.UtcNow,
            AnomalyCategory: "HighAmbientTemp", Title: "Environmental incident", Description: "Detected",
            SiteId: siteId);

    private static Ticket SiteTicket(Guid siteId, int anomalyType, string code) => new()
    {
        Id = Guid.NewGuid(),
        Code = code,
        BatteryAssetId = Guid.Empty,
        SiteId = siteId,
        AnomalyType = anomalyType,
        CustomerId = Guid.NewGuid(),
        OriginAlertId = Guid.NewGuid(),
        // Ca nam loai moi truong deu map ve Repair — dung cai lam ba su co bi gop lam mot.
        Category = TicketCategoryEnum.Repair,
        Title = "x",
        Description = "x",
        Status = TicketStatusEnum.InProgress,
        Origin = TicketOriginEnum.AutoFromEnvironment
    };

    private static ServiceProvider BuildHarnessProvider(Mock<IMediator> mediator, Mock<ITicketUnitOfWork> uow)
        => new ServiceCollection()
            .AddMassTransitTestHarness(x =>
            {
                x.AddConsumer<CreateTicketFromAlertConsumer>();
                x.SetTestTimeouts(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(15));
            })
            .AddSingleton(mediator.Object)
            .AddSingleton(uow.Object)
            .AddSingleton(NullLogger<CreateTicketFromAlertConsumer>.Instance)
            .BuildServiceProvider(true);

    // Gas dang mo o DEMO-V2, gio den luot qua nhiet o CHINH site do => phai ra ticket RIENG.
    //
    // Truoc day check tai su dung chi so (BatteryAssetId, Category); su co moi truong cap site
    // deu la (Guid.Empty, Repair) nen ba loai bi coi la mot: cai nao no truoc chiem ticket, hai
    // cai sau chi duoc gan vao do. Ba van de khac nhau, ba cach xu ly khac nhau.
    [Fact]
    public async Task SiteLevelAlert_DifferentAnomalyOnSameSite_ShouldCreateSeparateTicket()
    {
        var siteId = Guid.NewGuid();
        var existingGas = SiteTicket(siteId, anomalyType: 18, code: "TCK-GAS");

        var (mediator, uow, _) = BuildMocks(new List<Ticket> { existingGas });
        mediator
            .Setup(m => m.Send(It.IsAny<TicketAutoCreateFromAlertCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TicketActionResponse
            {
                IsSuccess = true,
                StatusCode = 201,
                Data = new TicketActionDTO
                {
                    Id = Guid.NewGuid().ToString(),
                    TicketId = Guid.NewGuid().ToString(),
                    Code = "TCK-TEMP"
                }
            });

        await using var provider = BuildHarnessProvider(mediator, uow);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        // Qua nhiet moi truong (type 9) tren cung site dang co ticket gas (type 18).
        await harness.Bus.Publish(MakeSiteMsg(Guid.NewGuid(), siteId, anomalyType: 9));
        (await harness.Consumed.Any<CreateTicketFromAlertCommand>()).Should().BeTrue();

        var published = harness.Published.Select<TicketCreatedFromAlertResponse>().ToList();
        published.Should().ContainSingle();
        published[0].Context.Message.IsReused.Should().BeFalse("moi loai su co moi truong la mot ticket rieng");
        mediator.Verify(m => m.Send(It.IsAny<TicketAutoCreateFromAlertCommand>(),
            It.IsAny<CancellationToken>()), Times.Once);

        await harness.Stop();
    }

    // Cung site + CUNG loai su co dang mo => van tai su dung. Neu bo not lop dedup nay thi nhiet do
    // dao dong quanh nguong se de ra hang loat ticket cho cung mot su co.
    [Fact]
    public async Task SiteLevelAlert_SameAnomalyOnSameSite_ShouldReuse()
    {
        var siteId = Guid.NewGuid();
        var existing = SiteTicket(siteId, anomalyType: 9, code: "TCK-TEMP");

        var (mediator, uow, _) = BuildMocks(new List<Ticket> { existing });

        await using var provider = BuildHarnessProvider(mediator, uow);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        await harness.Bus.Publish(MakeSiteMsg(Guid.NewGuid(), siteId, anomalyType: 9));
        (await harness.Consumed.Any<CreateTicketFromAlertCommand>()).Should().BeTrue();

        var published = harness.Published.Select<TicketCreatedFromAlertResponse>().ToList();
        published.Should().ContainSingle();
        published[0].Context.Message.IsReused.Should().BeTrue();
        published[0].Context.Message.TicketId.Should().Be(existing.Id);
        mediator.Verify(m => m.Send(It.IsAny<TicketAutoCreateFromAlertCommand>(),
            It.IsAny<CancellationToken>()), Times.Never);

        await harness.Stop();
    }

    /// <summary>Lenh cho alert CAP SITE: khong thuoc vien pin nao, mang SiteId + loai su co.</summary>
    private static CreateTicketFromAlertCommand MakeSiteMsg(Guid alertId, Guid siteId, int anomalyType)
        => new(
            CorrelationId: alertId, AlertId: alertId, BatteryAssetId: Guid.Empty,
            CustomerId: Guid.NewGuid(), AssetSerialNumber: "DEMO-SITE",
            AnomalyType: anomalyType, Severity: 3,
            ThresholdValue: 43m, ActualValue: 69m, Unit: "C",
            DetectedAt: DateTime.UtcNow,
            AnomalyCategory: "HighAmbientTemp", Title: "Environmental incident", Description: "Detected",
            SiteId: siteId);

    private static Ticket SiteTicket(Guid siteId, int anomalyType, string code) => new()
    {
        Id = Guid.NewGuid(),
        Code = code,
        BatteryAssetId = Guid.Empty,
        SiteId = siteId,
        AnomalyType = anomalyType,
        CustomerId = Guid.NewGuid(),
        OriginAlertId = Guid.NewGuid(),
        // Ca nam loai moi truong deu map ve Repair — dung cai lam ba su co bi gop lam mot.
        Category = TicketCategoryEnum.Repair,
        Title = "x",
        Description = "x",
        Status = TicketStatusEnum.InProgress,
        Origin = TicketOriginEnum.AutoFromEnvironment
    };

    private static ServiceProvider BuildHarnessProvider(Mock<IMediator> mediator, Mock<ITicketUnitOfWork> uow)
        => new ServiceCollection()
            .AddMassTransitTestHarness(x =>
            {
                x.AddConsumer<CreateTicketFromAlertConsumer>();
                x.SetTestTimeouts(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(15));
            })
            .AddSingleton(mediator.Object)
            .AddSingleton(uow.Object)
            .AddSingleton(NullLogger<CreateTicketFromAlertConsumer>.Instance)
            .BuildServiceProvider(true);

    // Gas dang mo o DEMO-V2, gio den luot qua nhiet o CHINH site do => phai ra ticket RIENG.
    //
    // Truoc day check tai su dung chi so (BatteryAssetId, Category); su co moi truong cap site
    // deu la (Guid.Empty, Repair) nen ba loai bi coi la mot: cai nao no truoc chiem ticket, hai
    // cai sau chi duoc gan vao do. Ba van de khac nhau, ba cach xu ly khac nhau.
    [Fact]
    public async Task SiteLevelAlert_DifferentAnomalyOnSameSite_ShouldCreateSeparateTicket()
    {
        var siteId = Guid.NewGuid();
        var existingGas = SiteTicket(siteId, anomalyType: 18, code: "TCK-GAS");

        var (mediator, uow, _) = BuildMocks(new List<Ticket> { existingGas });
        mediator
            .Setup(m => m.Send(It.IsAny<TicketAutoCreateFromAlertCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TicketActionResponse
            {
                IsSuccess = true, StatusCode = 201,
                Data = new TicketActionDTO
                {
                    Id = Guid.NewGuid().ToString(),
                    TicketId = Guid.NewGuid().ToString(),
                    Code = "TCK-TEMP"
                }
            });

        await using var provider = BuildHarnessProvider(mediator, uow);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        // Qua nhiet moi truong (type 9) tren cung site dang co ticket gas (type 18).
        await harness.Bus.Publish(MakeSiteMsg(Guid.NewGuid(), siteId, anomalyType: 9));
        (await harness.Consumed.Any<CreateTicketFromAlertCommand>()).Should().BeTrue();

        var published = harness.Published.Select<TicketCreatedFromAlertResponse>().ToList();
        published.Should().ContainSingle();
        published[0].Context.Message.IsReused.Should().BeFalse("moi loai su co moi truong la mot ticket rieng");
        mediator.Verify(m => m.Send(It.IsAny<TicketAutoCreateFromAlertCommand>(),
            It.IsAny<CancellationToken>()), Times.Once);

        await harness.Stop();
    }

    // Cung site + CUNG loai su co dang mo => van tai su dung. Neu bo not lop dedup nay thi nhiet do
    // dao dong quanh nguong se de ra hang loat ticket cho cung mot su co.
    [Fact]
    public async Task SiteLevelAlert_SameAnomalyOnSameSite_ShouldReuse()
    {
        var siteId = Guid.NewGuid();
        var existing = SiteTicket(siteId, anomalyType: 9, code: "TCK-TEMP");

        var (mediator, uow, _) = BuildMocks(new List<Ticket> { existing });

        await using var provider = BuildHarnessProvider(mediator, uow);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        await harness.Bus.Publish(MakeSiteMsg(Guid.NewGuid(), siteId, anomalyType: 9));
        (await harness.Consumed.Any<CreateTicketFromAlertCommand>()).Should().BeTrue();

        var published = harness.Published.Select<TicketCreatedFromAlertResponse>().ToList();
        published.Should().ContainSingle();
        published[0].Context.Message.IsReused.Should().BeTrue();
        published[0].Context.Message.TicketId.Should().Be(existing.Id);
        mediator.Verify(m => m.Send(It.IsAny<TicketAutoCreateFromAlertCommand>(),
            It.IsAny<CancellationToken>()), Times.Never);

        await harness.Stop();
    }

    [Fact]
    public async Task RedeliverySameAlertId_ShouldPublishReusedResponse_NotInvokeMediator()
    {
        var alertId = Guid.NewGuid();
        var assetId = Guid.NewGuid();
        var existing = new Ticket
        {
            Id = Guid.NewGuid(),
            Code = "TCK-001",
            BatteryAssetId = assetId,
            CustomerId = Guid.NewGuid(),
            OriginAlertId = alertId,
            Category = TicketCategoryEnum.Overheat,
            Title = "x",
            Description = "x",
            Status = TicketStatusEnum.Open,
            Origin = TicketOriginEnum.AutoFromAlert
        };

        var (mediator, uow, _) = BuildMocks(new List<Ticket> { existing });

        var provider = new ServiceCollection()
            .AddMassTransitTestHarness(x =>
            {
                x.AddConsumer<CreateTicketFromAlertConsumer>();
                // Sửa flaky 2026-07-31 — inactivity timeout mặc định của MassTransit v8 chỉ 1 giây;
                // `Consumed.Any<T>()` trả `false` cả khi hết giờ lẫn khi hỏng thật. Chạy cả solution
                // song song thì trượt ngưỡng. Khuôn: NotificationService/Helpers/ConsumerTestHarness.cs.
                x.SetTestTimeouts(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(15));
            })
            .AddSingleton(mediator.Object)
            .AddSingleton(uow.Object)
            .AddSingleton(NullLogger<CreateTicketFromAlertConsumer>.Instance)
            .BuildServiceProvider(true);

        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        await harness.Bus.Publish(MakeMsg(alertId, assetId));
        (await harness.Consumed.Any<CreateTicketFromAlertCommand>()).Should().BeTrue();

        var published = harness.Published.Select<TicketCreatedFromAlertResponse>().ToList();
        published.Should().ContainSingle();
        published[0].Context.Message.IsReused.Should().BeTrue();
        published[0].Context.Message.TicketId.Should().Be(existing.Id);

        mediator.Verify(m => m.Send(It.IsAny<TicketAutoCreateFromAlertCommand>(),
            It.IsAny<CancellationToken>()), Times.Never);

        await harness.Stop();
    }

    [Fact]
    public async Task ActiveTicketSameAssetCategory_ShouldReuse()
    {
        var alertId = Guid.NewGuid();
        var assetId = Guid.NewGuid();
        var existing = new Ticket
        {
            Id = Guid.NewGuid(),
            Code = "TCK-EXIST",
            BatteryAssetId = assetId,
            CustomerId = Guid.NewGuid(),
            OriginAlertId = Guid.NewGuid(), // khác AlertId hiện tại
            Category = TicketCategoryEnum.Overheat,
            Title = "x",
            Description = "x",
            Status = TicketStatusEnum.InProgress,
            Origin = TicketOriginEnum.AutoFromAlert
        };

        var (mediator, uow, _) = BuildMocks(new List<Ticket> { existing });

        var provider = new ServiceCollection()
            .AddMassTransitTestHarness(x =>
            {
                x.AddConsumer<CreateTicketFromAlertConsumer>();
                // Sửa flaky 2026-07-31 — inactivity timeout mặc định của MassTransit v8 chỉ 1 giây;
                // `Consumed.Any<T>()` trả `false` cả khi hết giờ lẫn khi hỏng thật. Chạy cả solution
                // song song thì trượt ngưỡng. Khuôn: NotificationService/Helpers/ConsumerTestHarness.cs.
                x.SetTestTimeouts(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(15));
            })
            .AddSingleton(mediator.Object)
            .AddSingleton(uow.Object)
            .AddSingleton(NullLogger<CreateTicketFromAlertConsumer>.Instance)
            .BuildServiceProvider(true);

        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        await harness.Bus.Publish(MakeMsg(alertId, assetId));
        (await harness.Consumed.Any<CreateTicketFromAlertCommand>()).Should().BeTrue();

        var published = harness.Published.Select<TicketCreatedFromAlertResponse>().ToList();
        published.Should().ContainSingle();
        published[0].Context.Message.IsReused.Should().BeTrue();
        published[0].Context.Message.TicketId.Should().Be(existing.Id);

        await harness.Stop();
    }

    [Fact]
    public async Task BatteryLevelAlert_WithSiteId_StillDeduplicatesByAssetAndCategory()
    {
        var siteId = Guid.NewGuid();
        var assetId = Guid.NewGuid();
        var existing = new Ticket
        {
            Id = Guid.NewGuid(),
            Code = "TCK-ASSET-AT-SITE",
            BatteryAssetId = assetId,
            SiteId = siteId,
            AnomalyType = 1,
            CustomerId = Guid.NewGuid(),
            OriginAlertId = Guid.NewGuid(),
            Category = TicketCategoryEnum.Overheat,
            Title = "x",
            Description = "x",
            Status = TicketStatusEnum.InProgress,
            Origin = TicketOriginEnum.AutoFromAlert
        };

        var (mediator, uow, _) = BuildMocks([existing]);

        await using var provider = BuildHarnessProvider(mediator, uow);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        await harness.Bus.Publish(MakeMsg(Guid.NewGuid(), assetId, siteId));
        (await harness.Consumed.Any<CreateTicketFromAlertCommand>()).Should().BeTrue();

        var published = harness.Published.Select<TicketCreatedFromAlertResponse>().ToList();
        published.Should().ContainSingle();
        published[0].Context.Message.IsReused.Should().BeTrue(
            "a battery alert remains asset-level even when its V2 payload includes SiteId");
        published[0].Context.Message.TicketId.Should().Be(existing.Id);
        mediator.Verify(m => m.Send(It.IsAny<TicketAutoCreateFromAlertCommand>(),
            It.IsAny<CancellationToken>()), Times.Never);

        await harness.Stop();
    }

    [Fact]
    public async Task NoExistingTicket_ShouldCallMediator_AndPublishNotReused()
    {
        var alertId = Guid.NewGuid();
        var assetId = Guid.NewGuid();
        var (mediator, uow, _) = BuildMocks(new List<Ticket>());
        var newTicketId = Guid.NewGuid();

        mediator.Setup(m => m.Send(It.IsAny<TicketAutoCreateFromAlertCommand>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TicketActionResponse
            {
                IsSuccess = true,
                StatusCode = 201,
                Data = new TicketActionDTO
                {
                    Id = newTicketId.ToString(),
                    TicketId = newTicketId.ToString(),
                    Code = "TCK-NEW"
                }
            });

        var provider = new ServiceCollection()
            .AddMassTransitTestHarness(x =>
            {
                x.AddConsumer<CreateTicketFromAlertConsumer>();
                // Sửa flaky 2026-07-31 — inactivity timeout mặc định của MassTransit v8 chỉ 1 giây;
                // `Consumed.Any<T>()` trả `false` cả khi hết giờ lẫn khi hỏng thật. Chạy cả solution
                // song song thì trượt ngưỡng. Khuôn: NotificationService/Helpers/ConsumerTestHarness.cs.
                x.SetTestTimeouts(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(15));
            })
            .AddSingleton(mediator.Object)
            .AddSingleton(uow.Object)
            .AddSingleton(NullLogger<CreateTicketFromAlertConsumer>.Instance)
            .BuildServiceProvider(true);

        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        await harness.Bus.Publish(MakeMsg(alertId, assetId));
        (await harness.Consumed.Any<CreateTicketFromAlertCommand>()).Should().BeTrue();

        var published = harness.Published.Select<TicketCreatedFromAlertResponse>().ToList();
        published.Should().ContainSingle();
        published[0].Context.Message.IsReused.Should().BeFalse();
        published[0].Context.Message.TicketCode.Should().Be("TCK-NEW");

        await harness.Stop();
    }

    [Fact]
    public async Task MediatorRejects_ShouldPublishRejection()
    {
        var alertId = Guid.NewGuid();
        var assetId = Guid.NewGuid();
        var (mediator, uow, _) = BuildMocks(new List<Ticket>());

        mediator.Setup(m => m.Send(It.IsAny<TicketAutoCreateFromAlertCommand>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TicketActionResponse
            {
                IsSuccess = false,
                StatusCode = 400,
                Message = "Asset not found",
                ListErrors = { new Errors { Field = "BatteryAssetId", Detail = "Asset not found" } }
            });

        var provider = new ServiceCollection()
            .AddMassTransitTestHarness(x =>
            {
                x.AddConsumer<CreateTicketFromAlertConsumer>();
                // Sửa flaky 2026-07-31 — inactivity timeout mặc định của MassTransit v8 chỉ 1 giây;
                // `Consumed.Any<T>()` trả `false` cả khi hết giờ lẫn khi hỏng thật. Chạy cả solution
                // song song thì trượt ngưỡng. Khuôn: NotificationService/Helpers/ConsumerTestHarness.cs.
                x.SetTestTimeouts(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(15));
            })
            .AddSingleton(mediator.Object)
            .AddSingleton(uow.Object)
            .AddSingleton(NullLogger<CreateTicketFromAlertConsumer>.Instance)
            .BuildServiceProvider(true);

        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        await harness.Bus.Publish(MakeMsg(alertId, assetId));
        (await harness.Consumed.Any<CreateTicketFromAlertCommand>()).Should().BeTrue();

        var rejected = harness.Published.Select<TicketCreationFromAlertRejected>().ToList();
        rejected.Should().ContainSingle();
        rejected[0].Context.Message.Reason.Should().Contain("Asset not found");

        await harness.Stop();
    }
}
