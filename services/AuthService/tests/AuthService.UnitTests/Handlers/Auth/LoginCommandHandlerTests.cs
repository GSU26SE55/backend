using AuthService.Application.CQRS.Command.Auth;
using AuthService.Application.CQRS.Handler.Auth;
using AuthService.Application.CQRS.Notification.Audit;
using AuthService.Application.DTOs.Response.Auth;
using AuthService.Application.Interfaces.Helpers;
using AuthService.Application.Interfaces.Services;
using AuthService.Domain.Entities;
using AuthService.Domain.Enums;
using AuthService.UnitTests.Helpers;
using MediatR;
using SharedContracts.Events;

namespace AuthService.UnitTests.Handlers.Auth;

public class LoginCommandHandlerTests
{
    private static readonly Guid CustomerRoleId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private readonly Mock<IPasswordHasher> _hasher = new();
    private readonly Mock<IAuthTokenIssuer> _tokenIssuer = new();
    private readonly Mock<ITwoFactorChallengeStore> _challengeStore = new();
    private readonly Mock<IPublisher> _publisher = MockPublisher.NoOp();

    public LoginCommandHandlerTests()
    {
        _tokenIssuer
            .Setup(t => t.IssueAsync(It.IsAny<Account>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((new TokenDTO { AccessToken = "access", RefreshToken = "refresh" }, Guid.NewGuid()));
        _challengeStore
            .Setup(s => s.CreateAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("CHALLENGE_TOKEN");
    }

    private LoginCommandHandler CreateHandler(Mock<AuthService.Application.Interfaces.Repositories.IAuthUnitOfWork> uow)
        => CreateHandler(uow, new Mock<SharedContracts.Interfaces.IMessageProducerService>().Object);

    /// <summary>GH-766 — bản cho phép truyền producer riêng để bắt event outbox.</summary>
    private LoginCommandHandler CreateHandler(
        Mock<AuthService.Application.Interfaces.Repositories.IAuthUnitOfWork> uow,
        SharedContracts.Interfaces.IMessageProducerService producer)
        => new(uow.Object, _hasher.Object, _tokenIssuer.Object, _challengeStore.Object, _publisher.Object,
            producer);

    private static Account ActiveAccount(string passwordHash = "HASHED", bool twoFactorEnabled = false)
    {
        var customerRole = new Role
        {
            Id = CustomerRoleId,
            Name = "Customer",
            NormalizedName = "CUSTOMER",
            Status = RoleStatusEnum.Active
        };

        return new Account
        {
            Id = Guid.NewGuid(),
            Email = "user@example.com",
            PasswordHash = passwordHash,
            FullName = "User",
            Status = AccountStatusEnum.Active,
            RoleId = CustomerRoleId,
            Role = customerRole,
            TwoFactorEnabled = twoFactorEnabled,
            TwoFactorSecret = twoFactorEnabled ? "SOMESECRET" : null,
        };
    }

    [Fact]
    public async Task Login_CorrectPassword_2FAOff_IssuesTokens()
    {
        var account = ActiveAccount();
        account.FailedLoginAttempts = 2;
        var (uow, _, _, _) = MockUnitOfWork.Build(accountSeed: new[] { account });
        _hasher.Setup(h => h.Verify("correct", "HASHED")).Returns(true);

        var response = await CreateHandler(uow).Handle(new LoginCommand { Email = "user@example.com", Password = "correct" }, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.StatusCode.Should().Be(200);
        response.Data!.Tokens!.AccessToken.Should().Be("access");
        response.Data.Challenge.Should().BeNull();
        _tokenIssuer.Verify(t => t.IssueAsync(account, It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Login_CorrectPassword_2FAOn_Returns200ChallengeNotTokens()
    {
        var account = ActiveAccount(twoFactorEnabled: true);
        var (uow, _, _, _) = MockUnitOfWork.Build(accountSeed: new[] { account });
        _hasher.Setup(h => h.Verify(It.IsAny<string>(), It.IsAny<string>())).Returns(true);

        var response = await CreateHandler(uow).Handle(new LoginCommand { Email = account.Email, Password = "correct" }, CancellationToken.None);

        response.StatusCode.Should().Be(200);
        response.Data!.RequiresTwoFactor.Should().BeTrue();
        response.Data.Tokens.Should().BeNull();
        response.Data.Challenge!.ChallengeToken.Should().Be("CHALLENGE_TOKEN");
        response.Data.Challenge.ExpiresInSeconds.Should().Be(300);
        response.Data.Challenge.Methods.Should().Contain("totp").And.Contain("backupCode");
        _tokenIssuer.Verify(t => t.IssueAsync(It.IsAny<Account>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Login_WrongPassword_IncrementsCounter_Returns400()
    {
        var account = ActiveAccount();
        var (uow, _, _, _) = MockUnitOfWork.Build(accountSeed: new[] { account });
        _hasher.Setup(h => h.Verify(It.IsAny<string>(), It.IsAny<string>())).Returns(false);

        var response = await CreateHandler(uow).Handle(new LoginCommand { Email = account.Email, Password = "wrong" }, CancellationToken.None);

        response.StatusCode.Should().Be(400);
        account.FailedLoginAttempts.Should().Be(1);
    }

    [Fact]
    public async Task Login_FifthWrongPassword_LocksAccount_Returns423()
    {
        var account = ActiveAccount();
        account.FailedLoginAttempts = 4;
        var (uow, _, _, _) = MockUnitOfWork.Build(accountSeed: new[] { account });
        _hasher.Setup(h => h.Verify(It.IsAny<string>(), It.IsAny<string>())).Returns(false);

        var response = await CreateHandler(uow).Handle(new LoginCommand { Email = account.Email, Password = "wrong" }, CancellationToken.None);

        response.StatusCode.Should().Be(423);
        account.Status.Should().Be(AccountStatusEnum.Locked);
        account.LockoutEndAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Login_PendingVerificationStatus_Returns403()
    {
        var account = ActiveAccount();
        account.Status = AccountStatusEnum.PendingVerification;
        var (uow, _, _, _) = MockUnitOfWork.Build(accountSeed: new[] { account });

        var response = await CreateHandler(uow).Handle(new LoginCommand { Email = account.Email, Password = "anything" }, CancellationToken.None);

        response.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Login_NonExistentEmail_Returns400()
    {
        var (uow, _, _, _) = MockUnitOfWork.Build();

        var response = await CreateHandler(uow).Handle(new LoginCommand { Email = "ghost@example.com", Password = "anything" }, CancellationToken.None);

        response.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task Login_2FAOn_PublishesLoginPending2FAAudit()
    {
        var account = ActiveAccount(twoFactorEnabled: true);
        var (uow, _, _, _) = MockUnitOfWork.Build(accountSeed: new[] { account });
        _hasher.Setup(h => h.Verify(It.IsAny<string>(), It.IsAny<string>())).Returns(true);

        await CreateHandler(uow).Handle(new LoginCommand { Email = account.Email, Password = "p" }, CancellationToken.None);

        _publisher.Verify(p => p.Publish(
            It.Is<AuditTrailNotification>(n => n.Action == AuditActionEnum.LoginPending2FA && n.TargetAccountId == account.Id && n.IsSuccess),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Login_Success2FAOff_PublishesLoginSuccessAudit()
    {
        var account = ActiveAccount();
        var (uow, _, _, _) = MockUnitOfWork.Build(accountSeed: new[] { account });
        _hasher.Setup(h => h.Verify(It.IsAny<string>(), It.IsAny<string>())).Returns(true);

        await CreateHandler(uow).Handle(new LoginCommand { Email = account.Email, Password = "p" }, CancellationToken.None);

        _publisher.Verify(p => p.Publish(
            It.Is<AuditTrailNotification>(n => n.Action == AuditActionEnum.LoginSuccess && n.TargetAccountId == account.Id && n.IsSuccess),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Login_WrongPassword_PublishesLoginFailedAudit()
    {
        var account = ActiveAccount();
        var (uow, _, _, _) = MockUnitOfWork.Build(accountSeed: new[] { account });
        _hasher.Setup(h => h.Verify(It.IsAny<string>(), It.IsAny<string>())).Returns(false);

        await CreateHandler(uow).Handle(new LoginCommand { Email = account.Email, Password = "wrong" }, CancellationToken.None);

        _publisher.Verify(p => p.Publish(
            It.Is<AuditTrailNotification>(n => n.Action == AuditActionEnum.LoginFailedWrongPassword && n.TargetAccountId == account.Id && !n.IsSuccess),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Login_FifthWrong_PublishesAutoLockedAudit()
    {
        var account = ActiveAccount();
        account.FailedLoginAttempts = 4;
        var (uow, _, _, _) = MockUnitOfWork.Build(accountSeed: new[] { account });
        _hasher.Setup(h => h.Verify(It.IsAny<string>(), It.IsAny<string>())).Returns(false);

        await CreateHandler(uow).Handle(new LoginCommand { Email = account.Email, Password = "wrong" }, CancellationToken.None);

        _publisher.Verify(p => p.Publish(
            It.Is<AuditTrailNotification>(n => n.Action == AuditActionEnum.AccountAutoLocked && n.IsSuccess),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Login_FifthWrong_PublishesAccountStatusChangedEvent()
    {
        // GH-766 — tự khoá là đường đổi trạng thái DỄ XẢY RA NHẤT (không cần admin thao tác).
        // Không phát event ở đây thì read-model bên Battery/Ticket vẫn coi tài khoản đang bị
        // brute-force là hoàn toàn bình thường.
        var account = ActiveAccount();
        account.FailedLoginAttempts = 4;
        var (uow, _, _, _) = MockUnitOfWork.Build(accountSeed: new[] { account });
        _hasher.Setup(h => h.Verify(It.IsAny<string>(), It.IsAny<string>())).Returns(false);

        var captured = new List<AccountStatusChangedEvent>();
        var producer = new Mock<SharedContracts.Interfaces.IMessageProducerService>();
        producer
            .Setup(x => x.PublishAsync(It.IsAny<AccountStatusChangedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<AccountStatusChangedEvent, CancellationToken>((e, _) => captured.Add(e))
            .Returns(Task.CompletedTask);

        await CreateHandler(uow, producer.Object).Handle(
            new LoginCommand { Email = account.Email, Password = "wrong" }, CancellationToken.None);

        var evt = captured.Should().ContainSingle().Subject;
        evt.AccountId.Should().Be(account.Id);
        evt.OldStatus.Should().Be((int)AccountStatusEnum.Active);
        evt.NewStatus.Should().Be((int)AccountStatusEnum.Locked);
    }

    [Fact]
    public async Task Login_WrongPasswordButNotYetLocked_PublishesNoStatusEvent()
    {
        // Sai mật khẩu lần 1-4 KHÔNG đổi trạng thái. Phát event ở đây sẽ dội vô nghĩa xuống mọi
        // service downstream mỗi lần ai đó gõ nhầm mật khẩu.
        var account = ActiveAccount();
        account.FailedLoginAttempts = 1;
        var (uow, _, _, _) = MockUnitOfWork.Build(accountSeed: new[] { account });
        _hasher.Setup(h => h.Verify(It.IsAny<string>(), It.IsAny<string>())).Returns(false);

        var captured = new List<AccountStatusChangedEvent>();
        var producer = new Mock<SharedContracts.Interfaces.IMessageProducerService>();
        producer
            .Setup(x => x.PublishAsync(It.IsAny<AccountStatusChangedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<AccountStatusChangedEvent, CancellationToken>((e, _) => captured.Add(e))
            .Returns(Task.CompletedTask);

        await CreateHandler(uow, producer.Object).Handle(
            new LoginCommand { Email = account.Email, Password = "wrong" }, CancellationToken.None);

        captured.Should().BeEmpty();
    }

    [Fact]
    public async Task Login_NonExistentEmail_PublishesAuditWithNullTarget()
    {
        var (uow, _, _, _) = MockUnitOfWork.Build();

        await CreateHandler(uow).Handle(new LoginCommand { Email = "ghost@example.com", Password = "x" }, CancellationToken.None);

        _publisher.Verify(p => p.Publish(
            It.Is<AuditTrailNotification>(n => n.Action == AuditActionEnum.LoginFailedWrongPassword && n.TargetAccountId == null && n.TargetEmail == "ghost@example.com" && !n.IsSuccess),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
