using AuthService.Application.CQRS.Command.Auth;
using AuthService.Application.CQRS.Handler.Auth;
using AuthService.Application.DTOs.Response.Auth;
using AuthService.Application.Interfaces.Helpers;
using AuthService.Application.Interfaces.Services;
using AuthService.Domain.Entities;
using AuthService.Domain.Enums;
using AuthService.UnitTests.Helpers;
using SharedContracts.Events;

namespace AuthService.UnitTests.Handlers.Login;

/// <summary>
/// GH-766 đã bịt nửa ĐI của cặp Active ↔ Locked (tự khoá sau 5 lần sai mật khẩu có phát
/// <see cref="AccountStatusChangedEvent"/>), nhưng bỏ sót nửa VỀ: nhánh tự mở khoá khi hết hạn
/// lockout trong <c>LoginCommandHandler</c> không phát gì cả.
///
/// <para><b>Vì sao là lỗi thật:</b> <c>BatteryService.AccountStatusChangedConsumer</c> đặt
/// <c>IsActive = (NewStatus == 1)</c>. Tự khoá đẩy <c>IsActive=false</c>; tự mở khoá không phát
/// event nên không có gì đưa nó về <c>true</c>. Khách hàng gõ sai mật khẩu 5 lần rồi đăng nhập lại
/// bình thường vẫn bị BatteryService coi là ngừng hoạt động — vĩnh viễn, cho tới khi có ai đó chạy
/// resync thủ công.</para>
///
/// <para>Khác với NotificationService: bên đó dùng <c>AccountStatusEnumExtensions.IsNotifiable</c>
/// (Locked vẫn tính là còn nhận thông báo) nên cặp chuyển này KHÔNG làm read-model lệch — đó là
/// quyết định có chủ ý, không phải chỗ cần sửa.</para>
/// </summary>
public class LoginAutoUnlockEventTests
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

        public List<AccountStatusChangedEvent> StatusEvents
            => Published.OfType<AccountStatusChangedEvent>().ToList();
    }

    private const string Email = "user@example.com";
    private const string Password = "Correct1@";

    private static Account SeedLockedAccount(DateTime? lockoutEndAt) => new()
    {
        Id = Guid.NewGuid(),
        Email = Email,
        PasswordHash = "hashed",
        FullName = "Nguyễn Văn A",
        Status = AccountStatusEnum.Locked,
        FailedLoginAttempts = 5,
        LockoutEndAt = lockoutEndAt,
        TwoFactorEnabled = false,
        Role = new Role { Id = Guid.NewGuid(), Name = "Customer" },
    };

    private static LoginCommandHandler BuildHandler(
        Account account, CapturingProducer producer, bool passwordValid)
    {
        var (uow, _, _, _) = MockUnitOfWork.Build(accountSeed: new[] { account });

        var hasher = new Mock<IPasswordHasher>();
        hasher.Setup(h => h.Verify(It.IsAny<string>(), It.IsAny<string>())).Returns(passwordValid);

        var tokenIssuer = new Mock<IAuthTokenIssuer>();
        tokenIssuer
            .Setup(t => t.IssueAsync(
                It.IsAny<Account>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((new TokenDTO { AccessToken = "a", RefreshToken = "r" }, Guid.NewGuid()));

        return new LoginCommandHandler(
            uow.Object,
            hasher.Object,
            tokenIssuer.Object,
            new Mock<ITwoFactorChallengeStore>().Object,
            MockPublisher.NoOp().Object,
            producer);
    }

    /// <summary>
    /// Chốt kiểm soát: nửa ĐI đã đúng từ GH-766. Nếu test này đỏ thì harness sai, không phải code sai.
    /// </summary>
    [Fact]
    public async Task AutoLock_AfterMaxFailedAttempts_PublishesActiveToLocked()
    {
        var account = SeedLockedAccount(lockoutEndAt: null);
        account.Status = AccountStatusEnum.Active;
        account.FailedLoginAttempts = 4;   // lần sai kế tiếp là lần thứ 5 ⇒ khoá

        var producer = new CapturingProducer();
        var handler = BuildHandler(account, producer, passwordValid: false);

        await handler.Handle(new LoginCommand { Email = Email, Password = "sai" }, CancellationToken.None);

        var evt = producer.StatusEvents.Should().ContainSingle().Subject;
        evt.OldStatus.Should().Be((int)AccountStatusEnum.Active);
        evt.NewStatus.Should().Be((int)AccountStatusEnum.Locked);
    }

    /// <summary>
    /// Nửa VỀ: hết hạn lockout, đăng nhập đúng mật khẩu ⇒ handler đưa account về Active
    /// (LoginCommandHandler.cs:125-131) nhưng KHÔNG phát event nào.
    /// </summary>
    [Fact]
    public async Task AutoUnlock_WhenLockoutExpired_PublishesLockedToActive()
    {
        var account = SeedLockedAccount(lockoutEndAt: DateTime.UtcNow.AddMinutes(-1));

        var producer = new CapturingProducer();
        var handler = BuildHandler(account, producer, passwordValid: true);

        var resp = await handler.Handle(
            new LoginCommand { Email = Email, Password = Password }, CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        account.Status.Should().Be(AccountStatusEnum.Active);   // handler ĐÃ đổi trạng thái…

        var evt = producer.StatusEvents.Should().ContainSingle().Subject;   // …nhưng không báo cho ai
        evt.OldStatus.Should().Be((int)AccountStatusEnum.Locked);
        evt.NewStatus.Should().Be((int)AccountStatusEnum.Active);
    }
}
