using AuthService.Application.CQRS.Command.Account;
using AuthService.Application.CQRS.Command.Auth;
using AuthService.Application.CQRS.Handler.Account;
using AuthService.Application.CQRS.Handler.Auth;
using AuthService.Domain.Entities;
using AuthService.Domain.Enums;
using AuthService.UnitTests.Helpers;
using FluentAssertions;
using SharedContracts.Events;
using SharedContracts.Interfaces;

namespace AuthService.UnitTests.Handlers.Accounts;

/// <summary>
/// GH-766 — <c>AccountStatusChangedEvent</c> chưa từng được publish.
///
/// <para>
/// BatteryService và TicketService đều đã viết consumer cho event này, nhưng tìm khắp AuthService
/// không có một chỗ nào <c>new AccountStatusChangedEvent</c>. Hậu quả: khoá / đình chỉ / cấm /
/// vô hiệu hoá tài khoản xong, hai read-model kia vẫn thấy account Active — người đã bị khoá vẫn
/// được coi là hợp lệ ở phía dưới.
/// </para>
/// <para>
/// <c>AccountSyncSnapshotEvent</c> (thêm 02/08/2026) KHÔNG thay thế được: chỉ NotificationService
/// consume nó, Battery/Ticket không nghe.
/// </para>
/// <para>
/// AuthService CÓ outbox — <c>IMessageProducerService.PublishAsync</c> ghi vào OutboxMessages của
/// cùng DbContext, nên phải publish TRƯỚC <c>SaveChangesAsync</c> để nguyên tử với commit.
/// </para>
/// </summary>
public class AccountStatusChangedEventTests
{
    /// <summary>Bắt lại các event đi qua outbox để khẳng định đúng nội dung, không chỉ đúng số lần.</summary>
    private sealed class CapturingProducer : IMessageProducerService
    {
        public List<object> Published { get; } = new();

        public Task PublishAsync<T>(T message, CancellationToken cancellationToken = default)
            where T : SharedContracts.Events.Root.IntegrationEvent
        {
            if (message is not null) Published.Add(message);
            return Task.CompletedTask;
        }

        public List<AccountStatusChangedEvent> StatusEvents
            => Published.OfType<AccountStatusChangedEvent>().ToList();
    }

    private static Account SeedAccount(AccountStatusEnum status, Guid? id = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        Email = "user@example.com",
        FullName = "Nguyễn Văn A",
        Status = status,
        Role = new Role { Id = Guid.NewGuid(), Name = "Customer" },
    };

    [Theory]
    [InlineData(AccountStatusEnum.Locked)]
    [InlineData(AccountStatusEnum.Suspended)]
    [InlineData(AccountStatusEnum.Banned)]
    [InlineData(AccountStatusEnum.Inactive)]
    public async Task AdminChangeStatus_PublishesEvent_WithBothOldAndNewStatus(AccountStatusEnum target)
    {
        var account = SeedAccount(AccountStatusEnum.Active);
        var (uow, _, _, _) = MockUnitOfWork.Build(accountSeed: new[] { account });
        var producer = new CapturingProducer();
        var handler = new ChangeAccountStatusCommandHandler(uow.Object, MockPublisher.NoOp().Object, producer);

        var resp = await handler.Handle(
            new ChangeAccountStatusCommand { Id = account.Id, Status = target, Reason = "Vi phạm điều khoản" },
            CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();

        var evt = producer.StatusEvents.Should().ContainSingle().Subject;
        evt.AccountId.Should().Be(account.Id);
        evt.Email.Should().Be(account.Email);
        // Cả trạng thái CŨ lẫn MỚI đều cần: consumer nào muốn phản ứng theo hướng chuyển đổi
        // (ví dụ chỉ dọn dẹp khi rời khỏi Active) sẽ không suy ra được nếu thiếu vế cũ.
        evt.OldStatus.Should().Be((int)AccountStatusEnum.Active);
        evt.NewStatus.Should().Be((int)target);
        evt.Reason.Should().Be("Vi phạm điều khoản");
    }

    [Fact]
    public async Task AdminChangeStatus_BackToActive_AlsoPublishes()
    {
        // Chỉ phát lúc KHOÁ mà quên lúc MỞ thì read-model kẹt ở trạng thái khoá vĩnh viễn —
        // còn tệ hơn không phát gì, vì người dùng hợp lệ bị chặn mà không ai hiểu vì sao.
        var account = SeedAccount(AccountStatusEnum.Suspended);
        var (uow, _, _, _) = MockUnitOfWork.Build(accountSeed: new[] { account });
        var producer = new CapturingProducer();
        var handler = new ChangeAccountStatusCommandHandler(uow.Object, MockPublisher.NoOp().Object, producer);

        await handler.Handle(
            new ChangeAccountStatusCommand { Id = account.Id, Status = AccountStatusEnum.Active },
            CancellationToken.None);

        var evt = producer.StatusEvents.Should().ContainSingle().Subject;
        evt.OldStatus.Should().Be((int)AccountStatusEnum.Suspended);
        evt.NewStatus.Should().Be((int)AccountStatusEnum.Active);
    }

    [Fact]
    public async Task AdminChangeStatus_NoOpTransition_PublishesNothing()
    {
        // Đổi sang đúng trạng thái đang có ⇒ handler trả sớm. Phát event ở đây chỉ tạo nhiễu và
        // làm consumer chạy không công.
        var account = SeedAccount(AccountStatusEnum.Active);
        var (uow, _, _, _) = MockUnitOfWork.Build(accountSeed: new[] { account });
        var producer = new CapturingProducer();
        var handler = new ChangeAccountStatusCommandHandler(uow.Object, MockPublisher.NoOp().Object, producer);

        await handler.Handle(
            new ChangeAccountStatusCommand { Id = account.Id, Status = AccountStatusEnum.Active },
            CancellationToken.None);

        producer.StatusEvents.Should().BeEmpty();
    }

    [Fact]
    public async Task AdminUnlock_PublishesLockedToActive()
    {
        var account = SeedAccount(AccountStatusEnum.Locked);
        account.FailedLoginAttempts = 5;
        account.LockoutEndAt = DateTime.UtcNow.AddMinutes(10);
        var (uow, _, _, _) = MockUnitOfWork.Build(accountSeed: new[] { account });
        var producer = new CapturingProducer();
        var handler = new UnlockAccountCommandHandler(uow.Object, MockPublisher.NoOp().Object, producer);

        var resp = await handler.Handle(new UnlockAccountCommand { Id = account.Id }, CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        var evt = producer.StatusEvents.Should().ContainSingle().Subject;
        evt.OldStatus.Should().Be((int)AccountStatusEnum.Locked);
        evt.NewStatus.Should().Be((int)AccountStatusEnum.Active);
    }

    [Fact]
    public async Task AdminUnlock_WhenNotLocked_PublishesNothing()
    {
        var account = SeedAccount(AccountStatusEnum.Active);
        var (uow, _, _, _) = MockUnitOfWork.Build(accountSeed: new[] { account });
        var producer = new CapturingProducer();
        var handler = new UnlockAccountCommandHandler(uow.Object, MockPublisher.NoOp().Object, producer);

        await handler.Handle(new UnlockAccountCommand { Id = account.Id }, CancellationToken.None);

        producer.StatusEvents.Should().BeEmpty();
    }

    [Fact]
    public async Task SelfDeactivate_PublishesActiveToInactive()
    {
        var account = SeedAccount(AccountStatusEnum.Active);
        var (uow, _, _, _) = MockUnitOfWork.Build(accountSeed: new[] { account });
        var producer = new CapturingProducer();
        var handler = new DeactivateMeCommandHandler(uow.Object, MockPublisher.NoOp().Object, producer);

        var resp = await handler.Handle(
            new DeactivateMeCommand { AccountId = account.Id }, CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        var evt = producer.StatusEvents.Should().ContainSingle().Subject;
        evt.OldStatus.Should().Be((int)AccountStatusEnum.Active);
        evt.NewStatus.Should().Be((int)AccountStatusEnum.Inactive);
    }

    [Fact]
    public async Task Reactivate_PublishesBackToActive()
    {
        var account = SeedAccount(AccountStatusEnum.Inactive);
        account.PasswordHash = "x";
        account.IsDeleted = true;
        account.DeletedAt = DateTime.UtcNow.AddDays(-5);
        account.OtpCode = "123456";
        account.OtpExpiredAt = DateTime.UtcNow.AddMinutes(5);
        var (uow, _, _, _) = MockUnitOfWork.Build(accountSeed: new[] { account });
        var producer = new CapturingProducer();
        var handler = new ReactivateVerifyCommandHandler(uow.Object, MockPublisher.NoOp().Object, producer);

        var resp = await handler.Handle(
            new ReactivateVerifyCommand { Email = account.Email, Otp = "123456" }, CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        var evt = producer.StatusEvents.Should().ContainSingle().Subject;
        evt.OldStatus.Should().Be((int)AccountStatusEnum.Inactive);
        evt.NewStatus.Should().Be((int)AccountStatusEnum.Active);
    }
}
