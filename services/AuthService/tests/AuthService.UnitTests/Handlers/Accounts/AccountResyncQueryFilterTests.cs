using AuthService.Application.CQRS.Command.Account;
using AuthService.Application.CQRS.Handler.Account;
using AuthService.Domain.Entities;
using AuthService.Domain.Enums;
using AuthService.Infrastructure.Implements.Repositories;
using AuthService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Events;
using SharedInfrastructure.Persistence.Interceptors;
using SharedInfrastructure.Services;

namespace AuthService.UnitTests.Handlers.Accounts;

/// <summary>
/// <c>AccountResyncCommandHandler</c> tồn tại để dựng lại read-model của các service khác, kể cả
/// việc đánh dấu account đã xoá. Nhưng <c>AccountConfiguration</c> đặt
/// <c>HasQueryFilter(a =&gt; !a.IsDeleted)</c>, còn <c>GenericRepository.GetAllAsync()</c> chỉ trả
/// <c>_dbSet.AsQueryable()</c> — thiếu <c>IgnoreQueryFilters()</c> là EF loại account đã xoá một
/// cách im lặng, và mọi lượt đối soát đều báo <c>DeletedAccounts = 0</c>.
///
/// <para>Test này PHẢI chạy trên <see cref="ApplicationDbContext"/> thật (InMemory) chứ không dùng
/// <c>MockUnitOfWork</c> được: mock trả <c>IQueryable</c> dựng từ list nên không có global query
/// filter, tức là không thể phát hiện được đúng lớp lỗi mà nó cần canh.</para>
///
/// <para>Ghi chú: AuthService CÓ global query filter, ngược với quy ước chung ghi ở
/// <c>.claude/rules/tech/be.md</c> ("dự án KHÔNG cấu hình global query filter"). Đừng suy luận từ
/// rule đó khi làm việc với service này.</para>
/// </summary>
public class AccountResyncQueryFilterTests
{
    private sealed class CapturingProducer : IMessageProducerService
    {
        public List<object> Published { get; } = new();

        public Task PublishAsync<T>(T message, CancellationToken cancellationToken = default)
            where T : SharedContracts.Events.Root.IntegrationEvent
        {
            if (message is not null)
                Published.Add(message);
            return Task.CompletedTask;
        }

        public List<AccountSyncSnapshotEvent> Snapshots
            => Published.OfType<AccountSyncSnapshotEvent>().ToList();
    }

    private static ApplicationDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"resync-filter-{Guid.NewGuid():N}")
            .Options;

        return new ApplicationDbContext(
            options, new AuditableEntityInterceptor(new Mock<ICurrentUserService>().Object));
    }

    private static Account NewAccount(Role role, bool isDeleted) => new()
    {
        Id = Guid.NewGuid(),
        Email = isDeleted ? "deleted@example.com" : "alive@example.com",
        PasswordHash = "x",
        FullName = "Nguyễn Văn A",
        Status = AccountStatusEnum.Active,
        RoleId = role.Id,
        IsDeleted = isDeleted,
        DeletedAt = isDeleted ? DateTime.UtcNow.AddDays(-1) : null,
    };

    private static async Task<(ApplicationDbContext ctx, Role role)> SeedAsync(params bool[] deletedFlags)
    {
        var ctx = NewContext();
        var role = new Role
        {
            Id = Guid.NewGuid(),
            Name = "Customer",
            NormalizedName = "CUSTOMER",
            Status = RoleStatusEnum.Active,
        };

        ctx.Roles.Add(role);
        foreach (var deleted in deletedFlags)
            ctx.Users.Add(NewAccount(role, deleted));

        await ctx.SaveChangesAsync();
        return (ctx, role);
    }

    [Fact]
    public async Task Resync_PublishesSnapshot_ForSoftDeletedAccountToo()
    {
        var (ctx, _) = await SeedAsync(false, true);
        await using var _ctx = ctx;

        var producer = new CapturingProducer();
        var handler = new AccountResyncCommandHandler(new UnitOfWork(ctx), producer);

        var resp = await handler.Handle(new AccountResyncCommand(), CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        producer.Snapshots.Should().HaveCount(2, "cả account còn sống lẫn account đã xoá đều phải "
            + "được phát để read-model bên kia mirror theo");

        var deleted = producer.Snapshots.Should().ContainSingle(s => s.IsDeleted).Subject;
        deleted.Email.Should().Be("deleted@example.com");
        // Account đã xoá thì không còn nhận thông báo, bất kể Status là gì.
        deleted.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task Resync_ReportsDeletedAccountsInSummary()
    {
        var (ctx, _) = await SeedAsync(false, true, true);
        await using var _ctx = ctx;

        var producer = new CapturingProducer();
        var handler = new AccountResyncCommandHandler(new UnitOfWork(ctx), producer);

        var resp = await handler.Handle(new AccountResyncCommand(), CancellationToken.None);

        // Con số này là thứ admin nhìn để biết lượt đối soát có chạm tới account đã xoá hay không.
        // Báo 0 trong khi DB có 2 dòng đã xoá là im lặng nói dối.
        resp.Data!.TotalAccounts.Should().Be(3);
        resp.Data.DeletedAccounts.Should().Be(2);
        resp.Data.ActiveAccounts.Should().Be(1);
    }

    [Fact]
    public async Task Resync_ByAccountId_FindsSoftDeletedAccount()
    {
        var (ctx, _) = await SeedAsync(true);
        await using var _ctx = ctx;

        var target = await ctx.Set<Account>().IgnoreQueryFilters().SingleAsync();

        var producer = new CapturingProducer();
        var handler = new AccountResyncCommandHandler(new UnitOfWork(ctx), producer);

        // Đối soát một account cụ thể: nếu query filter ăn mất thì handler trả 404 "Account not
        // found" cho một account có thật — đúng chỗ admin sẽ đi tìm khi phát hiện read-model lệch.
        var resp = await handler.Handle(
            new AccountResyncCommand { AccountId = target.Id }, CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        producer.Snapshots.Should().ContainSingle().Which.IsDeleted.Should().BeTrue();
    }

    [Fact]
    public async Task Resync_PublishesAuthoritativeResolvedAvatarSnapshot()
    {
        var (ctx, _) = await SeedAsync(false);
        await using var _ctx = ctx;
        var account = await ctx.Users.SingleAsync();
        ctx.AccountProfiles.Add(new AccountProfile
        {
            Id = Guid.NewGuid(),
            AccountId = account.Id,
            ExternalAvatarUrl = "https://cdn.example.com/google-avatar.png",
            AvatarSource = AvatarSourceEnum.Google
        });
        await ctx.SaveChangesAsync();

        var producer = new CapturingProducer();
        var handler = new AccountResyncCommandHandler(new UnitOfWork(ctx), producer);

        await handler.Handle(new AccountResyncCommand { AccountId = account.Id }, CancellationToken.None);

        var snapshot = producer.Snapshots.Should().ContainSingle().Subject;
        snapshot.HasAvatarSnapshot.Should().BeTrue();
        snapshot.AvatarUrl.Should().Be("https://cdn.example.com/google-avatar.png");
    }
}
