using Microsoft.EntityFrameworkCore;
using SharedInfrastructure.Persistence.Repositories;

namespace SharedInfrastructure.UnitTests.Persistence;

public class GenericRepositoryTests : IDisposable
{
    private readonly TestDbContext _ctx;

    public GenericRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(databaseName: $"genrepo-{Guid.NewGuid()}")
            .Options;
        _ctx = new TestDbContext(options);
    }

    public void Dispose() => _ctx.Dispose();

    private GenericRepository<Widget> Sut() => new(_ctx);

    [Fact]
    public async Task AddAsync_AddsEntity_AndSaveChangesPersists()
    {
        var w = new Widget { Id = Guid.NewGuid(), Name = "A", Quantity = 1 };

        await Sut().AddAsync(w);
        await _ctx.SaveChangesAsync();

        (await _ctx.Widgets.FindAsync(w.Id)).Should().NotBeNull();
    }

    [Fact]
    public async Task GetByIdAsync_FindsExisting_ReturnsEntity()
    {
        var w = new Widget { Id = Guid.NewGuid(), Name = "A" };
        _ctx.Widgets.Add(w);
        await _ctx.SaveChangesAsync();

        var found = await Sut().GetByIdAsync(w.Id);

        found.Should().NotBeNull();
        found!.Name.Should().Be("A");
    }

    [Fact]
    public async Task GetByIdAsync_Missing_ReturnsNull()
    {
        var found = await Sut().GetByIdAsync(Guid.NewGuid());
        found.Should().BeNull();
    }

    [Fact]
    public async Task GetAllAsync_ReturnsIQueryable_NotAwaited()
    {
        _ctx.Widgets.AddRange(
            new Widget { Id = Guid.NewGuid(), Name = "A", Quantity = 1 },
            new Widget { Id = Guid.NewGuid(), Name = "B", Quantity = 2 });
        await _ctx.SaveChangesAsync();

        // GetAllAsync trả IQueryable trực tiếp (không phải Task) — verify chain qua LINQ.
        var qty1 = Sut().GetAllAsync().Where(w => w.Quantity == 1).ToList();
        qty1.Should().ContainSingle().Which.Name.Should().Be("A");
    }

    [Fact]
    public async Task FindAsync_AppliesPredicate()
    {
        _ctx.Widgets.AddRange(
            new Widget { Id = Guid.NewGuid(), Name = "Apple" },
            new Widget { Id = Guid.NewGuid(), Name = "Banana" });
        await _ctx.SaveChangesAsync();

        var apples = Sut().FindAsync(w => w.Name.StartsWith("App")).ToList();
        apples.Should().ContainSingle().Which.Name.Should().Be("Apple");
    }

    [Fact]
    public async Task UpdateAsync_IsVoid_AndPersistsAfterSaveChanges()
    {
        var w = new Widget { Id = Guid.NewGuid(), Name = "Old", Quantity = 1 };
        _ctx.Widgets.Add(w);
        await _ctx.SaveChangesAsync();
        _ctx.Entry(w).State = EntityState.Detached;

        var loaded = await _ctx.Widgets.FindAsync(w.Id);
        loaded!.Name = "New";
        Sut().UpdateAsync(loaded); // void — không await
        await _ctx.SaveChangesAsync();

        _ctx.ChangeTracker.Clear();
        var verify = await _ctx.Widgets.FindAsync(w.Id);
        verify!.Name.Should().Be("New");
    }

    [Fact]
    public async Task DeleteAsync_AttachedEntity_RemovesFromContext()
    {
        var w = new Widget { Id = Guid.NewGuid(), Name = "X" };
        _ctx.Widgets.Add(w);
        await _ctx.SaveChangesAsync();

        Sut().DeleteAsync(w); // void
        await _ctx.SaveChangesAsync();

        (await _ctx.Widgets.FindAsync(w.Id)).Should().BeNull();
    }

    [Fact]
    public async Task DeleteAsync_DetachedEntity_AttachesThenRemoves()
    {
        var id = Guid.NewGuid();
        var w = new Widget { Id = id, Name = "X" };
        _ctx.Widgets.Add(w);
        await _ctx.SaveChangesAsync();
        _ctx.Entry(w).State = EntityState.Detached;

        // Detached → DeleteAsync phải Attach rồi Remove (không throw).
        var act = () =>
        {
            Sut().DeleteAsync(w);
        };
        act.Should().NotThrow();

        await _ctx.SaveChangesAsync();
        (await _ctx.Widgets.FindAsync(id)).Should().BeNull();
    }

    [Fact]
    public async Task AnyAsync_True_WhenMatchExists()
    {
        _ctx.Widgets.Add(new Widget { Id = Guid.NewGuid(), Name = "X", Quantity = 5 });
        await _ctx.SaveChangesAsync();

        (await Sut().AnyAsync(w => w.Quantity == 5)).Should().BeTrue();
        (await Sut().AnyAsync(w => w.Quantity == 99)).Should().BeFalse();
    }
}
