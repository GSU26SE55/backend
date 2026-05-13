using AuthService.Application.CQRS.Command.Auth;
using AuthService.Application.CQRS.Handler.Auth;
using AuthService.Application.Interfaces.Helpers;
using AuthService.Domain.Entities;
using AuthService.Domain.Enums;
using AuthService.UnitTests.Helpers;

namespace AuthService.UnitTests.Handlers.Auth;

public class RefreshTokenCommandHandlerTests
{
    private readonly Mock<IJwtHelper> _jwt = new();

    public RefreshTokenCommandHandlerTests()
    {
        _jwt.Setup(j => j.GenerateAccessToken(It.IsAny<Account>(), It.IsAny<IEnumerable<string>>(), It.IsAny<IEnumerable<string>?>())).ReturnsAsync("new-access");
        _jwt.Setup(j => j.GenerateRefreshToken()).Returns("new-refresh");
    }

    private static (global::AuthService.Domain.Entities.Account account, RefreshToken token) Seed(RefreshTokenStatus status, DateTime? expiredAt = null)
    {
        var account = new global::AuthService.Domain.Entities.Account
        {
            Id = Guid.NewGuid(),
            Email = "rt@example.com",
            PasswordHash = "x",
            FullName = "RT",
            Status = AccountStatusEnum.Active,
            AccountRoles = new List<AccountRole>()
        };
        var token = new RefreshToken
        {
            Id = Guid.NewGuid(),
            AccountId = account.Id,
            Token = "old-refresh",
            IssuedAt = DateTime.UtcNow.AddDays(-1),
            ExpiredAt = expiredAt ?? DateTime.UtcNow.AddDays(6),
            Status = status,
            Account = account
        };
        return (account, token);
    }

    [Fact]
    public async Task Rotate_ActiveToken_IssuesNewPair_MarksOldAsUsed()
    {
        var (account, oldToken) = Seed(RefreshTokenStatus.Active);
        var (uow, _, refreshTokens, _, _) = MockUnitOfWork.Build(accountSeed: new[] { account }, tokenSeed: new[] { oldToken });

        var handler = new RefreshTokenCommandHandler(uow.Object, _jwt.Object, MockPublisher.NoOp().Object);
        var response = await handler.Handle(new RefreshTokenCommand { RefreshToken = "old-refresh" }, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data!.AccessToken.Should().Be("new-access");
        response.Data.RefreshToken.Should().Be("new-refresh");

        oldToken.Status.Should().Be(RefreshTokenStatus.Used);
        oldToken.UsedAt.Should().NotBeNull();
        oldToken.ReplacedByToken.Should().Be("new-refresh");

        refreshTokens.Verify(r => r.AddAsync(It.Is<RefreshToken>(rt =>
            rt.Token == "new-refresh" &&
            rt.AccountId == account.Id &&
            rt.Status == RefreshTokenStatus.Active
        )), Times.Once);
    }

    [Fact]
    public async Task Rotate_AlreadyUsedToken_TriggersReuseAttack_RevokesAllActiveTokens()
    {
        var (account, usedToken) = Seed(RefreshTokenStatus.Used);
        var otherActive = new RefreshToken
        {
            Id = Guid.NewGuid(),
            AccountId = account.Id,
            Token = "other-active",
            Status = RefreshTokenStatus.Active,
            IssuedAt = DateTime.UtcNow,
            ExpiredAt = DateTime.UtcNow.AddDays(7),
            Account = account
        };
        var (uow, _, refreshTokens, _, _) = MockUnitOfWork.Build(accountSeed: new[] { account }, tokenSeed: new[] { usedToken, otherActive });

        var handler = new RefreshTokenCommandHandler(uow.Object, _jwt.Object, MockPublisher.NoOp().Object);
        var response = await handler.Handle(new RefreshTokenCommand { RefreshToken = "old-refresh" }, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.StatusCode.Should().Be(401);
        response.Message.Should().Contain("tái sử dụng");

        otherActive.Status.Should().Be(RefreshTokenStatus.Revoked);
        otherActive.RevokedAt.Should().NotBeNull();
        otherActive.RevokedReason.Should().Contain("reuse");
        refreshTokens.Verify(r => r.AddAsync(It.IsAny<RefreshToken>()), Times.Never);
    }

    [Fact]
    public async Task Rotate_RevokedToken_Returns401()
    {
        var (account, revoked) = Seed(RefreshTokenStatus.Revoked);
        var (uow, _, _, _, _) = MockUnitOfWork.Build(accountSeed: new[] { account }, tokenSeed: new[] { revoked });

        var handler = new RefreshTokenCommandHandler(uow.Object, _jwt.Object, MockPublisher.NoOp().Object);
        var response = await handler.Handle(new RefreshTokenCommand { RefreshToken = "old-refresh" }, CancellationToken.None);

        response.StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task Rotate_ExpiredToken_MarksExpired_Returns401()
    {
        var (account, expired) = Seed(RefreshTokenStatus.Active, expiredAt: DateTime.UtcNow.AddSeconds(-1));
        var (uow, _, _, _, _) = MockUnitOfWork.Build(accountSeed: new[] { account }, tokenSeed: new[] { expired });

        var handler = new RefreshTokenCommandHandler(uow.Object, _jwt.Object, MockPublisher.NoOp().Object);
        var response = await handler.Handle(new RefreshTokenCommand { RefreshToken = "old-refresh" }, CancellationToken.None);

        response.StatusCode.Should().Be(401);
        expired.Status.Should().Be(RefreshTokenStatus.Expired);
    }

    [Fact]
    public async Task Rotate_NonExistentToken_Returns401()
    {
        var (uow, _, _, _, _) = MockUnitOfWork.Build();

        var handler = new RefreshTokenCommandHandler(uow.Object, _jwt.Object, MockPublisher.NoOp().Object);
        var response = await handler.Handle(new RefreshTokenCommand { RefreshToken = "ghost-token" }, CancellationToken.None);

        response.StatusCode.Should().Be(401);
    }
}
