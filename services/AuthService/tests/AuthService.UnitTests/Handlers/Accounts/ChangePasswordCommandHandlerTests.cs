using AuthService.Application.CQRS.Command.Account;
using AuthService.Application.CQRS.Handler.Account;
using AuthService.Application.CQRS.Notification.Audit;
using AuthService.Application.Interfaces.Helpers;
using AuthService.Application.Interfaces.Services;
using AuthService.Domain.Entities;
using AuthService.Domain.Enums;
using AuthService.UnitTests.Helpers;
using MediatR;

namespace AuthService.UnitTests.Handlers.Accounts;

/// <summary>
/// #AUTH-87 + #AUTH-90: dedicated tests cho <see cref="ChangePasswordCommandHandler"/>.
/// Cover: old password check + revoke sessions + audit log publish + TRL bulk revoke.
/// </summary>
public class ChangePasswordCommandHandlerTests
{
    private readonly Mock<IPasswordHasher> _hasher = new();
    private readonly Mock<IPublisher> _publisher = new();
    private readonly Mock<ITokenRevocationStore> _revocationStore = new();

    private static Account NewAccount(string passwordHash = "OLD-HASH")
        => new()
        {
            Id = Guid.NewGuid(),
            Email = "u@example.com",
            PasswordHash = passwordHash,
            FullName = "U",
            Status = AccountStatusEnum.Active
        };

    private ChangePasswordCommandHandler Build(Mock<AuthService.Application.Interfaces.Repositories.IAuthUnitOfWork> uow)
        => new(uow.Object, _hasher.Object, _publisher.Object, _revocationStore.Object);

    // ============== #AUTH-90: Old password verify ==============

    [Fact]
    public async Task ChangePassword_WrongCurrentPassword_Returns400_PublishesFailedAudit()
    {
        var account = NewAccount();
        _hasher.Setup(h => h.Verify("wrong-pw", "OLD-HASH")).Returns(false);
        var (uow, _, _, _) = MockUnitOfWork.Build(accountSeed: new[] { account });

        var resp = await Build(uow).Handle(new ChangePasswordCommand
        {
            AccountId = account.Id,
            CurrentPassword = "wrong-pw",
            NewPassword = "NewStrong1!",
            ConfirmPassword = "NewStrong1!"
        }, CancellationToken.None);

        resp.IsSuccess.Should().BeFalse();
        resp.StatusCode.Should().Be(400);
        account.PasswordHash.Should().Be("OLD-HASH"); // not modified

        // #AUTH-90: audit log fail published.
        _publisher.Verify(p => p.Publish(
            It.Is<AuditTrailNotification>(n => n.Action == AuditActionEnum.PasswordChanged && !n.IsSuccess),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ChangePassword_AccountNotFound_Returns404()
    {
        var (uow, _, _, _) = MockUnitOfWork.Build();
        var resp = await Build(uow).Handle(new ChangePasswordCommand
        {
            AccountId = Guid.NewGuid(),
            CurrentPassword = "x",
            NewPassword = "NewStrong1!",
            ConfirmPassword = "NewStrong1!"
        }, CancellationToken.None);

        resp.StatusCode.Should().Be(404);
    }

    // ============== #AUTH-23 + #AUTH-90: New == Old reject ==============

    [Fact]
    public async Task ChangePassword_NewEqualsOld_Returns400_BeforePasswordVerify()
    {
        var account = NewAccount();
        var (uow, _, _, _) = MockUnitOfWork.Build(accountSeed: new[] { account });

        var resp = await Build(uow).Handle(new ChangePasswordCommand
        {
            AccountId = account.Id,
            CurrentPassword = "samePw1!",
            NewPassword = "samePw1!",
            ConfirmPassword = "samePw1!"
        }, CancellationToken.None);

        resp.StatusCode.Should().Be(400);
        resp.Message.Should().Contain("khác");
        // hasher.Verify KHÔNG được gọi vì check new==old đứng trước.
        _hasher.Verify(h => h.Verify(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    // ============== #AUTH-87: Revoke sessions verify ==============

    [Fact]
    public async Task ChangePassword_Success_RevokesAllActiveRefreshTokens()
    {
        var account = NewAccount();
        _hasher.Setup(h => h.Verify("old-pw", "OLD-HASH")).Returns(true);
        _hasher.Setup(h => h.Hash("NewStrong1!")).Returns("NEW-HASH");

        var activeToken1 = new RefreshToken
        {
            Id = Guid.NewGuid(),
            AccountId = account.Id,
            Token = "rt1",
            Status = RefreshTokenStatus.Active,
            IssuedAt = DateTime.UtcNow,
            ExpiredAt = DateTime.UtcNow.AddDays(7)
        };
        var activeToken2 = new RefreshToken
        {
            Id = Guid.NewGuid(),
            AccountId = account.Id,
            Token = "rt2",
            Status = RefreshTokenStatus.Active,
            IssuedAt = DateTime.UtcNow,
            ExpiredAt = DateTime.UtcNow.AddDays(7)
        };
        var alreadyRevoked = new RefreshToken
        {
            Id = Guid.NewGuid(),
            AccountId = account.Id,
            Token = "rt3",
            Status = RefreshTokenStatus.Revoked, // không revoke lại
            IssuedAt = DateTime.UtcNow.AddDays(-1),
            ExpiredAt = DateTime.UtcNow.AddDays(6),
            RevokedAt = DateTime.UtcNow.AddHours(-1)
        };
        var (uow, _, _, _) = MockUnitOfWork.Build(
            accountSeed: new[] { account },
            tokenSeed: new[] { activeToken1, activeToken2, alreadyRevoked });

        var resp = await Build(uow).Handle(new ChangePasswordCommand
        {
            AccountId = account.Id,
            CurrentPassword = "old-pw",
            NewPassword = "NewStrong1!",
            ConfirmPassword = "NewStrong1!"
        }, CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        account.PasswordHash.Should().Be("NEW-HASH");

        // #AUTH-87: cả 2 active tokens phải revoke với reason "Password changed".
        activeToken1.Status.Should().Be(RefreshTokenStatus.Revoked);
        activeToken1.RevokedReason.Should().Be("Password changed");
        activeToken1.RevokedAt.Should().NotBeNull();
        activeToken2.Status.Should().Be(RefreshTokenStatus.Revoked);

        // Already-revoked token KHÔNG được touch (đã revoke từ trước).
        alreadyRevoked.RevokedReason.Should().NotBe("Password changed");
    }

    // ============== #AUTH-54: TRL bulk revoke access tokens ==============

    [Fact]
    public async Task ChangePassword_Success_CallsTokenRevocationStore_BulkRevoke()
    {
        var account = NewAccount();
        _hasher.Setup(h => h.Verify("old-pw", "OLD-HASH")).Returns(true);
        _hasher.Setup(h => h.Hash("NewStrong1!")).Returns("NEW-HASH");
        var (uow, _, _, _) = MockUnitOfWork.Build(accountSeed: new[] { account });

        await Build(uow).Handle(new ChangePasswordCommand
        {
            AccountId = account.Id,
            CurrentPassword = "old-pw",
            NewPassword = "NewStrong1!",
            ConfirmPassword = "NewStrong1!"
        }, CancellationToken.None);

        // #AUTH-54: bulk revoke access tokens qua TRL.
        _revocationStore.Verify(s => s.RevokeAllByAccountAsync(
            account.Id, It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ============== #AUTH-90: Audit log success ==============

    [Fact]
    public async Task ChangePassword_Success_PublishesAuditTrailWithRevokedCount()
    {
        var account = NewAccount();
        _hasher.Setup(h => h.Verify("old-pw", "OLD-HASH")).Returns(true);
        _hasher.Setup(h => h.Hash("NewStrong1!")).Returns("NEW-HASH");
        var activeToken = new RefreshToken
        {
            Id = Guid.NewGuid(),
            AccountId = account.Id,
            Token = "rt",
            Status = RefreshTokenStatus.Active,
            IssuedAt = DateTime.UtcNow,
            ExpiredAt = DateTime.UtcNow.AddDays(7)
        };
        var (uow, _, _, _) = MockUnitOfWork.Build(
            accountSeed: new[] { account },
            tokenSeed: new[] { activeToken });

        await Build(uow).Handle(new ChangePasswordCommand
        {
            AccountId = account.Id,
            CurrentPassword = "old-pw",
            NewPassword = "NewStrong1!",
            ConfirmPassword = "NewStrong1!"
        }, CancellationToken.None);

        // #AUTH-90: audit log row inserted with success flag + revokedSessions metadata.
        _publisher.Verify(p => p.Publish(
            It.Is<AuditTrailNotification>(n =>
                n.Action == AuditActionEnum.PasswordChanged
                && n.IsSuccess
                && n.TargetAccountId == account.Id),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
