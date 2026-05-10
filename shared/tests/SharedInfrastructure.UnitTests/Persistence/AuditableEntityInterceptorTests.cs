using Microsoft.EntityFrameworkCore;
using SharedInfrastructure.Persistence.Interceptors;
using SharedInfrastructure.Services;

namespace SharedInfrastructure.UnitTests.Persistence;

public class AuditableEntityInterceptorTests : IDisposable
{
    private readonly Mock<ICurrentUserService> _currentUser = new();
    private readonly TestDbContext _ctx;
    private readonly AuditableEntityInterceptor _interceptor;

    public AuditableEntityInterceptorTests()
    {
        _interceptor = new AuditableEntityInterceptor(_currentUser.Object);
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(databaseName: $"audit-{Guid.NewGuid()}")
            .AddInterceptors(_interceptor)
            .Options;
        _ctx = new TestDbContext(options);
    }

    public void Dispose() => _ctx.Dispose();

    [Fact]
    public async Task Add_WithUserId_SetsCreatedAt_AndCreatedByFromClaim()
    {
        var uid = Guid.NewGuid();
        _currentUser.Setup(c => c.UserId).Returns(uid.ToString());

        var w = new Widget { Id = Guid.NewGuid(), Name = "A" };
        _ctx.Widgets.Add(w);
        var before = DateTime.UtcNow.AddSeconds(-1);
        await _ctx.SaveChangesAsync();

        w.CreatedAt.Should().BeOnOrAfter(before);
        w.CreatedBy.Should().Be(uid);
        w.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public async Task Add_NoUserContext_FallsBackToGuidEmpty()
    {
        _currentUser.Setup(c => c.UserId).Returns((string?)null);

        var w = new Widget { Id = Guid.NewGuid(), Name = "A" };
        _ctx.Widgets.Add(w);
        await _ctx.SaveChangesAsync();

        w.CreatedBy.Should().Be(Guid.Empty);
    }

    [Fact]
    public async Task Add_NonGuidUserId_FallsBackToGuidEmpty()
    {
        _currentUser.Setup(c => c.UserId).Returns("not-a-guid");

        var w = new Widget { Id = Guid.NewGuid(), Name = "A" };
        _ctx.Widgets.Add(w);
        await _ctx.SaveChangesAsync();

        w.CreatedBy.Should().Be(Guid.Empty);
    }

    [Fact]
    public async Task Modify_SetsUpdatedAt()
    {
        _currentUser.Setup(c => c.UserId).Returns(Guid.NewGuid().ToString());

        var w = new Widget { Id = Guid.NewGuid(), Name = "A", Quantity = 1 };
        _ctx.Widgets.Add(w);
        await _ctx.SaveChangesAsync();
        w.UpdatedAt.Should().BeNull();

        w.Quantity = 2;
        var before = DateTime.UtcNow.AddSeconds(-1);
        await _ctx.SaveChangesAsync();

        w.UpdatedAt.Should().NotBeNull();
        w.UpdatedAt!.Value.Should().BeOnOrAfter(before);
    }

    [Fact]
    public async Task Delete_SoftEntity_ConvertsToModified_SetsIsDeletedAndDeletedAt()
    {
        _currentUser.Setup(c => c.UserId).Returns(Guid.NewGuid().ToString());

        var w = new Widget { Id = Guid.NewGuid(), Name = "A" };
        _ctx.Widgets.Add(w);
        await _ctx.SaveChangesAsync();

        _ctx.Widgets.Remove(w);
        var before = DateTime.UtcNow.AddSeconds(-1);
        await _ctx.SaveChangesAsync();

        w.IsDeleted.Should().BeTrue();
        w.DeletedAt.Should().NotBeNull();
        w.DeletedAt!.Value.Should().BeOnOrAfter(before);

        _ctx.ChangeTracker.Clear();
        // Vẫn còn trong DB (soft-delete chứ không hard-delete).
        (await _ctx.Widgets.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == w.Id)).Should().NotBeNull();
    }

    [Fact]
    public async Task Delete_HardDeleteEntity_PhysicallyRemoved_NoSoftDeleteFields()
    {
        _currentUser.Setup(c => c.UserId).Returns(Guid.NewGuid().ToString());

        var hw = new HardWidget { Id = Guid.NewGuid(), Name = "Hard" };
        _ctx.HardWidgets.Add(hw);
        await _ctx.SaveChangesAsync();

        _ctx.HardWidgets.Remove(hw);
        await _ctx.SaveChangesAsync();

        _ctx.ChangeTracker.Clear();
        (await _ctx.HardWidgets.FindAsync(hw.Id)).Should().BeNull();
    }
}
