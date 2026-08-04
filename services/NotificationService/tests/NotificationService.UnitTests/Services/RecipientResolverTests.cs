using MockQueryable.Moq;
using NotificationService.Application.Interfaces.Repositories;
using NotificationService.Domain.Entities;
using NotificationService.Infrastructure.Services;
using SharedKernels.Interfaces;

namespace NotificationService.UnitTests.Services;

/// <summary>
/// GH-604 — RecipientResolver: resolve active account IDs theo role từ read-model.
/// LUÔN filter IsDeleted + IsActive, so khớp role không phân biệt hoa-thường.
/// </summary>
public class RecipientResolverTests
{
    private static AccountReadModel Account(string role, bool isActive = true, bool isDeleted = false) => new()
    {
        Id = Guid.NewGuid(),
        Email = "x@y.z",
        FullName = "X",
        Role = role,
        IsActive = isActive,
        IsDeleted = isDeleted,
        LastSyncedAtUtc = new DateTime(2026, 6, 23, 0, 0, 0, DateTimeKind.Utc)
    };

    private static RecipientResolver BuildResolver(IEnumerable<AccountReadModel> seed)
    {
        var repo = new Mock<IGenericRepository<AccountReadModel>>();
        repo.Setup(r => r.GetAllAsync()).Returns(seed.AsQueryable().BuildMock());
        // Sprint 6.4 — resolver chuyển sang overload no-tracking cho truy vấn chỉ-đọc (#AUTH-35).
        // Thiếu stub này thì Moq trả IQueryable rỗng không async được, và lỗi hiện ra dưới dạng
        // "doesn't implement IAsyncEnumerable" chứ không phải sai kết quả — rất mất thời gian truy.
        repo.Setup(r => r.GetAllAsync(It.IsAny<bool>())).Returns(seed.AsQueryable().BuildMock());

        var uow = new Mock<INotificationUnitOfWork>();
        uow.SetupGet(u => u.Accounts).Returns(repo.Object);
        return new RecipientResolver(uow.Object);
    }

    [Fact]
    public async Task GetActiveByRole_SingleRole_ReturnsOnlyActiveNonDeleted()
    {
        var activeManager = Account("Manager");
        var resolver = BuildResolver(new[]
        {
            activeManager,
            Account("Manager", isActive: false),     // loại — inactive
            Account("Manager", isDeleted: true),      // loại — deleted
            Account("Admin"),                         // loại — khác role
            Account("Customer")
        });

        var result = await resolver.GetActiveByRoleAsync(CancellationToken.None, "Manager");

        result.Should().ContainSingle().Which.Should().Be(activeManager.Id);
    }

    [Fact]
    public async Task GetActiveByRole_MultipleRoles_ReturnsUnion()
    {
        var manager = Account("Manager");
        var admin = Account("Admin");
        var resolver = BuildResolver(new[] { manager, admin, Account("Staff"), Account("Customer") });

        var result = await resolver.GetActiveByRoleAsync(CancellationToken.None, "Manager", "Admin");

        result.Should().BeEquivalentTo(new[] { manager.Id, admin.Id });
    }

    [Fact]
    public async Task GetActiveByRole_IsCaseInsensitive()
    {
        var manager = Account("Manager");
        var resolver = BuildResolver(new[] { manager });

        var result = await resolver.GetActiveByRoleAsync(CancellationToken.None, "manager");

        result.Should().ContainSingle().Which.Should().Be(manager.Id);
    }

    [Fact]
    public async Task GetActiveByRole_NoMatch_ReturnsEmpty()
    {
        var resolver = BuildResolver(new[] { Account("Customer"), Account("Staff") });

        var result = await resolver.GetActiveByRoleAsync(CancellationToken.None, "Manager", "Admin");

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetActiveByRole_NoRolesProvided_ReturnsEmpty()
    {
        var resolver = BuildResolver(new[] { Account("Manager") });

        var result = await resolver.GetActiveByRoleAsync(CancellationToken.None);

        result.Should().BeEmpty();
    }
}
