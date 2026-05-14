using BatteryService.Application.CQRS.Command.BatteryType;
using BatteryService.Application.CQRS.Handler.BatteryType;
using BatteryService.Application.CQRS.Query.BatteryType;
using BatteryService.Domain.Entities;
using BatteryService.Domain.Enums;
using BatteryService.UnitTests.Helpers;

namespace BatteryService.UnitTests.Application;

public class BatteryTypeFullHandlerTests
{
    private static BatteryType MakeType(string name = "LiFePO4 12V", bool deleted = false)
        => new()
        {
            Id = Guid.NewGuid(),
            Name = name,
            Manufacturer = "ACME",
            NominalCapacityAh = 100,
            NominalVoltage = 12,
            Chemistry = BatteryChemistryEnum.LiFePO4,
            MaxCycleCount = 2000,
            CreatedAt = DateTime.UtcNow,
            IsDeleted = deleted,
            DeletedAt = deleted ? DateTime.UtcNow : null
        };

    [Fact]
    public async Task Update_NotFound_Returns404()
    {
        var b = new MockUnitOfWorkBuilder();
        var handler = new UpdateBatteryTypeCommandHandler(b.Build());
        var result = await handler.Handle(new UpdateBatteryTypeCommand { Id = Guid.NewGuid(), Name = "x", NominalCapacityAh = 1, NominalVoltage = 1, Chemistry = BatteryChemistryEnum.LiFePO4, MaxCycleCount = 1 }, CancellationToken.None);
        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Update_DuplicateName_Returns409()
    {
        var target = MakeType("A");
        var other = MakeType("B");
        var b = new MockUnitOfWorkBuilder().WithBatteryTypes(target, other);
        var handler = new UpdateBatteryTypeCommandHandler(b.Build());
        var result = await handler.Handle(new UpdateBatteryTypeCommand { Id = target.Id, Name = "B", NominalCapacityAh = 1, NominalVoltage = 1, Chemistry = BatteryChemistryEnum.LiFePO4, MaxCycleCount = 1 }, CancellationToken.None);
        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Update_Valid_Returns200AndUpdates()
    {
        var target = MakeType("A");
        var b = new MockUnitOfWorkBuilder().WithBatteryTypes(target);
        var handler = new UpdateBatteryTypeCommandHandler(b.Build());
        var result = await handler.Handle(new UpdateBatteryTypeCommand { Id = target.Id, Name = "A v2", Manufacturer = "X", NominalCapacityAh = 50, NominalVoltage = 24, Chemistry = BatteryChemistryEnum.Nmc, MaxCycleCount = 5000, Description = "desc" }, CancellationToken.None);
        result.IsSuccess.Should().BeTrue();
        result.Data!.Name.Should().Be("A v2");
        b.BatteryTypes.Verify(r => r.UpdateAsync(It.IsAny<BatteryType>()), Times.Once);
        b.UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Delete_NotFound_Returns404()
    {
        var b = new MockUnitOfWorkBuilder();
        var handler = new DeleteBatteryTypeCommandHandler(b.Build());
        var result = await handler.Handle(new DeleteBatteryTypeCommand { Id = Guid.NewGuid() }, CancellationToken.None);
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Delete_WithAssignedAssets_Returns409()
    {
        var t = MakeType();
        var asset = new BatteryAsset { Id = Guid.NewGuid(), BatteryTypeId = t.Id, SerialNumber = "S1", CustomerId = Guid.NewGuid(), InstallDate = DateTime.UtcNow };
        var b = new MockUnitOfWorkBuilder().WithBatteryTypes(t).WithBatteryAssets(asset);
        var handler = new DeleteBatteryTypeCommandHandler(b.Build());
        var result = await handler.Handle(new DeleteBatteryTypeCommand { Id = t.Id }, CancellationToken.None);
        result.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Delete_NoAssets_Succeeds()
    {
        var t = MakeType();
        var b = new MockUnitOfWorkBuilder().WithBatteryTypes(t);
        var handler = new DeleteBatteryTypeCommandHandler(b.Build());
        var result = await handler.Handle(new DeleteBatteryTypeCommand { Id = t.Id }, CancellationToken.None);
        result.IsSuccess.Should().BeTrue();
        b.BatteryTypes.Verify(r => r.DeleteAsync(It.IsAny<BatteryType>()), Times.Once);
    }

    [Fact]
    public async Task Restore_NotDeleted_Returns404()
    {
        var t = MakeType();
        var b = new MockUnitOfWorkBuilder().WithBatteryTypes(t);
        var handler = new RestoreBatteryTypeCommandHandler(b.Build());
        var result = await handler.Handle(new RestoreBatteryTypeCommand { Id = t.Id }, CancellationToken.None);
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Restore_DuplicateName_Returns409()
    {
        var deleted = MakeType("A", deleted: true);
        var alive = MakeType("A");
        var b = new MockUnitOfWorkBuilder().WithBatteryTypes(deleted, alive);
        var handler = new RestoreBatteryTypeCommandHandler(b.Build());
        var result = await handler.Handle(new RestoreBatteryTypeCommand { Id = deleted.Id }, CancellationToken.None);
        result.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Restore_Succeeds()
    {
        var deleted = MakeType("A", deleted: true);
        var b = new MockUnitOfWorkBuilder().WithBatteryTypes(deleted);
        var handler = new RestoreBatteryTypeCommandHandler(b.Build());
        var result = await handler.Handle(new RestoreBatteryTypeCommand { Id = deleted.Id }, CancellationToken.None);
        result.IsSuccess.Should().BeTrue();
        deleted.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public async Task GetById_NotFound_Returns404()
    {
        var b = new MockUnitOfWorkBuilder();
        var handler = new GetBatteryTypeByIdQueryHandler(b.Build());
        var result = await handler.Handle(new GetBatteryTypeByIdQuery { Id = Guid.NewGuid() }, CancellationToken.None);
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task GetById_Found_ReturnsDto()
    {
        var t = MakeType("X");
        var b = new MockUnitOfWorkBuilder().WithBatteryTypes(t);
        var handler = new GetBatteryTypeByIdQueryHandler(b.Build());
        var result = await handler.Handle(new GetBatteryTypeByIdQuery { Id = t.Id }, CancellationToken.None);
        result.IsSuccess.Should().BeTrue();
        result.Data!.Name.Should().Be("X");
    }

    [Fact]
    public async Task GetList_AppliesFiltersAndPagination()
    {
        var items = Enumerable.Range(1, 15).Select(i => MakeType($"Type-{i}")).ToArray();
        items[0].Manufacturer = "SpecialMfg";
        var b = new MockUnitOfWorkBuilder().WithBatteryTypes(items);
        var handler = new GetBatteryTypesQueryHandler(b.Build());

        var pagedResult = await handler.Handle(new GetBatteryTypesQuery { PageNumber = 2, PageSize = 5 }, CancellationToken.None);
        pagedResult.Data!.TotalItems.Should().Be(15);
        pagedResult.Data.Items.Count.Should().Be(5);
        pagedResult.Data.PageNumber.Should().Be(2);

        var keywordResult = await handler.Handle(new GetBatteryTypesQuery { Keyword = "specialmfg" }, CancellationToken.None);
        keywordResult.Data!.Items.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetList_IncludeDeleted_ReturnsAll()
    {
        var alive = MakeType("A");
        var dead = MakeType("B", deleted: true);
        var b = new MockUnitOfWorkBuilder().WithBatteryTypes(alive, dead);
        var handler = new GetBatteryTypesQueryHandler(b.Build());
        var result = await handler.Handle(new GetBatteryTypesQuery { IncludeDeleted = true }, CancellationToken.None);
        result.Data!.TotalItems.Should().Be(2);
    }
}
