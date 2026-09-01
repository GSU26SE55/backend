using AuthService.Application.CQRS.Command.Auth;
using AuthService.Application.CQRS.Handler.Auth;
using AuthService.Application.CQRS.Notification.Audit;
using AuthService.Application.CQRS.Notification.Login;
using AuthService.Application.DTOs.Response.Auth;
using AuthService.Application.Interfaces.Services;
using AuthService.Domain.Entities;
using AuthService.Domain.Enums;
using AuthService.UnitTests.Helpers;
using MediatR;

namespace AuthService.UnitTests.Handlers.Auth;

public class Verify2FALoginCommandHandlerTests
{
    private static readonly Guid RoleId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private readonly Mock<ITwoFactorChallengeStore> _store = new();
    private readonly Mock<ITotpService> _totp = new();
    private readonly Mock<ITwoFactorSecretProtector> _protector = new();
    private readonly Mock<IBackupCodeGenerator> _backup = new();
    private readonly Mock<IAuthTokenIssuer> _issuer = new();
    private readonly Mock<IPublisher> _publisher = MockPublisher.NoOp();

    public Verify2FALoginCommandHandlerTests()
    {
        _protector.Setup(p => p.Unprotect(It.IsAny<string>())).Returns<string>(s => s.StartsWith("enc:v1:") ? s.Substring(7) : s);
        _protector.Setup(p => p.IsProtected(It.IsAny<string>())).Returns<string>(s => !string.IsNullOrEmpty(s) && s.StartsWith("enc:v1:"));
        _protector.Setup(p => p.Protect(It.IsAny<string>())).Returns<string>(s => "enc:v1:" + s);
        _issuer.Setup(i => i.IssueAsync(It.IsAny<Account>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((new TokenDTO { AccessToken = "ACC", RefreshToken = "REF" }, Guid.NewGuid()));
    }

    private static Account ActiveTwoFa(string secret = "enc:v1:SECRET")
    {
        return new Account
        {
            Id = Guid.NewGuid(),
            Email = "u@example.com",
            PasswordHash = "x",
            FullName = "U",
            Status = AccountStatusEnum.Active,
            TwoFactorEnabled = true,
            TwoFactorSecret = secret,
            TwoFactorSecretEncryptedAt = secret.StartsWith("enc:") ? DateTime.UtcNow : (DateTime?)null,
            RoleId = RoleId,
            Role = new Role { Id = RoleId, Name = "Customer", NormalizedName = "CUSTOMER", Status = RoleStatusEnum.Active },
        };
    }

    private Verify2FALoginCommandHandler Build(Mock<AuthService.Application.Interfaces.Repositories.IAuthUnitOfWork> uow)
        => new(uow.Object, _store.Object, _totp.Object, _protector.Object, _backup.Object, _issuer.Object, _publisher.Object,
               StubRedis.Build().Object,
               new Mock<AuthService.Application.Interfaces.Services.ITwoFactorSmsOtpStore>().Object);

    [Fact]
    public async Task Verify_NoChallenge_Returns422()
    {
        var (uow, _, _, _) = MockUnitOfWork.Build();
        _store.Setup(s => s.GetAsync("missing", It.IsAny<CancellationToken>())).ReturnsAsync((TwoFactorChallengeData?)null);

        var resp = await Build(uow).Handle(new Verify2FALoginCommand { ChallengeToken = "missing", Code = "123456" }, CancellationToken.None);

        resp.StatusCode.Should().Be(422);
    }

    [Fact]
    public async Task Verify_TooManyAttempts_Returns429AndInvalidates()
    {
        var acc = ActiveTwoFa();
        var (uow, _, _, _) = MockUnitOfWork.Build(accountSeed: new[] { acc });
        _store.Setup(s => s.GetAsync("t", It.IsAny<CancellationToken>())).ReturnsAsync(new TwoFactorChallengeData(acc.Id, "1.1.1.1", "ua", 5, DateTime.UtcNow));
        _store.Setup(s => s.IncrementAttemptsAsync("t", It.IsAny<CancellationToken>())).ReturnsAsync(6);

        var resp = await Build(uow).Handle(new Verify2FALoginCommand { ChallengeToken = "t", Code = "123456" }, CancellationToken.None);

        resp.StatusCode.Should().Be(429);
        _store.Verify(s => s.InvalidateAsync("t", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Verify_ValidTotp_IssuesTokens()
    {
        var acc = ActiveTwoFa();
        var (uow, _, _, _) = MockUnitOfWork.Build(accountSeed: new[] { acc });
        _store.Setup(s => s.GetAsync("t", It.IsAny<CancellationToken>())).ReturnsAsync(new TwoFactorChallengeData(acc.Id, "1.1.1.1", "ua", 0, DateTime.UtcNow));
        _store.Setup(s => s.IncrementAttemptsAsync("t", It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _totp.Setup(t => t.VerifyCode("SECRET", "123456", It.IsAny<int>())).Returns(true);

        var resp = await Build(uow).Handle(new Verify2FALoginCommand { ChallengeToken = "t", Code = "123456", IsBackupCode = false }, CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        resp.StatusCode.Should().Be(200);
        resp.Data!.Tokens!.AccessToken.Should().Be("ACC");
        _store.Verify(s => s.InvalidateAsync("t", It.IsAny<CancellationToken>()), Times.Once);
        _publisher.Verify(p => p.Publish(It.Is<AuditTrailNotification>(n => n.Action == AuditActionEnum.LoginWith2FA), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Verify_WrongTotp_Returns422AndKeepsChallenge()
    {
        var acc = ActiveTwoFa();
        var (uow, _, _, _) = MockUnitOfWork.Build(accountSeed: new[] { acc });
        _store.Setup(s => s.GetAsync("t", It.IsAny<CancellationToken>())).ReturnsAsync(new TwoFactorChallengeData(acc.Id, "1.1.1.1", "ua", 0, DateTime.UtcNow));
        _store.Setup(s => s.IncrementAttemptsAsync("t", It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _totp.Setup(t => t.VerifyCode(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>())).Returns(false);

        var resp = await Build(uow).Handle(new Verify2FALoginCommand { ChallengeToken = "t", Code = "999999" }, CancellationToken.None);

        resp.StatusCode.Should().Be(422);
        _store.Verify(s => s.InvalidateAsync("t", It.IsAny<CancellationToken>()), Times.Never);
        // #39 QA solars.io.vn 2026-08-29: trước đây chỉ publish LoginAttemptNotification khi Success
        // ⇒ login history của chính chủ không hiện lần sai 2FA.
        _publisher.Verify(p => p.Publish(
            It.Is<LoginAttemptNotification>(n => n.AccountId == acc.Id && n.Result == LoginAttemptResult.WrongTwoFactorCode),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Verify_ValidBackupCode_RedeemsAndIssuesTokens()
    {
        var acc = ActiveTwoFa();
        var hash = "H";
        var bc = new BackupCode { Id = Guid.NewGuid(), AccountId = acc.Id, CodeHash = hash, RedeemedAt = null };
        var (uow, _, _, _) = MockUnitOfWork.Build(accountSeed: new[] { acc }, backupCodeSeed: new[] { bc });
        _store.Setup(s => s.GetAsync("t", It.IsAny<CancellationToken>())).ReturnsAsync(new TwoFactorChallengeData(acc.Id, "1.1.1.1", "ua", 0, DateTime.UtcNow));
        _store.Setup(s => s.IncrementAttemptsAsync("t", It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _backup.Setup(b => b.Verify("abcd-1234", hash)).Returns(true);

        var resp = await Build(uow).Handle(new Verify2FALoginCommand { ChallengeToken = "t", Code = "abcd-1234", IsBackupCode = true }, CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        bc.RedeemedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Verify_BackupCodeUnknown_Returns422()
    {
        var acc = ActiveTwoFa();
        var (uow, _, _, _) = MockUnitOfWork.Build(accountSeed: new[] { acc }, backupCodeSeed: Array.Empty<BackupCode>());
        _store.Setup(s => s.GetAsync("t", It.IsAny<CancellationToken>())).ReturnsAsync(new TwoFactorChallengeData(acc.Id, "1.1.1.1", "ua", 0, DateTime.UtcNow));
        _store.Setup(s => s.IncrementAttemptsAsync("t", It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var resp = await Build(uow).Handle(new Verify2FALoginCommand { ChallengeToken = "t", Code = "wrong-code", IsBackupCode = true }, CancellationToken.None);

        resp.StatusCode.Should().Be(422);
    }

    [Fact]
    public async Task Verify_LegacyPlaintextSecret_LazyReEncryptsAfterSuccess()
    {
        var acc = ActiveTwoFa(secret: "PLAINTEXTSECRET"); // no enc: prefix
        acc.TwoFactorSecretEncryptedAt = null;
        var (uow, _, _, _) = MockUnitOfWork.Build(accountSeed: new[] { acc });
        _store.Setup(s => s.GetAsync("t", It.IsAny<CancellationToken>())).ReturnsAsync(new TwoFactorChallengeData(acc.Id, "1.1.1.1", "ua", 0, DateTime.UtcNow));
        _store.Setup(s => s.IncrementAttemptsAsync("t", It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _totp.Setup(t => t.VerifyCode("PLAINTEXTSECRET", "123456", It.IsAny<int>())).Returns(true);

        var resp = await Build(uow).Handle(new Verify2FALoginCommand { ChallengeToken = "t", Code = "123456" }, CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        acc.TwoFactorSecret.Should().StartWith("enc:v1:");
        acc.TwoFactorSecretEncryptedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Verify_AccountSuspendedMidChallenge_Returns403()
    {
        var acc = ActiveTwoFa();
        acc.Status = AccountStatusEnum.Suspended;
        var (uow, _, _, _) = MockUnitOfWork.Build(accountSeed: new[] { acc });
        _store.Setup(s => s.GetAsync("t", It.IsAny<CancellationToken>())).ReturnsAsync(new TwoFactorChallengeData(acc.Id, "1.1.1.1", "ua", 0, DateTime.UtcNow));
        _store.Setup(s => s.IncrementAttemptsAsync("t", It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var resp = await Build(uow).Handle(new Verify2FALoginCommand { ChallengeToken = "t", Code = "123456" }, CancellationToken.None);

        resp.StatusCode.Should().Be(403);
        _store.Verify(s => s.InvalidateAsync("t", It.IsAny<CancellationToken>()), Times.Once);
    }
}
