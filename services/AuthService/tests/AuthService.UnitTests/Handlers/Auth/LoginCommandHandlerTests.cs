using AuthService.Application.CQRS.Command.Auth;
using AuthService.Application.CQRS.Handler.Auth;
using AuthService.Application.Interfaces.Helpers;
using AuthService.Domain.Entities;
using AuthService.Domain.Enums;
using AuthService.UnitTests.Helpers;

namespace AuthService.UnitTests.Handlers.Auth;

public class LoginCommandHandlerTests
{
    private readonly Mock<IJwtHelper> _jwt = new();
    private readonly Mock<IPasswordHasher> _hasher = new();

    public LoginCommandHandlerTests()
    {
        _jwt.Setup(j => j.GenerateAccessToken(It.IsAny<Account>(), It.IsAny<IEnumerable<string>>())).ReturnsAsync("access");
        _jwt.Setup(j => j.GenerateRefreshToken()).Returns("refresh");
    }

    private static global::AuthService.Domain.Entities.Account ActiveAccount(string passwordHash = "HASHED")
    {
        return new global::AuthService.Domain.Entities.Account
        {
            Id = Guid.NewGuid(),
            Email = "user@example.com",
            PasswordHash = passwordHash,
            FullName = "User",
            Status = AccountStatusEnum.Active,
            AccountRoles = new List<AccountRole>()
        };
    }

    [Fact]
    public async Task Login_CorrectPassword_IssuesTokens_ResetsCounter()
    {
        var account = ActiveAccount();
        account.FailedLoginAttempts = 2;
        var (uow, accounts, refreshTokens, _, _) = MockUnitOfWork.Build(accountSeed: new[] { account });
        _hasher.Setup(h => h.Verify("correct", "HASHED")).Returns(true);

        var handler = new LoginCommandHandler(uow.Object, _jwt.Object, _hasher.Object);
        var response = await handler.Handle(new LoginCommand
        {
            Email = "user@example.com",
            Password = "correct"
        }, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.StatusCode.Should().Be(200);
        response.Data!.AccessToken.Should().Be("access");
        account.FailedLoginAttempts.Should().Be(0);
        account.LastLoginAt.Should().NotBeNull();
        refreshTokens.Verify(r => r.AddAsync(It.Is<RefreshToken>(rt => rt.AccountId == account.Id)), Times.Once);
    }

    [Fact]
    public async Task Login_WrongPassword_IncrementsCounter_Returns401()
    {
        var account = ActiveAccount();
        var (uow, _, _, _, _) = MockUnitOfWork.Build(accountSeed: new[] { account });
        _hasher.Setup(h => h.Verify(It.IsAny<string>(), It.IsAny<string>())).Returns(false);

        var handler = new LoginCommandHandler(uow.Object, _jwt.Object, _hasher.Object);
        var response = await handler.Handle(new LoginCommand
        {
            Email = "user@example.com",
            Password = "wrong"
        }, CancellationToken.None);

        response.StatusCode.Should().Be(401);
        account.FailedLoginAttempts.Should().Be(1);
    }

    [Fact]
    public async Task Login_FifthWrongPassword_LocksAccount_Returns423()
    {
        var account = ActiveAccount();
        account.FailedLoginAttempts = 4;
        var (uow, _, _, _, _) = MockUnitOfWork.Build(accountSeed: new[] { account });
        _hasher.Setup(h => h.Verify(It.IsAny<string>(), It.IsAny<string>())).Returns(false);

        var handler = new LoginCommandHandler(uow.Object, _jwt.Object, _hasher.Object);
        var response = await handler.Handle(new LoginCommand
        {
            Email = "user@example.com",
            Password = "wrong"
        }, CancellationToken.None);

        response.StatusCode.Should().Be(423);
        account.Status.Should().Be(AccountStatusEnum.Locked);
        account.LockoutEndAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Login_PendingVerificationStatus_Returns403()
    {
        var account = ActiveAccount();
        account.Status = AccountStatusEnum.PendingVerification;
        var (uow, _, _, _, _) = MockUnitOfWork.Build(accountSeed: new[] { account });

        var handler = new LoginCommandHandler(uow.Object, _jwt.Object, _hasher.Object);
        var response = await handler.Handle(new LoginCommand
        {
            Email = "user@example.com",
            Password = "anything"
        }, CancellationToken.None);

        response.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Login_NonExistentEmail_Returns401_NoLeak()
    {
        var (uow, _, _, _, _) = MockUnitOfWork.Build();

        var handler = new LoginCommandHandler(uow.Object, _jwt.Object, _hasher.Object);
        var response = await handler.Handle(new LoginCommand
        {
            Email = "ghost@example.com",
            Password = "anything"
        }, CancellationToken.None);

        response.StatusCode.Should().Be(401);
        // KHÔNG tiết lộ "email không tồn tại" — message giống wrong password
    }
}
