using BatteryService.Application.CQRS.Command.BatteryAsset;
using BatteryService.Application.CQRS.Handler.BatteryAsset;
using BatteryService.Application.CQRS.Query.BatteryAsset;
using BatteryService.Domain.Entities;
using BatteryService.Domain.Enums;
using BatteryService.UnitTests.Helpers;
using SharedInfrastructure.Services;

namespace BatteryService.UnitTests.Application;

public class BatteryAssetFullHandlerTests
{
    private static readonly Guid CustomerId = Guid.NewGuid();
    private static readonly Guid BatteryTypeId = Guid.NewGuid();

    private static CustomerAccount Customer(Guid? id = null, bool active = true, bool deleted = false) => new()
    {
        Id = id ?? CustomerId,
        Email = "x@y.com",
        FullName = "x",
        Role = "Customer",
        IsActive = active,
        IsDeleted = deleted,
        LastSyncedAtUtc = DateTime.UtcNow,
        CreatedAt = DateTime.UtcNow
    };

    private static BatteryType MakeType(Guid? id = null) => new()
    {
        Id = id ?? BatteryTypeId,
        Name = "T",
        NominalCapacityAh = 10,
        NominalVoltage = 12,
        Chemistry = BatteryChemistryEnum.LiFePO4,
        MaxCycleCount = 2000,
        CreatedAt = DateTime.UtcNow
    };

    private static Site MakeSite(Guid? customerId = null) => new()
    {
        Id = Guid.NewGuid(),
        Name = "S1",
        CustomerId = customerId ?? CustomerId,
        InstallDate = DateTime.UtcNow,
        Status = SiteStatusEnum.Active,
        CreatedAt = DateTime.UtcNow
    };

    private static BatteryAsset MakeAsset(Guid? id = null, bool deleted = false, Guid? customerId = null, Guid? siteId = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        SerialNumber = "ABC-123",
        BatteryTypeId = BatteryTypeId,
        CustomerId = customerId ?? CustomerId,
        InstallDate = DateTime.UtcNow.AddDays(-10),
        SiteId = siteId,
        Status = BatteryStatusEnum.Active,
        IsDeleted = deleted,
        CreatedAt = DateTime.UtcNow
    };

    private static CreateBatteryAssetCommand BuildCreateCmd() => new()
    {
        SerialNumber = "ABC-999",
        BatteryTypeId = BatteryTypeId,
        CustomerId = CustomerId,
        InstallDate = DateTime.UtcNow.AddDays(-10),
        WarrantyEndDate = DateTime.UtcNow.AddYears(1)
    };

    // ---- Create ----
    [Fact]
    public async Task Create_CustomerMissing_Returns404()
    {
        var b = new MockUnitOfWorkBuilder().WithBatteryTypes(MakeType());
        var handler = new CreateBatteryAssetCommandHandler(b.Build(), NoOpOutbox.Instance, Moq.Mock.Of<MediatR.IPublisher>());
        var r = await handler.Handle(BuildCreateCmd(), CancellationToken.None);
        r.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Create_DuplicateSerial_Returns409()
    {
        var b = new MockUnitOfWorkBuilder()
            .WithCustomerAccounts(Customer())
            .WithBatteryTypes(MakeType())
            .WithBatteryAssets(MakeAsset());
        var cmd = BuildCreateCmd();
        cmd.SerialNumber = "ABC-123";
        var r = await new CreateBatteryAssetCommandHandler(b.Build(), NoOpOutbox.Instance, Moq.Mock.Of<MediatR.IPublisher>()).Handle(cmd, CancellationToken.None);
        r.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Create_BatteryTypeMissing_Returns404()
    {
        var b = new MockUnitOfWorkBuilder().WithCustomerAccounts(Customer());
        var r = await new CreateBatteryAssetCommandHandler(b.Build(), NoOpOutbox.Instance, Moq.Mock.Of<MediatR.IPublisher>()).Handle(BuildCreateCmd(), CancellationToken.None);
        r.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Create_SiteMissing_Returns404()
    {
        var cmd = BuildCreateCmd();
        cmd.SiteId = Guid.NewGuid();
        var b = new MockUnitOfWorkBuilder()
            .WithCustomerAccounts(Customer())
            .WithBatteryTypes(MakeType());
        var r = await new CreateBatteryAssetCommandHandler(b.Build(), NoOpOutbox.Instance, Moq.Mock.Of<MediatR.IPublisher>()).Handle(cmd, CancellationToken.None);
        r.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Create_SiteCustomerMismatch_Returns409()
    {
        var otherCustomer = Guid.NewGuid();
        var site = MakeSite(customerId: otherCustomer);
        var cmd = BuildCreateCmd();
        cmd.SiteId = site.Id;
        var b = new MockUnitOfWorkBuilder()
            .WithCustomerAccounts(Customer())
            .WithBatteryTypes(MakeType())
            .WithSites(site);
        var r = await new CreateBatteryAssetCommandHandler(b.Build(), NoOpOutbox.Instance, Moq.Mock.Of<MediatR.IPublisher>()).Handle(cmd, CancellationToken.None);
        r.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Create_SiteCustomerMismatch_IsReportedBeforeMissingCustomerMirror()
    {
        var site = MakeSite(customerId: Guid.NewGuid());
        var cmd = BuildCreateCmd();
        cmd.SiteId = site.Id;
        var b = new MockUnitOfWorkBuilder()
            .WithBatteryTypes(MakeType())
            .WithSites(site);

        var r = await new CreateBatteryAssetCommandHandler(
            b.Build(), NoOpOutbox.Instance, Moq.Mock.Of<MediatR.IPublisher>())
            .Handle(cmd, CancellationToken.None);

        r.StatusCode.Should().Be(409);
        r.Message.Should().Contain("Site không thuộc khách hàng");
    }

    [Fact]
    public async Task Create_HappyPath_Returns201()
    {
        var site = MakeSite();
        var cmd = BuildCreateCmd();
        cmd.SiteId = site.Id;
        cmd.WarrantyEndDate = DateTime.UtcNow.AddDays(-1); // hết hạn → WarrantyStatus = Expired
        var b = new MockUnitOfWorkBuilder()
            .WithCustomerAccounts(Customer())
            .WithBatteryTypes(MakeType())
            .WithSites(site);
        var r = await new CreateBatteryAssetCommandHandler(b.Build(), NoOpOutbox.Instance, Moq.Mock.Of<MediatR.IPublisher>()).Handle(cmd, CancellationToken.None);
        r.IsSuccess.Should().BeTrue();
        r.StatusCode.Should().Be(201);
        b.BatteryAssets.Verify(x => x.AddAsync(It.IsAny<BatteryAsset>()), Times.Once);
    }

    // ---- Update ----
    [Fact]
    public async Task Update_NotFound_Returns404()
    {
        var b = new MockUnitOfWorkBuilder();
        var cmd = new UpdateBatteryAssetCommand { Id = Guid.NewGuid(), SerialNumber = "ABC-999", BatteryTypeId = BatteryTypeId, CustomerId = CustomerId, InstallDate = DateTime.UtcNow.AddDays(-1) };
        var r = await new UpdateBatteryAssetCommandHandler(b.Build(), Moq.Mock.Of<MediatR.IPublisher>()).Handle(cmd, CancellationToken.None);
        r.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Update_DuplicateSerial_Returns409()
    {
        var asset1 = MakeAsset();
        var asset2 = MakeAsset();
        asset2.SerialNumber = "XYZ-111";
        var b = new MockUnitOfWorkBuilder().WithBatteryTypes(MakeType()).WithBatteryAssets(asset1, asset2);
        var cmd = new UpdateBatteryAssetCommand { Id = asset1.Id, SerialNumber = "XYZ-111", BatteryTypeId = BatteryTypeId, CustomerId = CustomerId, InstallDate = DateTime.UtcNow.AddDays(-1) };
        var r = await new UpdateBatteryAssetCommandHandler(b.Build(), Moq.Mock.Of<MediatR.IPublisher>()).Handle(cmd, CancellationToken.None);
        r.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Update_BatteryTypeMissing_Returns404()
    {
        var asset = MakeAsset();
        var b = new MockUnitOfWorkBuilder().WithBatteryAssets(asset);
        var cmd = new UpdateBatteryAssetCommand { Id = asset.Id, SerialNumber = "Z-1", BatteryTypeId = BatteryTypeId, CustomerId = CustomerId, InstallDate = DateTime.UtcNow.AddDays(-1) };
        var r = await new UpdateBatteryAssetCommandHandler(b.Build(), Moq.Mock.Of<MediatR.IPublisher>()).Handle(cmd, CancellationToken.None);
        r.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Update_HappyPath_Succeeds()
    {
        var site = MakeSite();
        var asset = MakeAsset();
        var b = new MockUnitOfWorkBuilder()
            .WithBatteryTypes(MakeType())
            .WithBatteryAssets(asset)
            .WithSites(site);

        var cmd = new UpdateBatteryAssetCommand
        {
            Id = asset.Id,
            SerialNumber = "ABC-999",
            BatteryTypeId = BatteryTypeId,
            CustomerId = CustomerId,
            InstallDate = DateTime.UtcNow.AddDays(-1),
            SiteId = site.Id,
            Status = BatteryStatusEnum.Active,
            WarrantyStatus = WarrantyStatusEnum.Active
        };
        var r = await new UpdateBatteryAssetCommandHandler(b.Build(), Moq.Mock.Of<MediatR.IPublisher>()).Handle(cmd, CancellationToken.None);
        r.IsSuccess.Should().BeTrue();
        asset.SerialNumber.Should().Be("ABC-999");
    }

    // ---- Delete ----
    [Fact]
    public async Task Delete_NotFound_Returns404()
    {
        var b = new MockUnitOfWorkBuilder();
        var r = await new DeleteBatteryAssetCommandHandler(b.Build(), Moq.Mock.Of<MediatR.IPublisher>()).Handle(new DeleteBatteryAssetCommand { Id = Guid.NewGuid() }, CancellationToken.None);
        r.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Delete_HappyPath_Succeeds()
    {
        var asset = MakeAsset();
        var b = new MockUnitOfWorkBuilder()
            .WithBatteryAssets(asset);
        var r = await new DeleteBatteryAssetCommandHandler(b.Build(), Moq.Mock.Of<MediatR.IPublisher>()).Handle(new DeleteBatteryAssetCommand { Id = asset.Id }, CancellationToken.None);
        r.IsSuccess.Should().BeTrue();
    }

    // ---- Restore ----
    [Fact]
    public async Task Restore_NotFound_Returns404()
    {
        var b = new MockUnitOfWorkBuilder();
        var r = await new RestoreBatteryAssetCommandHandler(b.Build()).Handle(new RestoreBatteryAssetCommand { Id = Guid.NewGuid() }, CancellationToken.None);
        r.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Restore_DuplicateSerial_Returns409()
    {
        var deleted = MakeAsset(deleted: true);
        var alive = MakeAsset();
        alive.SerialNumber = deleted.SerialNumber;
        var b = new MockUnitOfWorkBuilder().WithBatteryAssets(deleted, alive);
        var r = await new RestoreBatteryAssetCommandHandler(b.Build()).Handle(new RestoreBatteryAssetCommand { Id = deleted.Id }, CancellationToken.None);
        r.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Restore_SiteDeleted_Returns409()
    {
        var site = MakeSite();
        site.IsDeleted = true;
        var deleted = MakeAsset(deleted: true);
        deleted.SiteId = site.Id;
        var b = new MockUnitOfWorkBuilder().WithBatteryAssets(deleted).WithSites(site);
        var r = await new RestoreBatteryAssetCommandHandler(b.Build()).Handle(new RestoreBatteryAssetCommand { Id = deleted.Id }, CancellationToken.None);
        r.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Restore_HappyPath_Succeeds()
    {
        var site = MakeSite();
        var deleted = MakeAsset(deleted: true, siteId: site.Id);
        var b = new MockUnitOfWorkBuilder()
            .WithBatteryAssets(deleted)
            .WithSites(site);
        var r = await new RestoreBatteryAssetCommandHandler(b.Build()).Handle(new RestoreBatteryAssetCommand { Id = deleted.Id }, CancellationToken.None);
        r.IsSuccess.Should().BeTrue();
        deleted.IsDeleted.Should().BeFalse();
    }

    // ---- Transfer ----
    [Fact]
    public async Task Transfer_NotFound_Returns404()
    {
        var b = new MockUnitOfWorkBuilder();
        var r = await new TransferBatteryAssetOwnerCommandHandler(b.Build(), NoOpOutbox.Instance).Handle(new TransferBatteryAssetOwnerCommand { Id = Guid.NewGuid(), NewCustomerId = Guid.NewGuid() }, CancellationToken.None);
        r.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Transfer_SameCustomer_Returns409()
    {
        var asset = MakeAsset();
        var b = new MockUnitOfWorkBuilder().WithBatteryAssets(asset);
        var r = await new TransferBatteryAssetOwnerCommandHandler(b.Build(), NoOpOutbox.Instance).Handle(new TransferBatteryAssetOwnerCommand { Id = asset.Id, NewCustomerId = asset.CustomerId }, CancellationToken.None);
        r.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Transfer_NewCustomerMissing_Returns404()
    {
        var asset = MakeAsset();
        var b = new MockUnitOfWorkBuilder().WithBatteryAssets(asset);
        var r = await new TransferBatteryAssetOwnerCommandHandler(b.Build(), NoOpOutbox.Instance).Handle(new TransferBatteryAssetOwnerCommand { Id = asset.Id, NewCustomerId = Guid.NewGuid() }, CancellationToken.None);
        r.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Transfer_HappyPath_Resets()
    {
        var site = MakeSite();
        var asset = MakeAsset(siteId: site.Id);
        var newCustomerId = Guid.NewGuid();
        var b = new MockUnitOfWorkBuilder()
            .WithBatteryAssets(asset)
            .WithCustomerAccounts(Customer(id: newCustomerId));
        var r = await new TransferBatteryAssetOwnerCommandHandler(b.Build(), NoOpOutbox.Instance).Handle(new TransferBatteryAssetOwnerCommand { Id = asset.Id, NewCustomerId = newCustomerId, Reason = "moved" }, CancellationToken.None);
        r.IsSuccess.Should().BeTrue();
        asset.CustomerId.Should().Be(newCustomerId);
        asset.SiteId.Should().BeNull();
    }

    // ---- Queries ----
    [Fact]
    public async Task GetById_NotFound_Returns404()
    {
        var b = new MockUnitOfWorkBuilder();
        var r = await new GetBatteryAssetByIdQueryHandler(b.Build(), TestBatteryCurrentUserService.Admin()).Handle(new GetBatteryAssetByIdQuery { Id = Guid.NewGuid() }, CancellationToken.None);
        r.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task GetById_Found_ReturnsDto()
    {
        var asset = MakeAsset();
        asset.BatteryType = MakeType();
        var b = new MockUnitOfWorkBuilder()
            .WithCustomerAccounts(Customer())
            .WithBatteryAssets(asset);
        var r = await new GetBatteryAssetByIdQueryHandler(b.Build(), TestBatteryCurrentUserService.Admin()).Handle(new GetBatteryAssetByIdQuery { Id = asset.Id }, CancellationToken.None);
        r.IsSuccess.Should().BeTrue();
        r.Data!.CustomerName.Should().Be("x");
    }

    [Fact]
    public async Task GetList_AppliesAllFilters()
    {
        var asset = MakeAsset();
        asset.BatteryType = MakeType();
        var b = new MockUnitOfWorkBuilder()
            .WithCustomerAccounts(Customer())
            .WithBatteryAssets(asset);
        var r = await new GetBatteryAssetsQueryHandler(b.Build()).Handle(new GetBatteryAssetsQuery
        {
            Keyword = "abc",
            CustomerId = asset.CustomerId,
            BatteryTypeId = asset.BatteryTypeId,
            Status = BatteryStatusEnum.Active
        }, CancellationToken.None);
        r.IsSuccess.Should().BeTrue();
        r.Data!.TotalItems.Should().Be(1);
        r.Data.Items[0].CustomerName.Should().Be("x");
    }

    [Fact]
    public async Task GetList_IncludeDeleted_ReturnsAll()
    {
        var deleted = MakeAsset(deleted: true);
        deleted.BatteryType = MakeType();
        var b = new MockUnitOfWorkBuilder().WithBatteryAssets(deleted);
        var r = await new GetBatteryAssetsQueryHandler(b.Build()).Handle(new GetBatteryAssetsQuery { IncludeDeleted = true }, CancellationToken.None);
        r.Data!.TotalItems.Should().Be(1);
    }

    [Fact]
    public async Task GetMyAssets_NoUser_Returns401()
    {
        var b = new MockUnitOfWorkBuilder();
        var current = new Mock<ICurrentUserService>();
        current.SetupGet(x => x.UserId).Returns((string?)null);
        var r = await new GetMyBatteryAssetsQueryHandler(b.Build(), current.Object).Handle(new GetMyBatteryAssetsQuery(), CancellationToken.None);
        r.StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task GetMyAssets_HappyPath()
    {
        var asset = MakeAsset();
        asset.BatteryType = MakeType();
        var b = new MockUnitOfWorkBuilder().WithBatteryAssets(asset);
        var current = new Mock<ICurrentUserService>();
        current.SetupGet(x => x.UserId).Returns(CustomerId.ToString());
        var r = await new GetMyBatteryAssetsQueryHandler(b.Build(), current.Object).Handle(new GetMyBatteryAssetsQuery(), CancellationToken.None);
        r.Data!.TotalItems.Should().Be(1);
    }

    [Fact]
    public async Task Realtime_NotFound_Returns404()
    {
        var b = new MockUnitOfWorkBuilder();
        var r = await new GetBatteryAssetRealtimeQueryHandler(b.Build(), TestBatteryCurrentUserService.Admin()).Handle(new GetBatteryAssetRealtimeQuery { Id = Guid.NewGuid() }, CancellationToken.None);
        r.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Realtime_WithReading_PopulatesDto()
    {
        var asset = MakeAsset();
        var reading = new SensorReading { BatteryAssetId = asset.Id, Time = DateTime.UtcNow, Voltage = 12, Current = 1, Temperature = 25, SocPercent = 80, CycleCount = 100 };
        var alert = new Alert { Id = Guid.NewGuid(), BatteryAssetId = asset.Id, Status = AlertStatusEnum.Open, AnomalyType = AnomalyTypeEnum.Overvoltage, Severity = AlertSeverityEnum.Critical, Unit = "V", DetectedAt = DateTime.UtcNow, CreatedAt = DateTime.UtcNow };
        var b = new MockUnitOfWorkBuilder()
            .WithBatteryAssets(asset)
            .WithSensorReadings(reading)
            .WithAlerts(alert);
        var r = await new GetBatteryAssetRealtimeQueryHandler(b.Build(), TestBatteryCurrentUserService.Admin()).Handle(new GetBatteryAssetRealtimeQuery { Id = asset.Id }, CancellationToken.None);
        r.IsSuccess.Should().BeTrue();
        r.Data!.Voltage.Should().Be(12);
        r.Data.ActiveAlerts.Should().Be(1);
    }
}
