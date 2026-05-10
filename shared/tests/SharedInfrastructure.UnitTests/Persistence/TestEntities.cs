using Microsoft.EntityFrameworkCore;
using SharedKernels.Domain;

namespace SharedInfrastructure.UnitTests.Persistence;

public class Widget : AuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public int Quantity { get; set; }
}

public class HardWidget : AuditableEntity, IHardDeleteEntity
{
    public string Name { get; set; } = string.Empty;
}

public class TestDbContext : DbContext
{
    public DbSet<Widget> Widgets => Set<Widget>();
    public DbSet<HardWidget> HardWidgets => Set<HardWidget>();

    public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
}
