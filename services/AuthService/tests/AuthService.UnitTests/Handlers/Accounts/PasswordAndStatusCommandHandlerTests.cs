using AuthService.Application.CQRS.Command.Account;
using AuthService.Application.CQRS.Handler.Account;
using AuthService.Application.Interfaces.Helpers;
using AuthService.Domain.Entities;
using AuthService.Domain.Enums;
using AuthService.UnitTests.Helpers;
using MediatR;

namespace AuthService.UnitTests.Handlers.Accounts;

public class ChangePasswordCommandHandlerTests
{
    private readonly Mock<IPasswordHasher> _hasher = new();
    private readonly Mock<IPublisher> _publisher = MockPublisher.NoOp();

    [Fact]
    public async Task ChangePassword_CorrectCurrent_UpdatesHash_RevokesAllSessions()
    {
        var account = new global::AuthService.Domain.Entities.Account
        {
            Id = Guid.NewGuid(),
            Email = "u@example.com",
            PasswordHash = "old",
            FullName = "U",
            Status = AccountStatusEnum.Active
        };
        var token = new RefreshToken
        {
            Id = Guid.NewGuid(),
            AccountId = account.Id,
            Token = "rt",
            Status = RefreshTokenStatus.Active,
            IssuedAt = DateTime.UtcNow,
            ExpiredAt = DateTime.UtcNow.AddDays(7)
        };
        _hasher.Setup(h => h.Verify("old-pw", "old")).Returns(true);
        _hasher.Setup(h => h.Hash("new-pw")).Returns("NEW-HASH");
        var (uow, accounts, _, _, _) = MockUnitOfWork.Build(accountSeed: new[] { account }, tokenSeed: new[] { token });
        accounts.Setup(r => r.GetByIdAsync(account.Id)).ReturnsAsync(account);

        var handler = new ChangePasswordCommandHandler(uow.Object, _hasher.Object, _publisher.Object);
        var resp = await handler.Handle(new ChangePasswordCommand
        {
            AccountId = account.Id,
            CurrentPassword = "old-pw",
            NewPassword = "new-pw",
            ConfirmPassword = "new-pw"
        }, CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        account.PasswordHash.Should().Be("NEW-HASH");
        token.Status.Should().Be(RefreshTokenStatus.Revoked);
        token.RevokedReason.Should().Be("Password changed");
    }

    [Fact]
    public async Task ChangePassword_WrongCurrent_Returns400()
    {
        var account = new global::AuthService.Domain.Entities.Account
        {
            Id = Guid.NewGuid(),
            Email = "u@example.com",
            PasswordHash = "old",
            FullName = "U",
            Status = AccountStatusEnum.Active
        };
        _hasher.Setup(h => h.Verify(It.IsAny<string>(), It.IsAny<string>())).Returns(false);
        var (uow, accounts, _, _, _) = MockUnitOfWork.Build();
        accounts.Setup(r => r.GetByIdAsync(account.Id)).ReturnsAsync(account);

        var handler = new ChangePasswordCommandHandler(uow.Object, _hasher.Object, _publisher.Object);
        var resp = await handler.Handle(new ChangePasswordCommand
        {
            AccountId = account.Id,
            CurrentPassword = "wrong",
            NewPassword = "new",
            ConfirmPassword = "new"
        }, CancellationToken.None);

        resp.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task ChangePassword_AccountNotFound_Returns404()
    {
        var (uow, accounts, _, _, _) = MockUnitOfWork.Build();
        accounts.Setup(r => r.GetByIdAsync(It.IsAny<Guid>())).ReturnsAsync((global::AuthService.Domain.Entities.Account?)null);
        var handler = new ChangePasswordCommandHandler(uow.Object, _hasher.Object, _publisher.Object);

        var resp = await handler.Handle(new ChangePasswordCommand
        {
            AccountId = Guid.NewGuid(),
            CurrentPassword = "x",
            NewPassword = "y",
            ConfirmPassword = "y"
        }, CancellationToken.None);

        resp.StatusCode.Should().Be(404);
    }
}

public class ChangeAccountStatusCommandHandlerTests
{
    private readonly Mock<IPublisher> _publisher = MockPublisher.NoOp();

    private static global::AuthService.Domain.Entities.Account WithStatus(AccountStatusEnum status, int failed = 3) => new()
    {
        Id = Guid.NewGuid(),
        Email = "u@example.com",
        PasswordHash = "x",
        FullName = "U",
        Status = status,
        FailedLoginAttempts = failed,
        LockoutEndAt = DateTime.UtcNow.AddMinutes(10)
    };

    [Fact]
    public async Task Change_ToActive_ResetsFailedAttempts_AndLockoutEnd()
    {
        var account = WithStatus(AccountStatusEnum.Locked);
        var (uow, accounts, _, _, _) = MockUnitOfWork.Build();
        accounts.Setup(r => r.GetByIdAsync(account.Id)).ReturnsAsync(account);
        var handler = new ChangeAccountStatusCommandHandler(uow.Object, _publisher.Object);

        var resp = await handler.Handle(new ChangeAccountStatusCommand { Id = account.Id, Status = AccountStatusEnum.Active }, CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        account.Status.Should().Be(AccountStatusEnum.Active);
        account.FailedLoginAttempts.Should().Be(0);
        account.LockoutEndAt.Should().BeNull();
    }

    [Fact]
    public async Task Change_ToLocked_CascadesRevokeAllTokens()
    {
        var account = WithStatus(AccountStatusEnum.Active);
        var token = new RefreshToken
        {
            Id = Guid.NewGuid(),
            AccountId = account.Id,
            Token = "rt",
            Status = RefreshTokenStatus.Active,
            IssuedAt = DateTime.UtcNow,
            ExpiredAt = DateTime.UtcNow.AddDays(7)
        };
        var (uow, accounts, _, _, _) = MockUnitOfWork.Build(tokenSeed: new[] { token });
        accounts.Setup(r => r.GetByIdAsync(account.Id)).ReturnsAsync(account);
        var handler = new ChangeAccountStatusCommandHandler(uow.Object, _publisher.Object);

        var resp = await handler.Handle(new ChangeAccountStatusCommand { Id = account.Id, Status = AccountStatusEnum.Locked, Reason = "violation" }, CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        token.Status.Should().Be(RefreshTokenStatus.Revoked);
        token.RevokedReason.Should().Contain("Locked");
    }

    [Fact]
    public async Task Change_SameStatus_NoOp_Returns200()
    {
        var account = WithStatus(AccountStatusEnum.Active);
        var (uow, accounts, _, _, _) = MockUnitOfWork.Build();
        accounts.Setup(r => r.GetByIdAsync(account.Id)).ReturnsAsync(account);
        var handler = new ChangeAccountStatusCommandHandler(uow.Object, _publisher.Object);

        var resp = await handler.Handle(new ChangeAccountStatusCommand { Id = account.Id, Status = AccountStatusEnum.Active }, CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        resp.Message.Should().Contain("không thay đổi");
    }

    [Fact]
    public async Task Change_NotFound_Returns404()
    {
        var (uow, accounts, _, _, _) = MockUnitOfWork.Build();
        accounts.Setup(r => r.GetByIdAsync(It.IsAny<Guid>())).ReturnsAsync((global::AuthService.Domain.Entities.Account?)null);
        var handler = new ChangeAccountStatusCommandHandler(uow.Object, _publisher.Object);

        var resp = await handler.Handle(new ChangeAccountStatusCommand { Id = Guid.NewGuid(), Status = AccountStatusEnum.Active }, CancellationToken.None);

        resp.StatusCode.Should().Be(404);
    }
}

public class DeleteAccountCommandHandlerTests
{
    [Fact]
    public async Task Delete_ExistingAccount_SoftDeletes_RevokesTokens()
    {
        var account = new global::AuthService.Domain.Entities.Account
        {
            Id = Guid.NewGuid(),
            Email = "u@example.com",
            PasswordHash = "x",
            FullName = "U",
            Status = AccountStatusEnum.Active
        };
        var token = new RefreshToken
        {
            Id = Guid.NewGuid(),
            AccountId = account.Id,
            Token = "rt",
            Status = RefreshTokenStatus.Active,
            IssuedAt = DateTime.UtcNow,
            ExpiredAt = DateTime.UtcNow.AddDays(7)
        };
        var (uow, accounts, _, _, _) = MockUnitOfWork.Build(tokenSeed: new[] { token });
        accounts.Setup(r => r.GetByIdAsync(account.Id)).ReturnsAsync(account);
        var handler = new DeleteAccountCommandHandler(uow.Object, new Mock<IMessageProducerService>().Object);

        var resp = await handler.Handle(new DeleteAccountCommand { Id = account.Id }, CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        accounts.Verify(r => r.DeleteAsync(account), Times.Once);
        token.Status.Should().Be(RefreshTokenStatus.Revoked);
        token.RevokedReason.Should().Be("Account deleted");
    }

    [Fact]
    public async Task Delete_NotFound_Returns404()
    {
        var (uow, accounts, _, _, _) = MockUnitOfWork.Build();
        accounts.Setup(r => r.GetByIdAsync(It.IsAny<Guid>())).ReturnsAsync((global::AuthService.Domain.Entities.Account?)null);
        var handler = new DeleteAccountCommandHandler(uow.Object, new Mock<IMessageProducerService>().Object);

        var resp = await handler.Handle(new DeleteAccountCommand { Id = Guid.NewGuid() }, CancellationToken.None);

        resp.StatusCode.Should().Be(404);
    }
}
