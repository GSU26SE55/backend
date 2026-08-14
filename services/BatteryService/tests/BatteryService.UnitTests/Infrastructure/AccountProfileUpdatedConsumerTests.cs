using BatteryService.Domain.Entities;
using BatteryService.Infrastructure.Consumers;
using BatteryService.UnitTests.Helpers;
using FluentAssertions;
using MassTransit;
using Moq;
using SharedContracts.Events;
using SharedInfrastructure.Idempotency;
using Xunit;

namespace BatteryService.UnitTests.Infrastructure;

/// <summary>
/// GH-773 — hồ sơ khách hàng không bao giờ tới bản sao của BatteryService.
///
/// <para>
/// Chú thích của hợp đồng <c>AccountProfileUpdatedEvent</c> ghi rõ BatteryService là subscriber;
/// Ticket và Notification đều đã có consumer, riêng Battery thì không. Bản sao vì thế đứng yên ở
/// giá trị chụp lúc kích hoạt — danh sách site và danh sách pin hiển thị tên/số điện thoại cũ
/// vĩnh viễn, kể cả sau khi khách tự sửa hồ sơ.
/// </para>
/// </summary>
public class AccountProfileUpdatedConsumerTests
{
    private readonly Mock<IInboxStore> _inbox = new();

    public AccountProfileUpdatedConsumerTests()
    {
        _inbox.Setup(i => i.TryBeginAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new InboxClaim(InboxClaimStatus.Claimed, "gh764-test-token"));
    }

    private static ConsumeContext<AccountProfileUpdatedEvent> Ctx(
        Guid accountId,
        string email = "New@Example.COM",
        string fullName = "Tên Mới",
        string? phone = "0987654321")
    {
        var msg = new AccountProfileUpdatedEvent(accountId, email, fullName, phone, AvatarUrl: null);
        var mock = new Mock<ConsumeContext<AccountProfileUpdatedEvent>>();
        mock.SetupGet(c => c.Message).Returns(msg);
        mock.SetupGet(c => c.MessageId).Returns(Guid.NewGuid());
        mock.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);
        return mock.Object;
    }

    private static CustomerAccount SeedMirror(Guid accountId) => new()
    {
        Id = accountId,                  // bản sao dùng chính AccountId làm khoá chính
        Email = "old@example.com",
        FullName = "Tên Cũ",
        PhoneNumber = "0901111111",
        Role = "Customer",
        IsActive = true,
    };

    private AccountProfileUpdatedConsumer Consumer(MockUnitOfWorkBuilder uow)
        => new(uow.Build(), _inbox.Object);

    [Fact]
    public async Task ProfileUpdate_SyncsNameEmailAndPhone()
    {
        var accountId = Guid.NewGuid();
        var mirror = SeedMirror(accountId);
        var uow = new MockUnitOfWorkBuilder().WithCustomerAccounts(mirror);

        await Consumer(uow).Consume(Ctx(accountId));

        mirror.FullName.Should().Be("Tên Mới");
        mirror.PhoneNumber.Should().Be("0987654321");
        // Chuẩn hoá chữ thường khớp AccountStatusChangedConsumer — lệch nhau thì cùng một account
        // có hai dạng email tuỳ event nào tới sau.
        mirror.Email.Should().Be("new@example.com");
        mirror.LastSyncedAtUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task ProfileUpdate_DoesNotTouchRoleOrActiveFlag()
    {
        // Trạng thái và role có event RIÊNG. Chép thêm ở đây là tạo hai đường ghi cùng một ô, và
        // event nào tới sau sẽ thắng bất kể cái nào mới hơn.
        var accountId = Guid.NewGuid();
        var mirror = SeedMirror(accountId);
        mirror.IsActive = false;
        mirror.Role = "Staff";
        var uow = new MockUnitOfWorkBuilder().WithCustomerAccounts(mirror);

        await Consumer(uow).Consume(Ctx(accountId));

        mirror.IsActive.Should().BeFalse();
        mirror.Role.Should().Be("Staff");
    }

    [Fact]
    public async Task ClearingPhoneNumber_IsPropagated_NotIgnored()
    {
        // Xoá số điện thoại là một thay đổi HỢP LỆ. Coi null là "không có gì để cập nhật" sẽ khiến
        // số cũ nằm lại mãi trong danh bạ liên hệ.
        var accountId = Guid.NewGuid();
        var mirror = SeedMirror(accountId);
        var uow = new MockUnitOfWorkBuilder().WithCustomerAccounts(mirror);

        await Consumer(uow).Consume(Ctx(accountId, phone: null));

        mirror.PhoneNumber.Should().BeNull();
    }

    [Fact]
    public async Task UnknownAccount_IsIgnored_NotCreated()
    {
        var uow = new MockUnitOfWorkBuilder();

        var act = async () => await Consumer(uow).Consume(Ctx(Guid.NewGuid()));

        await act.Should().NotThrowAsync();
        uow.CustomerAccounts.Object.GetAllAsync().Should().BeEmpty();
    }

    [Fact]
    public async Task OnlyTheTargetMirrorIsUpdated()
    {
        var target = Guid.NewGuid();
        var other = SeedMirror(Guid.NewGuid());
        var mirror = SeedMirror(target);
        var uow = new MockUnitOfWorkBuilder().WithCustomerAccounts(mirror, other);

        await Consumer(uow).Consume(Ctx(target));

        mirror.FullName.Should().Be("Tên Mới");
        other.FullName.Should().Be("Tên Cũ");
    }

    [Fact]
    public async Task DuplicateMessage_DoesNothing()
    {
        _inbox.Setup(i => i.TryBeginAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(InboxClaim.Completed);

        var accountId = Guid.NewGuid();
        var mirror = SeedMirror(accountId);
        var uow = new MockUnitOfWorkBuilder().WithCustomerAccounts(mirror);

        await Consumer(uow).Consume(Ctx(accountId));

        mirror.FullName.Should().Be("Tên Cũ");
    }
}
