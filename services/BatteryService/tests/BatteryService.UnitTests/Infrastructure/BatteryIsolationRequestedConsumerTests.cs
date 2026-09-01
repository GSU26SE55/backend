using BatteryService.Application.CQRS.Command.BatteryAsset;
using BatteryService.Application.CQRS.Handler.BatteryAsset;
using BatteryService.Application.DTOs;
using BatteryService.Application.Interfaces;
using BatteryService.Application.Services;
using BatteryService.Domain.Entities;
using BatteryService.Domain.Enums;
using BatteryService.Infrastructure.Consumers;
using BatteryService.UnitTests.Helpers;
using FluentAssertions;
using MassTransit;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SharedContracts.Common.Responses;
using SharedContracts.Events;

namespace BatteryService.UnitTests.Infrastructure;

/// <summary>
/// Declare Incident → <see cref="BatteryIsolationRequestedEvent"/> → NGẮT XẢ thật sự.
/// Trước khi có consumer này event rơi vào hư không: không service nào consume.
/// </summary>
public class BatteryIsolationRequestedConsumerTests
{
    [Fact]
    public async Task Consume_CutsDischargeOnEveryAttachedAsset()
    {
        var declaredBy = Guid.NewGuid();
        var assetA = Guid.NewGuid();
        var assetB = Guid.NewGuid();
        var mediator = MediatorReturning(Accepted());

        await Consumer(mediator.Object).Consume(Context(new BatteryIsolationRequestedEvent(
            Guid.NewGuid(), Guid.NewGuid(), new[] { assetA, assetB, assetA }, DateTime.UtcNow, declaredBy)));

        foreach (var assetId in new[] { assetA, assetB })
        {
            mediator.Verify(m => m.Send(
                It.Is<SetBmsSwitchCommand>(c => c.BatteryAssetId == assetId
                                                && c.Target == "discharge"
                                                && !c.Enable
                                                && c.IssuedByAccountId == declaredBy),
                It.IsAny<CancellationToken>()), Times.Once);
        }
    }

    [Fact]
    public async Task Consume_ThrowsWhenBridgeIsDown_SoTheMessageIsRetried()
    {
        var mediator = MediatorReturning(new CommonResponse<BmsSwitchCommandAcceptedDto>
        {
            IsSuccess = false,
            StatusCode = 503,
            Message = "The MQTT bridge is unavailable."
        });

        var act = () => Consumer(mediator.Object).Consume(Context(new BatteryIsolationRequestedEvent(
            Guid.NewGuid(), Guid.NewGuid(), new[] { Guid.NewGuid() }, DateTime.UtcNow, Guid.NewGuid())));

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Consume_DoesNotThrowWhenACommandIsAlreadyPending()
    {
        var mediator = MediatorReturning(new CommonResponse<BmsSwitchCommandAcceptedDto>
        {
            IsSuccess = false,
            StatusCode = 409,
            Message = "A previous command for this switch is still awaiting a response."
        });

        var act = () => Consumer(mediator.Object).Consume(Context(new BatteryIsolationRequestedEvent(
            Guid.NewGuid(), Guid.NewGuid(), new[] { Guid.NewGuid() }, DateTime.UtcNow, Guid.NewGuid())));

        await act.Should().NotThrowAsync();
    }

    /// <summary>
    /// Consumer chạy ngoài HTTP request nên không có current user; lệnh do hệ thống phát phải đi
    /// lọt và audit đúng người đã bấm Declare Incident.
    /// </summary>
    [Fact]
    public async Task SystemIssuedCommand_BypassesCurrentUser_AndAuditsTheDeclaringAccount()
    {
        var declaredBy = Guid.NewGuid();
        var siteId = Guid.NewGuid();
        var asset = new BatteryAsset
        {
            Id = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            SiteId = siteId,
            SerialNumber = "BAT-001"
        };
        var device = new IotDevice
        {
            Id = Guid.NewGuid(),
            SiteId = siteId,
            DeviceCode = "GW-001",
            DisplayName = "Gateway",
            Status = IotDeviceStatusEnum.Active
        };
        var builder = new MockUnitOfWorkBuilder().WithBatteryAssets(asset).WithIotDevices(device);
        var mqtt = new Mock<IMqttBridgePublisher>();
        var handler = new SetBmsSwitchCommandHandler(
            builder.Build(),
            new TestBatteryCurrentUserService(null),
            mqtt.Object,
            NullLogger<SetBmsSwitchCommandHandler>.Instance);

        var result = await handler.Handle(new SetBmsSwitchCommand
        {
            BatteryAssetId = asset.Id,
            Target = "discharge",
            Enable = false,
            IssuedByAccountId = declaredBy
        }, CancellationToken.None);

        result.StatusCode.Should().Be(202);
        builder.IotDeviceCommands.Verify(repo => repo.AddAsync(It.Is<IotDeviceCommand>(command =>
            command.BatteryAssetId == asset.Id
            && command.IssuedByAccountId == declaredBy)), Times.Once);
        mqtt.Verify(publisher => publisher.PublishCommandAsync(
            device.DeviceCode,
            It.Is<string>(payload => payload.Contains("\"target\":\"discharge\"")
                                     && payload.Contains("\"enable\":false")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Consume_EnvironmentalTicketWithNoBattery_CutsEveryActiveBatteryOnTheSite()
    {
        var incidentId = Guid.NewGuid();
        var siteId = Guid.NewGuid();
        var onSiteA = new BatteryAsset { Id = Guid.NewGuid(), SiteId = siteId, Status = BatteryStatusEnum.Active };
        var onSiteB = new BatteryAsset { Id = Guid.NewGuid(), SiteId = siteId, Status = BatteryStatusEnum.Active };
        // Không được đụng tới: pin đã ngừng hoạt động và pin của site khác.
        var decommissioned = new BatteryAsset { Id = Guid.NewGuid(), SiteId = siteId, Status = BatteryStatusEnum.Decommissioned };
        var otherSite = new BatteryAsset { Id = Guid.NewGuid(), SiteId = Guid.NewGuid(), Status = BatteryStatusEnum.Active };

        var unitOfWork = new MockUnitOfWorkBuilder()
            .WithEnvironmentalIncidents(new EnvironmentalIncident { Id = incidentId, SiteId = siteId })
            .WithBatteryAssets(onSiteA, onSiteB, decommissioned, otherSite)
            .Build();
        var mediator = MediatorReturning(Accepted());

        await Consumer(mediator.Object, unitOfWork).Consume(Context(new BatteryIsolationRequestedEvent(
            incidentId, Guid.NewGuid(), Array.Empty<Guid>(), DateTime.UtcNow, Guid.NewGuid())));

        foreach (var asset in new[] { onSiteA, onSiteB })
        {
            mediator.Verify(m => m.Send(
                It.Is<SetBmsSwitchCommand>(c => c.BatteryAssetId == asset.Id && c.Target == "discharge" && !c.Enable),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        foreach (var asset in new[] { decommissioned, otherSite })
        {
            mediator.Verify(m => m.Send(
                It.Is<SetBmsSwitchCommand>(c => c.BatteryAssetId == asset.Id),
                It.IsAny<CancellationToken>()), Times.Never);
        }
    }

    [Fact]
    public async Task Consume_NoBatteryAndNoIncidentRecord_SendsNothing()
    {
        var mediator = MediatorReturning(Accepted());
        var unitOfWork = new MockUnitOfWorkBuilder().Build();

        await Consumer(mediator.Object, unitOfWork).Consume(Context(new BatteryIsolationRequestedEvent(
            Guid.NewGuid(), Guid.NewGuid(), Array.Empty<Guid>(), DateTime.UtcNow, Guid.NewGuid())));

        mediator.Verify(m => m.Send(It.IsAny<SetBmsSwitchCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <param name="unitOfWork">
    /// Chỉ dùng khi sự kiện không mang pin nào — consumer khi đó suy pin ra từ site của incident.
    /// Mặc định là builder rỗng: mọi test truyền sẵn BatteryAssetIds không chạm tới nhánh này.
    /// </param>
    private static BatteryIsolationRequestedConsumer Consumer(
        IMediator mediator,
        IBatteryUnitOfWork? unitOfWork = null) =>
        new(mediator,
            unitOfWork ?? new MockUnitOfWorkBuilder().Build(),
            NullLogger<BatteryIsolationRequestedConsumer>.Instance);

    private static Mock<IMediator> MediatorReturning(CommonResponse<BmsSwitchCommandAcceptedDto> response)
    {
        var mediator = new Mock<IMediator>();
        mediator.Setup(m => m.Send(It.IsAny<SetBmsSwitchCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);
        return mediator;
    }

    private static ConsumeContext<BatteryIsolationRequestedEvent> Context(BatteryIsolationRequestedEvent message)
    {
        var context = new Mock<ConsumeContext<BatteryIsolationRequestedEvent>>();
        context.SetupGet(c => c.Message).Returns(message);
        context.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);
        return context.Object;
    }

    private static CommonResponse<BmsSwitchCommandAcceptedDto> Accepted() => new()
    {
        IsSuccess = true,
        StatusCode = 202,
        Data = new BmsSwitchCommandAcceptedDto
        {
            CmdId = Guid.NewGuid().ToString("N"),
            Target = "discharge",
            Enable = false,
            Topic = "solar/gw-001/cmd"
        }
    };
}
