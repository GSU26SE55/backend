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

    private static CreateTicketFromAlertCommand MakeMsg(Guid alertId, Guid assetId)
        => new(
            CorrelationId: alertId, AlertId: alertId, BatteryAssetId: assetId,
            CustomerId: Guid.NewGuid(), AssetSerialNumber: "BMS-1",
            AnomalyType: 1, Severity: 3,
            ThresholdValue: 60m, ActualValue: 75m, Unit: "C",
            DetectedAt: DateTime.UtcNow,
            AnomalyCategory: "Overheat", Title: "Overheat", Description: "Detected");

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
            .AddMassTransitTestHarness(x => x.AddConsumer<CreateTicketFromAlertConsumer>())
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
            .AddMassTransitTestHarness(x => x.AddConsumer<CreateTicketFromAlertConsumer>())
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
            .AddMassTransitTestHarness(x => x.AddConsumer<CreateTicketFromAlertConsumer>())
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
            .AddMassTransitTestHarness(x => x.AddConsumer<CreateTicketFromAlertConsumer>())
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
