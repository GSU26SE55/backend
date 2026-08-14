using BatteryService.Domain.Entities;
using BatteryService.Infrastructure.Consumers;
using BatteryService.UnitTests.Helpers;
using FluentAssertions;
using MassTransit;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SharedContracts.Events;
using SharedInfrastructure.Idempotency;
using Xunit;

namespace BatteryService.UnitTests.Infrastructure;

public class AccountSyncSnapshotConsumerTests
{
    private readonly Mock<IInboxStore> _inbox = new();

    public AccountSyncSnapshotConsumerTests()
    {
        _inbox.Setup(i => i.TryBeginAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new InboxClaim(InboxClaimStatus.Claimed, "snapshot-test-token"));
    }

    [Fact]
    public async Task ActiveCustomerMissingFromMirror_IsCreated()
    {
        var accountId = Guid.NewGuid();
        var snapshotAt = DateTime.UtcNow;
        var uow = new MockUnitOfWorkBuilder();

        await Consumer(uow).Consume(Context(accountId, snapshotAt));

        var mirror = uow.CustomerAccounts.Object.GetAllAsync().Should().ContainSingle().Subject;
        mirror.Id.Should().Be(accountId);
        mirror.Email.Should().Be("customer@example.com");
        mirror.Role.Should().Be("Customer");
        mirror.IsActive.Should().BeTrue();
        mirror.LastSyncedAtUtc.Should().Be(snapshotAt);
    }

    [Fact]
    public async Task OlderSnapshot_DoesNotOverwriteMirror()
    {
        var accountId = Guid.NewGuid();
        var mirror = new CustomerAccount
        {
            Id = accountId,
            Email = "new@example.com",
            FullName = "New Name",
            Role = "Customer",
            IsActive = true,
            LastSyncedAtUtc = DateTime.UtcNow
        };
        var uow = new MockUnitOfWorkBuilder().WithCustomerAccounts(mirror);

        await Consumer(uow).Consume(Context(accountId, mirror.LastSyncedAtUtc.AddMinutes(-1)));

        mirror.Email.Should().Be("new@example.com");
        mirror.FullName.Should().Be("New Name");
    }

    [Fact]
    public async Task UnknownNonCustomer_IsNotAdded()
    {
        var uow = new MockUnitOfWorkBuilder();

        await Consumer(uow).Consume(Context(Guid.NewGuid(), DateTime.UtcNow, role: "Staff"));

        uow.CustomerAccounts.Object.GetAllAsync().Should().BeEmpty();
    }

    private AccountSyncSnapshotConsumer Consumer(MockUnitOfWorkBuilder uow) =>
        new(uow.Build(), _inbox.Object, NullLogger<AccountSyncSnapshotConsumer>.Instance);

    private static ConsumeContext<AccountSyncSnapshotEvent> Context(
        Guid accountId,
        DateTime snapshotAt,
        string role = "Customer")
    {
        var message = new AccountSyncSnapshotEvent(
            accountId,
            "Customer@Example.COM",
            "Customer Name",
            " 0901234567 ",
            role,
            IsActive: true,
            IsDeleted: false,
            SnapshotAtUtc: snapshotAt,
            Reason: "Resync");
        var context = new Mock<ConsumeContext<AccountSyncSnapshotEvent>>();
        context.SetupGet(item => item.Message).Returns(message);
        context.SetupGet(item => item.MessageId).Returns(Guid.NewGuid());
        context.SetupGet(item => item.CancellationToken).Returns(CancellationToken.None);
        return context.Object;
    }
}
