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

/// <summary>
/// GH-769 — bản sao khách hàng bên BatteryService giữ nguyên role cũ sau khi đổi role.
///
/// <para>
/// Bản sao được dựng đúng một lần lúc account kích hoạt, nên một người đã chuyển sang Staff vẫn
/// nằm đó dưới dạng khách hàng đang hoạt động — vẫn được resolve trong các luồng theo khách hàng.
/// </para>
/// </summary>
public class AccountRoleChangedConsumerTests
{
    private readonly Mock<IInboxStore> _inbox = new();

    public AccountRoleChangedConsumerTests()
    {
        _inbox.Setup(i => i.TryBeginAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new InboxClaim(InboxClaimStatus.Claimed, "gh764-test-token"));
    }

    private static ConsumeContext<AccountRoleChangedEvent> Ctx(Guid accountId, string oldRole, string newRole)
    {
        var msg = new AccountRoleChangedEvent(
            accountId, "User@Example.COM", "Nguyễn Văn A", "0901234567",
            oldRole, newRole, DateTime.UtcNow);
        var mock = new Mock<ConsumeContext<AccountRoleChangedEvent>>();
        mock.SetupGet(c => c.Message).Returns(msg);
        mock.SetupGet(c => c.MessageId).Returns(Guid.NewGuid());
        mock.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);
        return mock.Object;
    }

    private static CustomerAccount SeedCustomer(Guid accountId) => new()
    {
        Id = accountId,          // mirror dùng chính AccountId làm PK
        Email = "old@example.com",
        FullName = "Tên Cũ",
        Role = "Customer",
        IsActive = true,
    };

    [Fact]
    public async Task LeavingCustomerRole_DeactivatesMirror_ButKeepsTheRow()
    {
        var accountId = Guid.NewGuid();
        var mirror = SeedCustomer(accountId);
        var uow = new MockUnitOfWorkBuilder().WithCustomerAccounts(mirror);
        var consumer = new AccountRoleChangedConsumer(
            uow.Build(), _inbox.Object, NullLogger<AccountRoleChangedConsumer>.Instance);

        await consumer.Consume(Ctx(accountId, "Customer", "Staff"));

        mirror.IsActive.Should().BeFalse();
        mirror.Role.Should().Be("Staff");
        // Giữ lại bản ghi: pin đã gán vẫn phải truy ngược được chủ cũ, thay vì mồ côi dữ liệu.
        uow.CustomerAccounts.Object.GetAllAsync().Should().ContainSingle();
    }

    [Fact]
    public async Task ReturningToCustomerRole_ReactivatesMirror()
    {
        var accountId = Guid.NewGuid();
        var mirror = SeedCustomer(accountId);
        mirror.IsActive = false;
        mirror.Role = "Staff";
        var uow = new MockUnitOfWorkBuilder().WithCustomerAccounts(mirror);
        var consumer = new AccountRoleChangedConsumer(
            uow.Build(), _inbox.Object, NullLogger<AccountRoleChangedConsumer>.Instance);

        await consumer.Consume(Ctx(accountId, "Staff", "Customer"));

        mirror.IsActive.Should().BeTrue();
        mirror.Role.Should().Be("Customer");
    }

    [Fact]
    public async Task EmailIsNormalisedLowercase_MatchingTheOtherConsumers()
    {
        // AccountStatusChangedConsumer cũng chuẩn hoá về chữ thường. Lệch nhau thì cùng một account
        // có hai dạng email tuỳ theo event nào tới sau — tra cứu theo email sẽ hụt.
        var accountId = Guid.NewGuid();
        var mirror = SeedCustomer(accountId);
        var uow = new MockUnitOfWorkBuilder().WithCustomerAccounts(mirror);
        var consumer = new AccountRoleChangedConsumer(
            uow.Build(), _inbox.Object, NullLogger<AccountRoleChangedConsumer>.Instance);

        await consumer.Consume(Ctx(accountId, "Customer", "Customer"));

        mirror.Email.Should().Be("user@example.com");
    }

    [Fact]
    public async Task TransitionToCustomer_WhenMirrorIsMissing_CreatesProjection()
    {
        // customer_accounts là projection tài khoản Customer, không phải bảng ownership của pin.
        // Thiếu row ở đây phải được self-heal ngay khi role chuyển thành Customer.
        var uow = new MockUnitOfWorkBuilder();
        var consumer = new AccountRoleChangedConsumer(
            uow.Build(), _inbox.Object, NullLogger<AccountRoleChangedConsumer>.Instance);

        var accountId = Guid.NewGuid();
        await consumer.Consume(Ctx(accountId, "Staff", "Customer"));

        uow.CustomerAccounts.Object.GetAllAsync().Should().ContainSingle(item =>
            item.Id == accountId && item.Role == "Customer" && item.IsActive);
    }

    [Fact]
    public async Task DuplicateMessage_DoesNothing()
    {
        _inbox.Setup(i => i.TryBeginAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(InboxClaim.Completed);

        var accountId = Guid.NewGuid();
        var mirror = SeedCustomer(accountId);
        var uow = new MockUnitOfWorkBuilder().WithCustomerAccounts(mirror);
        var consumer = new AccountRoleChangedConsumer(
            uow.Build(), _inbox.Object, NullLogger<AccountRoleChangedConsumer>.Instance);

        await consumer.Consume(Ctx(accountId, "Customer", "Staff"));

        mirror.IsActive.Should().BeTrue("message trùng thì không được đụng vào bản sao");
        mirror.Role.Should().Be("Customer");
    }
}
