using AuthService.Application.CQRS.Command.Account;
using AuthService.Application.CQRS.Command.Admin;
using AuthService.Application.CQRS.Command.Auth;
using AuthService.Application.CQRS.Handler.Account;
using AuthService.Application.CQRS.Handler.Admin;
using AuthService.Application.CQRS.Handler.Auth;
using AuthService.Application.CQRS.Notification.Audit;
using AuthService.Application.Interfaces.Helpers;
using AuthService.Application.Interfaces.Services;
using AuthService.Domain.Entities;
using AuthService.Domain.Enums;
using AuthService.UnitTests.Helpers;
using MediatR;
using Microsoft.Extensions.Configuration;

namespace AuthService.UnitTests.Handlers.Auth;

// --------- Enable2FA (DEPRECATED) ---------
public class Enable2FACommandHandlerTests
{
    [Fact]
    public async Task Enable_LegacyEndpoint_Returns410()
    {
        var handler = new Enable2FACommandHandler();
        var resp = await handler.Handle(new Enable2FACommand { AccountId = Guid.NewGuid() }, CancellationToken.None);

        resp.IsSuccess.Should().BeFalse();
        resp.StatusCode.Should().Be(410);
        resp.Message.Should().Contain("/2fa/init");
    }
}

// --------- Init2FA ---------
public class Init2FACommandHandlerTests
{
    private static IConfiguration Cfg() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["JwtSettings:Issuer"] = "TestApp" })
        .Build();

    private readonly Mock<ITotpService> _totp = new();
    private readonly Mock<ITwoFactorPendingStore> _pending = new();

    public Init2FACommandHandlerTests()
    {
        _totp.Setup(t => t.GenerateSecret()).Returns("BASE32SECRET");
        _totp.Setup(t => t.BuildOtpAuthUri(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns("otpauth://totp/TestApp:u@example.com?secret=BASE32SECRET");
    }

    [Fact]
    public async Task Init_ActiveAccount_ReturnsSetupDtoWithoutEnabling()
    {
        var acc = new Account { Id = Guid.NewGuid(), Email = "u@example.com", PasswordHash = "x", FullName = "U", Status = AccountStatusEnum.Active };
        var (uow, _, _, _) = MockUnitOfWork.Build(accountSeed: new[] { acc });
        var handler = new Init2FACommandHandler(uow.Object, _totp.Object, _pending.Object, Cfg());

        var resp = await handler.Handle(new Init2FACommand { AccountId = acc.Id }, CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        resp.StatusCode.Should().Be(200);
        resp.Data!.Secret.Should().Be("BASE32SECRET");
        resp.Data.OtpAuthUri.Should().StartWith("otpauth://totp/");
        resp.Data.PendingToken.Should().NotBeNullOrEmpty();
        acc.TwoFactorEnabled.Should().BeFalse(); // KEY: not enabled yet
        _pending.Verify(p => p.SetAsync(acc.Id, "BASE32SECRET", It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Init_AlreadyEnabled_Returns409()
    {
        var acc = new Account { Id = Guid.NewGuid(), Email = "u@example.com", PasswordHash = "x", FullName = "U", Status = AccountStatusEnum.Active, TwoFactorEnabled = true, TwoFactorSecret = "EXISTING" };
        var (uow, _, _, _) = MockUnitOfWork.Build(accountSeed: new[] { acc });
        var handler = new Init2FACommandHandler(uow.Object, _totp.Object, _pending.Object, Cfg());

        var resp = await handler.Handle(new Init2FACommand { AccountId = acc.Id }, CancellationToken.None);

        resp.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Init_NotFound_Returns404()
    {
        var (uow, _, _, _) = MockUnitOfWork.Build();
        var handler = new Init2FACommandHandler(uow.Object, _totp.Object, _pending.Object, Cfg());

        var resp = await handler.Handle(new Init2FACommand { AccountId = Guid.NewGuid() }, CancellationToken.None);

        resp.StatusCode.Should().Be(404);
    }
}

// --------- Confirm2FA ---------
public class Confirm2FACommandHandlerTests
{
    private readonly Mock<ITotpService> _totp = new();
    private readonly Mock<ITwoFactorPendingStore> _pending = new();
    private readonly Mock<ITwoFactorSecretProtector> _protector = new();
    private readonly Mock<IBackupCodeGenerator> _backup = new();
    private readonly Mock<IPublisher> _publisher = MockPublisher.NoOp();

    public Confirm2FACommandHandlerTests()
    {
        _protector.Setup(p => p.Protect(It.IsAny<string>())).Returns<string>(s => "enc:v1:" + s);
        _backup.Setup(b => b.Generate(8)).Returns(Enumerable.Range(1, 8).Select(i => $"abcd-{i}{i}{i}{i}").ToList());
        _backup.Setup(b => b.Hash(It.IsAny<string>())).Returns<string>(p => "HASH(" + p + ")");
    }

    private Confirm2FACommandHandler Build(Mock<AuthService.Application.Interfaces.Repositories.IAuthUnitOfWork> uow)
        => new(uow.Object, _totp.Object, _pending.Object, _protector.Object, _backup.Object, _publisher.Object);

    [Fact]
    public async Task Confirm_ValidCode_EnablesAndIssuesBackupCodes()
    {
        var acc = new Account { Id = Guid.NewGuid(), Email = "u@example.com", PasswordHash = "x", FullName = "U", Status = AccountStatusEnum.Active };
        var (uow, _, _, _) = MockUnitOfWork.Build(accountSeed: new[] { acc });
        _pending.Setup(p => p.GetAsync(acc.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TwoFactorPendingData("BASE32SECRET", "ptok-1", DateTime.UtcNow));
        _totp.Setup(t => t.VerifyCode("BASE32SECRET", "123456", It.IsAny<int>())).Returns(true);

        var resp = await Build(uow).Handle(new Confirm2FACommand { AccountId = acc.Id, PendingToken = "ptok-1", Code = "123456" }, CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        resp.Data!.Enabled.Should().BeTrue();
        resp.Data.BackupCodes.Should().HaveCount(8);
        acc.TwoFactorEnabled.Should().BeTrue();
        acc.TwoFactorSecret.Should().StartWith("enc:v1:");
        acc.TwoFactorSecretEncryptedAt.Should().NotBeNull();
        _pending.Verify(p => p.RemoveAsync(acc.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Confirm_PendingExpired_Returns422()
    {
        var acc = new Account { Id = Guid.NewGuid(), Email = "u@example.com", PasswordHash = "x", FullName = "U", Status = AccountStatusEnum.Active };
        var (uow, _, _, _) = MockUnitOfWork.Build(accountSeed: new[] { acc });
        _pending.Setup(p => p.GetAsync(acc.Id, It.IsAny<CancellationToken>())).ReturnsAsync((TwoFactorPendingData?)null);

        var resp = await Build(uow).Handle(new Confirm2FACommand { AccountId = acc.Id, PendingToken = "x", Code = "123456" }, CancellationToken.None);

        resp.StatusCode.Should().Be(422);
        acc.TwoFactorEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task Confirm_WrongPendingToken_Returns422()
    {
        var acc = new Account { Id = Guid.NewGuid(), Email = "u@example.com", PasswordHash = "x", FullName = "U", Status = AccountStatusEnum.Active };
        var (uow, _, _, _) = MockUnitOfWork.Build(accountSeed: new[] { acc });
        _pending.Setup(p => p.GetAsync(acc.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TwoFactorPendingData("SECRET", "RIGHT", DateTime.UtcNow));

        var resp = await Build(uow).Handle(new Confirm2FACommand { AccountId = acc.Id, PendingToken = "WRONG", Code = "123456" }, CancellationToken.None);

        resp.StatusCode.Should().Be(422);
        acc.TwoFactorEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task Confirm_AlreadyEnabled_Returns409()
    {
        var acc = new Account { Id = Guid.NewGuid(), Email = "u@example.com", PasswordHash = "x", FullName = "U", Status = AccountStatusEnum.Active, TwoFactorEnabled = true, TwoFactorSecret = "enc:v1:EXISTING" };
        var (uow, _, _, _) = MockUnitOfWork.Build(accountSeed: new[] { acc });

        var resp = await Build(uow).Handle(new Confirm2FACommand { AccountId = acc.Id, PendingToken = "ptok", Code = "123456" }, CancellationToken.None);

        resp.StatusCode.Should().Be(409);
        // Pending KHÔNG được check / consume khi account đã có 2FA — fail fast
        _pending.Verify(p => p.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Confirm_NotFound_Returns404()
    {
        var (uow, _, _, _) = MockUnitOfWork.Build();
        var resp = await Build(uow).Handle(new Confirm2FACommand { AccountId = Guid.NewGuid(), PendingToken = "ptok", Code = "123456" }, CancellationToken.None);
        resp.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Confirm_WrongCode_PendingKept_Returns422()
    {
        var acc = new Account { Id = Guid.NewGuid(), Email = "u@example.com", PasswordHash = "x", FullName = "U", Status = AccountStatusEnum.Active };
        var (uow, _, _, _) = MockUnitOfWork.Build(accountSeed: new[] { acc });
        _pending.Setup(p => p.GetAsync(acc.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TwoFactorPendingData("SECRET", "ptok", DateTime.UtcNow));
        _totp.Setup(t => t.VerifyCode("SECRET", "999999", It.IsAny<int>())).Returns(false);

        var resp = await Build(uow).Handle(new Confirm2FACommand { AccountId = acc.Id, PendingToken = "ptok", Code = "999999" }, CancellationToken.None);

        resp.StatusCode.Should().Be(422);
        acc.TwoFactorEnabled.Should().BeFalse();
        _pending.Verify(p => p.RemoveAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}

// --------- Disable2FA ---------
public class Disable2FACommandHandlerTests
{
    private readonly Mock<IPasswordHasher> _hasher = new();
    private readonly Mock<ITotpService> _totp = new();
    private readonly Mock<ITwoFactorSecretProtector> _protector = new();
    private readonly Mock<IPublisher> _publisher = MockPublisher.NoOp();

    public Disable2FACommandHandlerTests()
    {
        _protector.Setup(p => p.Unprotect(It.IsAny<string>())).Returns<string>(s => s.StartsWith("enc:v1:") ? s.Substring(7) : s);
    }

    private Disable2FACommandHandler Build(Mock<AuthService.Application.Interfaces.Repositories.IAuthUnitOfWork> uow)
        => new(uow.Object, _hasher.Object, _totp.Object, _protector.Object, _publisher.Object);

    [Fact]
    public async Task Disable_PasswordAndTotpOk_ClearsSecret()
    {
        var acc = new Account { Id = Guid.NewGuid(), Email = "u@example.com", PasswordHash = "PH", FullName = "U", Status = AccountStatusEnum.Active, TwoFactorEnabled = true, TwoFactorSecret = "enc:v1:S", TwoFactorSecretEncryptedAt = DateTime.UtcNow };
        var (uow, _, _, _) = MockUnitOfWork.Build(accountSeed: new[] { acc });
        _hasher.Setup(h => h.Verify("pass", "PH")).Returns(true);
        _totp.Setup(t => t.VerifyCode("S", "123456", It.IsAny<int>())).Returns(true);

        var resp = await Build(uow).Handle(new Disable2FACommand { AccountId = acc.Id, Password = "pass", TotpCode = "123456" }, CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        resp.StatusCode.Should().Be(200);
        acc.TwoFactorEnabled.Should().BeFalse();
        acc.TwoFactorSecret.Should().BeNull();
        acc.TwoFactorSecretEncryptedAt.Should().BeNull();
    }

    [Fact]
    public async Task Disable_WrongPassword_Returns422()
    {
        var acc = new Account { Id = Guid.NewGuid(), Email = "u@example.com", PasswordHash = "PH", FullName = "U", Status = AccountStatusEnum.Active, TwoFactorEnabled = true, TwoFactorSecret = "S" };
        var (uow, _, _, _) = MockUnitOfWork.Build(accountSeed: new[] { acc });
        _hasher.Setup(h => h.Verify(It.IsAny<string>(), It.IsAny<string>())).Returns(false);

        var resp = await Build(uow).Handle(new Disable2FACommand { AccountId = acc.Id, Password = "wrong", TotpCode = "123456" }, CancellationToken.None);

        resp.StatusCode.Should().Be(422);
        acc.TwoFactorEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task Disable_WrongTotp_Returns422()
    {
        var acc = new Account { Id = Guid.NewGuid(), Email = "u@example.com", PasswordHash = "PH", FullName = "U", Status = AccountStatusEnum.Active, TwoFactorEnabled = true, TwoFactorSecret = "S" };
        var (uow, _, _, _) = MockUnitOfWork.Build(accountSeed: new[] { acc });
        _hasher.Setup(h => h.Verify("pass", "PH")).Returns(true);
        _totp.Setup(t => t.VerifyCode(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>())).Returns(false);

        var resp = await Build(uow).Handle(new Disable2FACommand { AccountId = acc.Id, Password = "pass", TotpCode = "000000" }, CancellationToken.None);

        resp.StatusCode.Should().Be(422);
        acc.TwoFactorEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task Disable_AlreadyOff_Returns200Idempotent()
    {
        var acc = new Account { Id = Guid.NewGuid(), Email = "u@example.com", PasswordHash = "PH", FullName = "U", Status = AccountStatusEnum.Active, TwoFactorEnabled = false };
        var (uow, _, _, _) = MockUnitOfWork.Build(accountSeed: new[] { acc });

        var resp = await Build(uow).Handle(new Disable2FACommand { AccountId = acc.Id, Password = "p", TotpCode = "123456" }, CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        resp.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task Disable_NotFound_Returns404()
    {
        var (uow, _, _, _) = MockUnitOfWork.Build();
        var resp = await Build(uow).Handle(new Disable2FACommand { AccountId = Guid.NewGuid(), Password = "p", TotpCode = "123456" }, CancellationToken.None);
        resp.StatusCode.Should().Be(404);
    }
}

// --------- RegenerateBackupCodes ---------
public class RegenerateBackupCodesCommandHandlerTests
{
    private readonly Mock<ITotpService> _totp = new();
    private readonly Mock<ITwoFactorSecretProtector> _protector = new();
    private readonly Mock<IBackupCodeGenerator> _backup = new();
    private readonly Mock<IPublisher> _publisher = MockPublisher.NoOp();

    public RegenerateBackupCodesCommandHandlerTests()
    {
        _protector.Setup(p => p.Unprotect(It.IsAny<string>())).Returns<string>(s => s.StartsWith("enc:v1:") ? s.Substring(7) : s);
        _backup.Setup(b => b.Generate(8)).Returns(Enumerable.Range(1, 8).Select(i => $"new-{i}").ToList());
        _backup.Setup(b => b.Hash(It.IsAny<string>())).Returns<string>(p => "H(" + p + ")");
    }

    private RegenerateBackupCodesCommandHandler Build(Mock<AuthService.Application.Interfaces.Repositories.IAuthUnitOfWork> uow)
        => new(uow.Object, _totp.Object, _protector.Object, _backup.Object, _publisher.Object);

    [Fact]
    public async Task Regen_ValidTotp_Returns8NewCodes()
    {
        var acc = new Account { Id = Guid.NewGuid(), Email = "u@example.com", PasswordHash = "x", FullName = "U", Status = AccountStatusEnum.Active, TwoFactorEnabled = true, TwoFactorSecret = "enc:v1:S" };
        var (uow, _, _, _) = MockUnitOfWork.Build(accountSeed: new[] { acc });
        _totp.Setup(t => t.VerifyCode("S", "123456", It.IsAny<int>())).Returns(true);

        var resp = await Build(uow).Handle(new RegenerateBackupCodesCommand { AccountId = acc.Id, TotpCode = "123456" }, CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        resp.Data!.BackupCodes.Should().HaveCount(8);
    }

    [Fact]
    public async Task Regen_WrongTotp_Returns422()
    {
        var acc = new Account { Id = Guid.NewGuid(), Email = "u@example.com", PasswordHash = "x", FullName = "U", Status = AccountStatusEnum.Active, TwoFactorEnabled = true, TwoFactorSecret = "S" };
        var (uow, _, _, _) = MockUnitOfWork.Build(accountSeed: new[] { acc });
        _totp.Setup(t => t.VerifyCode(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>())).Returns(false);

        var resp = await Build(uow).Handle(new RegenerateBackupCodesCommand { AccountId = acc.Id, TotpCode = "000000" }, CancellationToken.None);
        resp.StatusCode.Should().Be(422);
    }

    [Fact]
    public async Task Regen_2FAOff_Returns409()
    {
        var acc = new Account { Id = Guid.NewGuid(), Email = "u@example.com", PasswordHash = "x", FullName = "U", Status = AccountStatusEnum.Active, TwoFactorEnabled = false };
        var (uow, _, _, _) = MockUnitOfWork.Build(accountSeed: new[] { acc });

        var resp = await Build(uow).Handle(new RegenerateBackupCodesCommand { AccountId = acc.Id, TotpCode = "123456" }, CancellationToken.None);
        resp.StatusCode.Should().Be(409);
    }
}

// --------- AdminReset2FA ---------
public class AdminReset2FACommandHandlerTests
{
    private readonly Mock<IPublisher> _publisher = MockPublisher.NoOp();

    [Fact]
    public async Task Reset_ActiveAccountWith2FA_ClearsAndAudits()
    {
        var acc = new Account { Id = Guid.NewGuid(), Email = "u@example.com", PasswordHash = "x", FullName = "U", Status = AccountStatusEnum.Active, TwoFactorEnabled = true, TwoFactorSecret = "enc:v1:X", TwoFactorSecretEncryptedAt = DateTime.UtcNow };
        var (uow, _, _, _) = MockUnitOfWork.Build(accountSeed: new[] { acc });
        var handler = new AdminReset2FACommandHandler(uow.Object, _publisher.Object);

        var resp = await handler.Handle(new AdminReset2FACommand { TargetAccountId = acc.Id }, CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        acc.TwoFactorEnabled.Should().BeFalse();
        acc.TwoFactorSecret.Should().BeNull();
        acc.TwoFactorSecretEncryptedAt.Should().BeNull();
        _publisher.Verify(p => p.Publish(
            It.Is<AuditTrailNotification>(n => n.Action == AuditActionEnum.Admin2FAReset && n.TargetAccountId == acc.Id && n.IsSuccess),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Reset_NotFound_Returns404()
    {
        var (uow, _, _, _) = MockUnitOfWork.Build();
        var handler = new AdminReset2FACommandHandler(uow.Object, _publisher.Object);

        var resp = await handler.Handle(new AdminReset2FACommand { TargetAccountId = Guid.NewGuid() }, CancellationToken.None);
        resp.StatusCode.Should().Be(404);
    }
}
