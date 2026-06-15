using AuthService.Application.CQRS.Command.Account;
using AuthService.Application.CQRS.Handler.Account;
using AuthService.Application.Interfaces.Helpers;
using AuthService.Domain.Entities;
using AuthService.Domain.Enums;
using AuthService.UnitTests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using SharedContracts.Events;
using SharedContracts.Interfaces;

namespace AuthService.UnitTests.Handlers.Accounts;

public class ChangeEmailCommandHandlerTests
{
    private readonly Mock<IPasswordHasher> _hasher = new();
    private readonly Mock<IMessageProducerService> _producer = new();

    [Fact]
    public async Task ChangeEmail_HappyPath_StoresPendingEmail_PublishesOtp()
    {
        var account = new global::AuthService.Domain.Entities.Account
        {
            Id = Guid.NewGuid(),
            Email = "old@example.com",
            PasswordHash = "old-hash",
            FullName = "U",
            Status = AccountStatusEnum.Active
        };
        _hasher.Setup(h => h.Verify("right-password", "old-hash")).Returns(true);
        var (uow, accounts, _, _) = MockUnitOfWork.Build(accountSeed: new[] { account });

        var handler = new ChangeEmailCommandHandler(uow.Object, _hasher.Object, _producer.Object, NullLogger<ChangeEmailCommandHandler>.Instance);
        var resp = await handler.Handle(new ChangeEmailCommand
        {
            AccountId = account.Id,
            NewEmail = "  NEW@Example.com  ",
            CurrentPassword = "right-password"
        }, CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        account.PendingEmail.Should().Be("new@example.com");
        account.OtpPurpose.Should().Be(OtpPurposeEnum.EmailChange);
        account.OtpCode!.Length.Should().Be(6);
        account.Email.Should().Be("old@example.com");  // chưa đổi
        _producer.Verify(p => p.PublishAsync(It.Is<SendEmailChangeOtpEvent>(e => e.ToNewEmail == "new@example.com"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ChangeEmail_WrongPassword_Returns400()
    {
        var account = new global::AuthService.Domain.Entities.Account
        {
            Id = Guid.NewGuid(),
            Email = "u@example.com",
            PasswordHash = "x",
            FullName = "U",
            Status = AccountStatusEnum.Active
        };
        _hasher.Setup(h => h.Verify(It.IsAny<string>(), It.IsAny<string>())).Returns(false);
        var (uow, accounts, _, _) = MockUnitOfWork.Build(accountSeed: new[] { account });

        var handler = new ChangeEmailCommandHandler(uow.Object, _hasher.Object, _producer.Object, NullLogger<ChangeEmailCommandHandler>.Instance);
        var resp = await handler.Handle(new ChangeEmailCommand { AccountId = account.Id, NewEmail = "new@example.com", CurrentPassword = "wrong" }, CancellationToken.None);

        resp.StatusCode.Should().Be(401);
        account.PendingEmail.Should().BeNull();
    }

    [Fact]
    public async Task ChangeEmail_SameAsCurrentEmail_Returns400()
    {
        var account = new global::AuthService.Domain.Entities.Account
        {
            Id = Guid.NewGuid(),
            Email = "u@example.com",
            PasswordHash = "h",
            FullName = "U",
            Status = AccountStatusEnum.Active
        };
        _hasher.Setup(h => h.Verify(It.IsAny<string>(), It.IsAny<string>())).Returns(true);
        var (uow, accounts, _, _) = MockUnitOfWork.Build(accountSeed: new[] { account });

        var handler = new ChangeEmailCommandHandler(uow.Object, _hasher.Object, _producer.Object, NullLogger<ChangeEmailCommandHandler>.Instance);
        var resp = await handler.Handle(new ChangeEmailCommand { AccountId = account.Id, NewEmail = "U@Example.com", CurrentPassword = "p" }, CancellationToken.None);

        resp.StatusCode.Should().Be(422);
    }

    [Fact]
    public async Task ChangeEmail_NewEmailAlreadyTaken_Returns409()
    {
        var account = new global::AuthService.Domain.Entities.Account
        {
            Id = Guid.NewGuid(),
            Email = "old@example.com",
            PasswordHash = "h",
            FullName = "U",
            Status = AccountStatusEnum.Active
        };
        var taken = new global::AuthService.Domain.Entities.Account
        {
            Id = Guid.NewGuid(),
            Email = "new@example.com",
            PasswordHash = "x",
            FullName = "Other",
            Status = AccountStatusEnum.Active
        };
        _hasher.Setup(h => h.Verify(It.IsAny<string>(), It.IsAny<string>())).Returns(true);
        var (uow, accounts, _, _) = MockUnitOfWork.Build(accountSeed: new[] { account, taken });
        accounts.Setup(r => r.GetByIdAsync(account.Id)).ReturnsAsync(account);

        var handler = new ChangeEmailCommandHandler(uow.Object, _hasher.Object, _producer.Object, NullLogger<ChangeEmailCommandHandler>.Instance);
        var resp = await handler.Handle(new ChangeEmailCommand { AccountId = account.Id, NewEmail = "new@example.com", CurrentPassword = "p" }, CancellationToken.None);

        resp.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task ChangeEmail_AccountNotFound_Returns404()
    {
        var (uow, accounts, _, _) = MockUnitOfWork.Build();
        accounts.Setup(r => r.GetByIdAsync(It.IsAny<Guid>())).ReturnsAsync((global::AuthService.Domain.Entities.Account?)null);

        var handler = new ChangeEmailCommandHandler(uow.Object, _hasher.Object, _producer.Object, NullLogger<ChangeEmailCommandHandler>.Instance);
        var resp = await handler.Handle(new ChangeEmailCommand { AccountId = Guid.NewGuid(), NewEmail = "x@e.com", CurrentPassword = "p" }, CancellationToken.None);

        resp.StatusCode.Should().Be(404);
    }
}

public class ConfirmEmailChangeCommandHandlerTests
{
    private static global::AuthService.Domain.Entities.Account WithPending(string otp = "123456", DateTime? expired = null) => new()
    {
        Id = Guid.NewGuid(),
        Email = "old@example.com",
        PendingEmail = "new@example.com",
        PasswordHash = "x",
        FullName = "U",
        Status = AccountStatusEnum.Active,
        EmailConfirmed = true,
        OtpCode = otp,
        OtpExpiredAt = expired ?? DateTime.UtcNow.AddMinutes(8),
        OtpPurpose = OtpPurposeEnum.EmailChange
    };

    [Fact]
    public async Task Confirm_CorrectOtp_SwapsEmail_RevokesAllSessions()
    {
        var account = WithPending();
        var token = new RefreshToken
        {
            Id = Guid.NewGuid(),
            AccountId = account.Id,
            Token = "rt",
            Status = RefreshTokenStatus.Active,
            IssuedAt = DateTime.UtcNow,
            ExpiredAt = DateTime.UtcNow.AddDays(7)
        };
        var (uow, accounts, _, _) = MockUnitOfWork.Build(accountSeed: new[] { account }, tokenSeed: new[] { token });
        var handler = new ConfirmEmailChangeCommandHandler(uow.Object);

        var resp = await handler.Handle(new ConfirmEmailChangeCommand { AccountId = account.Id, Otp = "123456" }, CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        account.Email.Should().Be("new@example.com");
        account.PendingEmail.Should().BeNull();
        account.EmailConfirmed.Should().BeTrue();
        account.OtpCode.Should().BeNull();
        account.OtpPurpose.Should().BeNull();
        token.Status.Should().Be(RefreshTokenStatus.Revoked);
        token.RevokedReason.Should().Be("Email changed");
    }

    [Fact]
    public async Task Confirm_NoPendingChange_Returns400()
    {
        var account = WithPending();
        account.PendingEmail = null;
        account.OtpPurpose = OtpPurposeEnum.Register;
        var (uow, accounts, _, _) = MockUnitOfWork.Build(accountSeed: new[] { account });
        var handler = new ConfirmEmailChangeCommandHandler(uow.Object);

        var resp = await handler.Handle(new ConfirmEmailChangeCommand { AccountId = account.Id, Otp = "123456" }, CancellationToken.None);

        resp.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Confirm_OtpExpired_Returns400()
    {
        var account = WithPending(expired: DateTime.UtcNow.AddMinutes(-1));
        var (uow, accounts, _, _) = MockUnitOfWork.Build(accountSeed: new[] { account });
        var handler = new ConfirmEmailChangeCommandHandler(uow.Object);

        var resp = await handler.Handle(new ConfirmEmailChangeCommand { AccountId = account.Id, Otp = "123456" }, CancellationToken.None);

        resp.StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task Confirm_WrongOtp_IncrementsCounter_Returns401()
    {
        var account = WithPending(otp: "999999");
        var (uow, accounts, _, _) = MockUnitOfWork.Build(accountSeed: new[] { account });
        var handler = new ConfirmEmailChangeCommandHandler(uow.Object);

        var resp = await handler.Handle(new ConfirmEmailChangeCommand { AccountId = account.Id, Otp = "111111" }, CancellationToken.None);

        resp.StatusCode.Should().Be(401);
        account.FailedLoginAttempts.Should().Be(1);
    }

    [Fact]
    public async Task Confirm_PendingEmailTakenInRace_Returns409_ClearsPending()
    {
        var account = WithPending();
        var stealer = new global::AuthService.Domain.Entities.Account
        {
            Id = Guid.NewGuid(),
            Email = "new@example.com",
            PasswordHash = "x",
            FullName = "Stealer",
            Status = AccountStatusEnum.Active
        };
        var (uow, accounts, _, _) = MockUnitOfWork.Build(accountSeed: new[] { account, stealer });
        var handler = new ConfirmEmailChangeCommandHandler(uow.Object);

        var resp = await handler.Handle(new ConfirmEmailChangeCommand { AccountId = account.Id, Otp = "123456" }, CancellationToken.None);

        resp.StatusCode.Should().Be(409);
        account.Email.Should().Be("old@example.com");
        account.PendingEmail.Should().BeNull();
    }

    [Fact]
    public async Task Confirm_AccountNotFound_Returns404()
    {
        var (uow, accounts, _, _) = MockUnitOfWork.Build();
        accounts.Setup(r => r.GetByIdAsync(It.IsAny<Guid>())).ReturnsAsync((global::AuthService.Domain.Entities.Account?)null);
        var handler = new ConfirmEmailChangeCommandHandler(uow.Object);

        var resp = await handler.Handle(new ConfirmEmailChangeCommand { AccountId = Guid.NewGuid(), Otp = "123456" }, CancellationToken.None);

        resp.StatusCode.Should().Be(404);
    }
}
