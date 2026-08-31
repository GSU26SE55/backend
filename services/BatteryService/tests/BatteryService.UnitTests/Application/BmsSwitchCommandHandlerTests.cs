using BatteryService.Application.CQRS.Command.BatteryAsset;
using BatteryService.Application.CQRS.Handler.BatteryAsset;
using BatteryService.Application.CQRS.Query.BatteryAsset;
using BatteryService.Application.Services;
using BatteryService.Domain.Entities;
using BatteryService.Domain.Enums;
using BatteryService.UnitTests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace BatteryService.UnitTests.Application;

public class BmsSwitchCommandHandlerTests
{
    [Fact]
    public async Task SwitchCommand_IsAcceptedAndAudited()
    {
        var customerId = Guid.NewGuid();
        var siteId = Guid.NewGuid();
        var asset = Asset(customerId, siteId);
        var device = Device(siteId);
        var builder = new MockUnitOfWorkBuilder()
            .WithBatteryAssets(asset)
            .WithIotDevices(device);
        var mqtt = new Mock<IMqttBridgePublisher>();

        var result = await Handler(builder, TestBatteryCurrentUserService.Customer(customerId), mqtt.Object)
            .Handle(new SetBmsSwitchCommand
            {
                BatteryAssetId = asset.Id,
                Target = "charge",
                Enable = false
            }, CancellationToken.None);

        result.StatusCode.Should().Be(202);
        result.IsSuccess.Should().BeTrue();
        builder.IotDeviceCommands.Verify(repo => repo.AddAsync(It.Is<IotDeviceCommand>(command =>
            command.BatteryAssetId == asset.Id
            && command.IssuedByAccountId == customerId
            && command.Status == IotDeviceCommandStatusEnum.Pending)), Times.Once);
        mqtt.Verify(publisher => publisher.PublishCommandAsync(
            device.DeviceCode,
            It.Is<string>(payload => payload.Contains("set_bms_switch")
                                     && payload.Contains(asset.SerialNumber)
                                     && payload.Contains("\"target\":\"charge\"")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("charge")]
    [InlineData("discharge")]
    [InlineData("all")]
    public async Task SupportedTargets_ArePublishedToDevice(string target)
    {
        var customerId = Guid.NewGuid();
        var siteId = Guid.NewGuid();
        var asset = Asset(customerId, siteId);
        var device = Device(siteId);
        var builder = new MockUnitOfWorkBuilder()
            .WithBatteryAssets(asset)
            .WithIotDevices(device);
        var mqtt = new Mock<IMqttBridgePublisher>();

        var result = await Handler(builder, TestBatteryCurrentUserService.Customer(customerId), mqtt.Object)
            .Handle(new SetBmsSwitchCommand
            {
                BatteryAssetId = asset.Id,
                Target = target,
                Enable = true
            }, CancellationToken.None);

        result.StatusCode.Should().Be(202);
        mqtt.Verify(publisher => publisher.PublishCommandAsync(
            device.DeviceCode,
            It.Is<string>(payload => payload.Contains($"\"target\":\"{target}\"")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // "all" đã rời danh sách này khi firmware nhận mapping all=3 — xem
    // AllTarget_IsAcceptedAndSentToTheDevice ngay bên dưới.
    [Theory]
    [InlineData("both")]
    [InlineData("")]
    [InlineData("CHARGE_MOSFET")]
    public async Task UnsupportedTarget_IsRejectedBeforeReachingDevice(string target)
    {
        var customerId = Guid.NewGuid();
        var siteId = Guid.NewGuid();
        var asset = Asset(customerId, siteId);
        var builder = new MockUnitOfWorkBuilder()
            .WithBatteryAssets(asset)
            .WithIotDevices(Device(siteId));
        var mqtt = new Mock<IMqttBridgePublisher>();

        var result = await Handler(builder, TestBatteryCurrentUserService.Customer(customerId), mqtt.Object)
            .Handle(new SetBmsSwitchCommand
            {
                BatteryAssetId = asset.Id,
                Target = target,
                Enable = true
            }, CancellationToken.None);

        result.StatusCode.Should().Be(400);
        mqtt.Verify(publisher => publisher.PublishCommandAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AllTarget_IsAcceptedAndSentToTheDevice()
    {
        var customerId = Guid.NewGuid();
        var siteId = Guid.NewGuid();
        var asset = Asset(customerId, siteId);
        var device = Device(siteId);
        var builder = new MockUnitOfWorkBuilder()
            .WithBatteryAssets(asset)
            .WithIotDevices(device);
        var mqtt = new Mock<IMqttBridgePublisher>();

        var result = await Handler(builder, TestBatteryCurrentUserService.Customer(customerId), mqtt.Object)
            .Handle(new SetBmsSwitchCommand
            {
                BatteryAssetId = asset.Id,
                Target = "all",
                Enable = false
            }, CancellationToken.None);

        result.StatusCode.Should().Be(202);
        // Gửi nguyên "all" xuống thiết bị, KHÔNG tách thành hai lệnh: firmware map all=3 và ghi
        // cả hai MOSFET trong một lượt, nên tách ra sẽ thành hai lần áp dụng có thể lệch nhau.
        mqtt.Verify(publisher => publisher.PublishCommandAsync(
            device.DeviceCode,
            It.Is<string>(payload => payload.Contains("\"target\":\"all\"")
                                     && payload.Contains("\"enable\":false")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    // "all" phủ cả hai MOSFET nên phải xung đột với lệnh charge/discharge đang chờ ack, theo cả
    // hai chiều. Thiếu vế này thì hai lệnh trái chiều cùng xuống thiết bị và trạng thái cuối phụ
    // thuộc cái nào tới trước.
    [InlineData("all", "charge")]
    [InlineData("all", "discharge")]
    [InlineData("charge", "all")]
    [InlineData("discharge", "all")]
    [InlineData("all", "all")]
    public async Task AllTarget_ConflictsWithAnyPendingCommandOnEitherMosfet(
        string pendingTarget,
        string requestedTarget)
    {
        var customerId = Guid.NewGuid();
        var siteId = Guid.NewGuid();
        var asset = Asset(customerId, siteId);
        var device = Device(siteId);
        var pending = new IotDeviceCommand
        {
            Id = Guid.NewGuid(),
            IotDeviceId = device.Id,
            BatteryAssetId = asset.Id,
            CmdId = "pending-all",
            Type = "set_bms_switch",
            ParamsJson = $"{{\"serial\":\"BAT-001\",\"target\":\"{pendingTarget}\",\"enable\":true}}",
            Status = IotDeviceCommandStatusEnum.Pending
        };
        var builder = new MockUnitOfWorkBuilder()
            .WithBatteryAssets(asset)
            .WithIotDevices(device)
            .WithIotDeviceCommands(pending);
        var mqtt = new Mock<IMqttBridgePublisher>();

        var result = await Handler(builder, TestBatteryCurrentUserService.Customer(customerId), mqtt.Object)
            .Handle(new SetBmsSwitchCommand
            {
                BatteryAssetId = asset.Id,
                Target = requestedTarget,
                Enable = false
            }, CancellationToken.None);

        result.StatusCode.Should().Be(409);
        mqtt.Verify(publisher => publisher.PublishCommandAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CustomerNonOwner_IsHiddenAsNotFound()
    {
        var ownerId = Guid.NewGuid();
        var siteId = Guid.NewGuid();
        var asset = Asset(ownerId, siteId);
        var builder = new MockUnitOfWorkBuilder()
            .WithBatteryAssets(asset)
            .WithIotDevices(Device(siteId));

        var result = await Handler(builder, TestBatteryCurrentUserService.Customer(Guid.NewGuid()),
                Mock.Of<IMqttBridgePublisher>())
            .Handle(new SetBmsSwitchCommand
            {
                BatteryAssetId = asset.Id,
                Target = "charge",
                Enable = false
            }, CancellationToken.None);

        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task MultipleActiveGateways_ReturnsConflict()
    {
        var customerId = Guid.NewGuid();
        var siteId = Guid.NewGuid();
        var asset = Asset(customerId, siteId);
        var builder = new MockUnitOfWorkBuilder()
            .WithBatteryAssets(asset)
            .WithIotDevices(Device(siteId), Device(siteId));

        var result = await Handler(builder, TestBatteryCurrentUserService.Customer(customerId),
                Mock.Of<IMqttBridgePublisher>())
            .Handle(new SetBmsSwitchCommand
            {
                BatteryAssetId = asset.Id,
                Target = "discharge",
                Enable = true
            }, CancellationToken.None);

        result.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task PendingCommand_ReturnsConflict()
    {
        var customerId = Guid.NewGuid();
        var siteId = Guid.NewGuid();
        var asset = Asset(customerId, siteId);
        var device = Device(siteId);
        var pending = new IotDeviceCommand
        {
            Id = Guid.NewGuid(),
            IotDeviceId = device.Id,
            BatteryAssetId = asset.Id,
            CmdId = "pending-1",
            Type = "set_bms_switch",
            ParamsJson = "{\"serial\":\"BAT-001\",\"target\":\"charge\",\"enable\":true}",
            Status = IotDeviceCommandStatusEnum.Pending
        };
        var builder = new MockUnitOfWorkBuilder()
            .WithBatteryAssets(asset)
            .WithIotDevices(device)
            .WithIotDeviceCommands(pending);

        var result = await Handler(builder, TestBatteryCurrentUserService.Customer(customerId),
                Mock.Of<IMqttBridgePublisher>())
            .Handle(new SetBmsSwitchCommand
            {
                BatteryAssetId = asset.Id,
                Target = "charge",
                Enable = false
            }, CancellationToken.None);

        result.StatusCode.Should().Be(409);
    }

    // Ma trận chống trùng lệnh: `all` chạm CẢ HAI MOSFET nên giao với mọi target, theo cả hai
    // chiều. Trước khi có `TargetsOverlap`, chỗ này so chuỗi thuần — một lệnh `all` đang chờ ack
    // không chặn được lệnh `charge` mới, hai lệnh trái chiều cùng xuống thiết bị và MOSFET nằm ở
    // trạng thái nào là do thứ tự ack quyết định.
    [Theory]
    [InlineData("all", "charge")]
    [InlineData("all", "discharge")]
    [InlineData("charge", "all")]
    [InlineData("discharge", "all")]
    [InlineData("all", "all")]
    public async Task PendingCommandTouchingSameMosfet_IsRejectedAsConflict(
        string pendingTarget, string requestedTarget)
    {
        var customerId = Guid.NewGuid();
        var siteId = Guid.NewGuid();
        var asset = Asset(customerId, siteId);
        var device = Device(siteId);
        var builder = new MockUnitOfWorkBuilder()
            .WithBatteryAssets(asset)
            .WithIotDevices(device)
            .WithIotDeviceCommands(PendingSwitch(device, asset, pendingTarget, enable: true));
        var mqtt = new Mock<IMqttBridgePublisher>();

        var result = await Handler(builder, TestBatteryCurrentUserService.Customer(customerId), mqtt.Object)
            .Handle(new SetBmsSwitchCommand
            {
                BatteryAssetId = asset.Id,
                Target = requestedTarget,
                Enable = false
            }, CancellationToken.None);

        result.StatusCode.Should().Be(409);
        mqtt.Verify(publisher => publisher.PublishCommandAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // Hai MOSFET khác nhau thì không đụng nhau — vẫn phải cho qua, nếu không thao tác một bên
    // sẽ bị khoá oan tới khi bên kia timeout 60s.
    [Fact]
    public async Task PendingCommandOnOtherMosfet_DoesNotBlock()
    {
        var customerId = Guid.NewGuid();
        var siteId = Guid.NewGuid();
        var asset = Asset(customerId, siteId);
        var device = Device(siteId);
        var builder = new MockUnitOfWorkBuilder()
            .WithBatteryAssets(asset)
            .WithIotDevices(device)
            .WithIotDeviceCommands(PendingSwitch(device, asset, "charge", enable: true));

        var result = await Handler(builder, TestBatteryCurrentUserService.Customer(customerId),
                Mock.Of<IMqttBridgePublisher>())
            .Handle(new SetBmsSwitchCommand
            {
                BatteryAssetId = asset.Id,
                Target = "discharge",
                Enable = false
            }, CancellationToken.None);

        result.StatusCode.Should().Be(202);
    }

    // Ngoại lệ an toàn: lệnh ngắt xả TỰ ĐỘNG do sự cố chen ngang được cả một lệnh `all` đang chờ.
    // An toàn ưu tiên hơn thứ tự — nếu không, một lệnh `all` mất ack sẽ khoá đường cắt xả khẩn
    // cấp suốt cửa sổ timeout.
    [Fact]
    public async Task AutomaticDischargeCut_SupersedesPendingAllCommand()
    {
        var customerId = Guid.NewGuid();
        var siteId = Guid.NewGuid();
        var asset = Asset(customerId, siteId);
        var device = Device(siteId);
        var builder = new MockUnitOfWorkBuilder()
            .WithBatteryAssets(asset)
            .WithIotDevices(device)
            .WithIotDeviceCommands(PendingSwitch(device, asset, "all", enable: true));
        var publisher = new Mock<IMqttBridgePublisher>();

        var result = await Handler(builder, new TestBatteryCurrentUserService(null), publisher.Object)
            .Handle(new SetBmsSwitchCommand
            {
                BatteryAssetId = asset.Id,
                Target = "discharge",
                Enable = false,
                IssuedByAccountId = Guid.Empty
            }, CancellationToken.None);

        result.StatusCode.Should().Be(202);
    }

    private static IotDeviceCommand PendingSwitch(
        IotDevice device, BatteryAsset asset, string target, bool enable) => new()
        {
            Id = Guid.NewGuid(),
            IotDeviceId = device.Id,
            BatteryAssetId = asset.Id,
            CmdId = $"pending-{target}",
            Type = "set_bms_switch",
            ParamsJson = $"{{\"serial\":\"BAT-001\",\"target\":\"{target}\",\"enable\":{(enable ? "true" : "false")}}}",
            Status = IotDeviceCommandStatusEnum.Pending
        };

    [Fact]
    public async Task AutomaticDischargeCut_SupersedesPendingEnableCommand()
    {
        var customerId = Guid.NewGuid();
        var siteId = Guid.NewGuid();
        var asset = Asset(customerId, siteId);
        var device = Device(siteId);
        var pendingEnable = new IotDeviceCommand
        {
            Id = Guid.NewGuid(),
            IotDeviceId = device.Id,
            BatteryAssetId = asset.Id,
            CmdId = "pending-enable",
            Type = "set_bms_switch",
            ParamsJson = "{\"serial\":\"BAT-001\",\"target\":\"discharge\",\"enable\":true}",
            Status = IotDeviceCommandStatusEnum.Pending
        };
        var builder = new MockUnitOfWorkBuilder()
            .WithBatteryAssets(asset)
            .WithIotDevices(device)
            .WithIotDeviceCommands(pendingEnable);
        var publisher = new Mock<IMqttBridgePublisher>();

        var result = await Handler(builder, new TestBatteryCurrentUserService(null), publisher.Object)
            .Handle(new SetBmsSwitchCommand
            {
                BatteryAssetId = asset.Id,
                Target = "discharge",
                Enable = false,
                IssuedByAccountId = Guid.Empty
            }, CancellationToken.None);

        result.StatusCode.Should().Be(202);
        builder.IotDeviceCommands.Verify(repository => repository.AddAsync(
            It.Is<IotDeviceCommand>(command =>
                command.BatteryAssetId == asset.Id
                && command.ParamsJson.Contains("\"target\":\"discharge\"")
                && command.ParamsJson.Contains("\"enable\":false"))), Times.Once);
        publisher.Verify(x => x.PublishCommandAsync(
            device.DeviceCode,
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    private static SetBmsSwitchCommandHandler Handler(
        MockUnitOfWorkBuilder builder,
        TestBatteryCurrentUserService currentUser,
        IMqttBridgePublisher publisher) => new(
            builder.Build(),
            currentUser,
            publisher,
            NullLogger<SetBmsSwitchCommandHandler>.Instance);

    private static BatteryAsset Asset(Guid customerId, Guid siteId) => new()
    {
        Id = Guid.NewGuid(),
        CustomerId = customerId,
        SiteId = siteId,
        SerialNumber = "BAT-001"
    };

    private static IotDevice Device(Guid siteId) => new()
    {
        Id = Guid.NewGuid(),
        SiteId = siteId,
        DeviceCode = $"GW-{Guid.NewGuid():N}",
        DisplayName = "Gateway",
        Status = IotDeviceStatusEnum.Active
    };
}

public class BmsSwitchStateQueryHandlerTests
{
    [Fact]
    public async Task ReturnsLastReadBackStateAndPendingCommand()
    {
        var customerId = Guid.NewGuid();
        var siteId = Guid.NewGuid();
        var asset = new BatteryAsset
        {
            Id = Guid.NewGuid(),
            CustomerId = customerId,
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
        var verified = new IotDeviceCommand
        {
            Id = Guid.NewGuid(),
            IotDeviceId = device.Id,
            BatteryAssetId = asset.Id,
            CmdId = "verified",
            Type = "set_bms_switch",
            ParamsJson = "{\"target\":\"charge\",\"enable\":true}",
            ResultJson = "{\"serial\":\"BAT-001\",\"chargeEnabled\":true,\"dischargeEnabled\":false}",
            Status = IotDeviceCommandStatusEnum.Ok,
            CreatedAt = DateTime.UtcNow.AddSeconds(-10),
            AckedAt = DateTime.UtcNow.AddSeconds(-9)
        };
        var pending = new IotDeviceCommand
        {
            Id = Guid.NewGuid(),
            IotDeviceId = device.Id,
            BatteryAssetId = asset.Id,
            CmdId = "pending",
            Type = "set_bms_switch",
            ParamsJson = "{\"target\":\"discharge\",\"enable\":true}",
            Status = IotDeviceCommandStatusEnum.Pending,
            CreatedAt = DateTime.UtcNow
        };
        var builder = new MockUnitOfWorkBuilder()
            .WithBatteryAssets(asset)
            .WithIotDevices(device)
            .WithIotDeviceCommands(verified, pending);
        var handler = new GetBmsSwitchStateQueryHandler(
            builder.Build(), TestBatteryCurrentUserService.Customer(customerId));

        var result = await handler.Handle(
            new GetBmsSwitchStateQuery { BatteryAssetId = asset.Id }, CancellationToken.None);

        result.StatusCode.Should().Be(200);
        result.Data!.ChargeEnabled.Should().BeTrue();
        result.Data.DischargeEnabled.Should().BeFalse();
        result.Data.PendingCommand!.CmdId.Should().Be("pending");
        result.Data.PendingCommand.Target.Should().Be("discharge");
    }

    // Áp dụng được MỘT MOSFET: firmware trả `failed` kèm state lệch thật. State đó phải thắng
    // state của lệnh `Ok` cũ hơn.
    //
    // Trước fix, chỗ đọc state chỉ nhận lệnh `Ok`, nên UI vẫn hiện "cả hai đã tắt" của lệnh cũ
    // trong khi discharge MOSFET đang bật và pin vẫn cấp điện cho tải. Đây là điều khiển điện
    // lực — hiển thị sai kiểu này nguy hiểm hơn là không hiển thị gì.
    [Fact]
    public async Task PartiallyAppliedCommand_ReportsTheStateTheDeviceActuallyReadBack()
    {
        var customerId = Guid.NewGuid();
        var siteId = Guid.NewGuid();
        var asset = StateAsset(customerId, siteId);
        var device = StateDevice(siteId);
        var olderOk = SwitchCommand(device, asset, "older-ok",
            "{\"chargeEnabled\":false,\"dischargeEnabled\":false}",
            IotDeviceCommandStatusEnum.Ok, secondsAgo: 60);
        var newerPartial = SwitchCommand(device, asset, "newer-failed",
            "{\"chargeEnabled\":false,\"dischargeEnabled\":true}",
            IotDeviceCommandStatusEnum.Failed, secondsAgo: 5);
        var builder = new MockUnitOfWorkBuilder()
            .WithBatteryAssets(asset)
            .WithIotDevices(device)
            .WithIotDeviceCommands(olderOk, newerPartial);

        var result = await new GetBmsSwitchStateQueryHandler(
                builder.Build(), TestBatteryCurrentUserService.Customer(customerId))
            .Handle(new GetBmsSwitchStateQuery { BatteryAssetId = asset.Id }, CancellationToken.None);

        result.StatusCode.Should().Be(200);
        result.Data!.ChargeEnabled.Should().BeFalse();
        result.Data.DischargeEnabled.Should().BeTrue("state của ack failed là sự thật hiện tại");
    }

    // Lý do thô của firmware phải sống sót tới client. `Error` vẫn là câu chuẩn hoá để hiển thị;
    // `DeviceReason` giữ nguyên văn để client phân biệt được nguyên nhân — mobile dùng nó để ẩn
    // control BMS trên thiết bị không hỗ trợ lệnh.
    [Fact]
    public async Task RejectedCommand_KeepsTheRawDeviceReasonBesideTheDisplayMessage()
    {
        var customerId = Guid.NewGuid();
        var siteId = Guid.NewGuid();
        var asset = StateAsset(customerId, siteId);
        var device = StateDevice(siteId);
        var rejected = SwitchCommand(device, asset, "rejected", resultJson: null,
            IotDeviceCommandStatusEnum.Rejected, secondsAgo: 5);
        rejected.AckError = "unsupported target";
        var builder = new MockUnitOfWorkBuilder()
            .WithBatteryAssets(asset)
            .WithIotDevices(device)
            .WithIotDeviceCommands(rejected);

        var result = await new GetBmsSwitchStateQueryHandler(
                builder.Build(), TestBatteryCurrentUserService.Customer(customerId))
            .Handle(new GetBmsSwitchStateQuery { BatteryAssetId = asset.Id }, CancellationToken.None);

        result.Data!.LastCommand!.Error.Should().Be("The BMS rejected the control command.");
        result.Data.LastCommand.DeviceReason.Should().Be("unsupported target");
    }

    private static BatteryAsset StateAsset(Guid customerId, Guid siteId) => new()
    {
        Id = Guid.NewGuid(),
        CustomerId = customerId,
        SiteId = siteId,
        SerialNumber = "BAT-001"
    };

    private static IotDevice StateDevice(Guid siteId) => new()
    {
        Id = Guid.NewGuid(),
        SiteId = siteId,
        DeviceCode = "GW-001",
        DisplayName = "Gateway",
        Status = IotDeviceStatusEnum.Active
    };

    private static IotDeviceCommand SwitchCommand(
        IotDevice device,
        BatteryAsset asset,
        string cmdId,
        string? resultJson,
        IotDeviceCommandStatusEnum status,
        int secondsAgo) => new()
        {
            Id = Guid.NewGuid(),
            IotDeviceId = device.Id,
            BatteryAssetId = asset.Id,
            CmdId = cmdId,
            Type = "set_bms_switch",
            ParamsJson = "{\"target\":\"all\",\"enable\":false}",
            ResultJson = resultJson,
            Status = status,
            CreatedAt = DateTime.UtcNow.AddSeconds(-secondsAgo),
            AckedAt = DateTime.UtcNow.AddSeconds(-secondsAgo + 1)
        };
}
