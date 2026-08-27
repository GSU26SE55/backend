using MassTransit;
using MockQueryable.Moq;
using NotificationService.Application.Consumers;
using NotificationService.Application.Interfaces.Repositories;
using NotificationService.Domain.Entities;
using SharedContracts.Events;
using SharedKernels.Interfaces;

namespace NotificationService.UnitTests.Consumers;

/// <summary>
/// GH-604 — Sync read-model account từ AccountActivated/ProfileUpdated/Deleted.
/// Test gọi trực tiếp consumer với mock <see cref="ConsumeContext{T}"/> + capture repo calls.
/// </summary>
public class AccountReadModelSyncConsumerTests
{
    private sealed class RepoCapture
    {
        public List<AccountReadModel> Added { get; } = new();
        public List<AccountReadModel> Updated { get; } = new();
        public List<AccountReadModel> Deleted { get; } = new();
        public Mock<INotificationUnitOfWork> Uow { get; init; } = null!;
    }

    private static RepoCapture BuildUow(IEnumerable<AccountReadModel> seed)
    {
        var capture = new RepoCapture { Uow = new Mock<INotificationUnitOfWork>() };

        var repo = new Mock<IGenericRepository<AccountReadModel>>();
        repo.Setup(r => r.GetAllAsync()).Returns(seed.AsQueryable().BuildMock());
        repo.Setup(r => r.AddAsync(It.IsAny<AccountReadModel>()))
            .Callback<AccountReadModel>(capture.Added.Add).Returns(Task.CompletedTask);
        repo.Setup(r => r.UpdateAsync(It.IsAny<AccountReadModel>()))
            .Callback<AccountReadModel>(capture.Updated.Add);
        repo.Setup(r => r.DeleteAsync(It.IsAny<AccountReadModel>()))
            .Callback<AccountReadModel>(capture.Deleted.Add);

        capture.Uow.SetupGet(u => u.Accounts).Returns(repo.Object);
        capture.Uow.Setup(u => u.BeginTransactionAsync()).Returns(Task.CompletedTask);
        capture.Uow.Setup(u => u.CommitTransactionAsync()).Returns(Task.CompletedTask);
        capture.Uow.Setup(u => u.RollbackTransactionAsync()).Returns(Task.CompletedTask);
        capture.Uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        return capture;
    }

    private static ConsumeContext<T> Ctx<T>(T message) where T : class
    {
        var ctx = new Mock<ConsumeContext<T>>();
        ctx.SetupGet(c => c.Message).Returns(message);
        ctx.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);
        return ctx.Object;
    }

    private static AccountReadModel Existing(Guid id, string role, string fullName = "Old Name") => new()
    {
        Id = id,
        Email = "old@x.z",
        FullName = fullName,
        Role = role,
        IsActive = true,
        IsDeleted = false,
        LastSyncedAtUtc = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc)
    };

    [Fact]
    public async Task Activated_NewAccount_Adds_WithRoleAndActive()
    {
        var cap = BuildUow(Array.Empty<AccountReadModel>());
        var evt = new AccountActivatedEvent(Guid.NewGuid(), "M@X.Z", "Manager One", "0901234567", "Manager", "admin-create");

        await new AccountActivatedSyncConsumer(cap.Uow.Object).Consume(Ctx(evt));

        cap.Added.Should().ContainSingle();
        var added = cap.Added[0];
        added.Id.Should().Be(evt.AccountId);
        added.Role.Should().Be("Manager");
        added.IsActive.Should().BeTrue();
        added.Email.Should().Be("m@x.z");
    }

    [Fact]
    public async Task Activated_ExistingAccount_Updates()
    {
        var id = Guid.NewGuid();
        var cap = BuildUow(new[] { Existing(id, "Staff") });
        var evt = new AccountActivatedEvent(id, "n@x.z", "New Name", null, "Manager", "otp");

        await new AccountActivatedSyncConsumer(cap.Uow.Object).Consume(Ctx(evt));

        cap.Added.Should().BeEmpty();
        cap.Updated.Should().ContainSingle();
        cap.Updated[0].Role.Should().Be("Manager");
        cap.Updated[0].FullName.Should().Be("New Name");
    }

    [Fact]
    public async Task ProfileUpdated_Existing_UpdatesNameKeepsRole()
    {
        var id = Guid.NewGuid();
        var cap = BuildUow(new[] { Existing(id, "Manager") });
        var evt = new AccountProfileUpdatedEvent(id, "n@x.z", "Renamed", "0900000000", null);

        await new AccountProfileUpdatedSyncConsumer(cap.Uow.Object).Consume(Ctx(evt));

        cap.Updated.Should().ContainSingle();
        cap.Updated[0].FullName.Should().Be("Renamed");
        cap.Updated[0].Role.Should().Be("Manager"); // không đổi
    }

    [Fact]
    public async Task ProfileUpdated_NotExists_NoOp()
    {
        var cap = BuildUow(Array.Empty<AccountReadModel>());
        var evt = new AccountProfileUpdatedEvent(Guid.NewGuid(), "n@x.z", "Ghost", null, null);

        await new AccountProfileUpdatedSyncConsumer(cap.Uow.Object).Consume(Ctx(evt));

        cap.Added.Should().BeEmpty();
        cap.Updated.Should().BeEmpty();
        cap.Uow.Verify(u => u.BeginTransactionAsync(), Times.Never);
    }

    [Fact]
    public async Task Deleted_Existing_SoftDeletes_AndDeactivates()
    {
        var id = Guid.NewGuid();
        var cap = BuildUow(new[] { Existing(id, "Manager") });
        var evt = new AccountDeletedEvent(id, "x@x.z", "admin-delete");

        await new AccountDeletedSyncConsumer(cap.Uow.Object).Consume(Ctx(evt));

        cap.Deleted.Should().ContainSingle();
        cap.Deleted[0].IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task Deleted_NotExists_NoOp()
    {
        var cap = BuildUow(Array.Empty<AccountReadModel>());
        var evt = new AccountDeletedEvent(Guid.NewGuid(), "x@x.z", "self");

        await new AccountDeletedSyncConsumer(cap.Uow.Object).Consume(Ctx(evt));

        cap.Deleted.Should().BeEmpty();
        cap.Uow.Verify(u => u.BeginTransactionAsync(), Times.Never);
    }

    [Fact]
    public async Task StatusChanged_AutoLock_UpdatesNotificationEligibilityImmediately()
    {
        var id = Guid.NewGuid();
        var existing = Existing(id, "Customer");
        var cap = BuildUow(new[] { existing });
        var evt = new AccountStatusChangedEvent(
            id, "new@example.com", 1, 2, "automatic lockout",
            Role: "Customer", FullName: "Customer A", PhoneNumber: "0901234567", IsActive: true);

        await new AccountStatusChangedSyncConsumer(cap.Uow.Object).Consume(Ctx(evt));

        cap.Updated.Should().ContainSingle();
        existing.Email.Should().Be("new@example.com");
        existing.IsActive.Should().BeTrue("Locked accounts remain eligible for operational notifications");
        existing.LastSnapshotAtUtc.Should().Be(evt.OccurredAt);
    }

    [Fact]
    public async Task StatusChanged_StaleEvent_DoesNotOverwriteNewerSnapshot()
    {
        var id = Guid.NewGuid();
        var existing = Existing(id, "Customer");
        var incomingAt = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);
        existing.LastSnapshotAtUtc = incomingAt.AddMinutes(1);
        var cap = BuildUow(new[] { existing });
        var evt = new AccountStatusChangedEvent(
            id, "old@example.com", 1, 3, "stale",
            Role: "Customer", FullName: "Old", IsActive: false)
        {
            OccurredAt = incomingAt
        };

        await new AccountStatusChangedSyncConsumer(cap.Uow.Object).Consume(Ctx(evt));

        cap.Updated.Should().BeEmpty();
        existing.Email.Should().Be("old@x.z");
        existing.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task RoleChanged_MissingReadModel_CreatesCompleteProjection()
    {
        var cap = BuildUow(Array.Empty<AccountReadModel>());
        var changedAt = new DateTime(2026, 8, 2, 0, 0, 0, DateTimeKind.Utc);
        var evt = new AccountRoleChangedEvent(
            Guid.NewGuid(), "ADMIN@EXAMPLE.COM", "Admin A", null,
            "Staff", "Admin", changedAt, AccountStatus: 1);

        await new AccountRoleChangedSyncConsumer(cap.Uow.Object).Consume(Ctx(evt));

        cap.Added.Should().ContainSingle();
        cap.Added[0].Email.Should().Be("admin@example.com");
        cap.Added[0].Role.Should().Be("Admin");
        cap.Added[0].IsActive.Should().BeTrue();
        cap.Added[0].LastSnapshotAtUtc.Should().Be(changedAt);
    }
}
