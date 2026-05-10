using AuthService.Application.CQRS.Command.Auth;
using AuthService.Application.CQRS.Handler.Auth;
using AuthService.Domain.Entities;
using AuthService.Domain.Enums;
using AuthService.UnitTests.Helpers;

namespace AuthService.UnitTests.Handlers.Auth;

public class LogoutCommandHandlerTests
{
    [Fact]
    public async Task Logout_ActiveToken_Revokes()
    {
        var token = new RefreshToken
        {
            Id = Guid.NewGuid(),
            AccountId = Guid.NewGuid(),
            Token = "rt-abc",
            Status = RefreshTokenStatus.Active,
            IssuedAt = DateTime.UtcNow,
            ExpiredAt = DateTime.UtcNow.AddDays(7)
        };
        var (uow, _, _, _, _) = MockUnitOfWork.Build(tokenSeed: new[] { token });
        var handler = new LogoutCommandHandler(uow.Object);

        var resp = await handler.Handle(new LogoutCommand { RefreshToken = "rt-abc" }, CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        token.Status.Should().Be(RefreshTokenStatus.Revoked);
        token.RevokedReason.Should().Be("UserLogout");
        token.RevokedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Logout_TokenNotFound_ReturnsAlreadyInactive()
    {
        var (uow, _, _, _, _) = MockUnitOfWork.Build();
        var handler = new LogoutCommandHandler(uow.Object);

        var resp = await handler.Handle(new LogoutCommand { RefreshToken = "ghost" }, CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        resp.Data.Should().Be("AlreadyInactive");
    }

    [Fact]
    public async Task Logout_AlreadyRevokedToken_NoOp()
    {
        var token = new RefreshToken
        {
            Id = Guid.NewGuid(),
            AccountId = Guid.NewGuid(),
            Token = "rt-revoked",
            Status = RefreshTokenStatus.Revoked,
            IssuedAt = DateTime.UtcNow,
            ExpiredAt = DateTime.UtcNow.AddDays(7)
        };
        var (uow, _, _, _, _) = MockUnitOfWork.Build(tokenSeed: new[] { token });
        var handler = new LogoutCommandHandler(uow.Object);

        var resp = await handler.Handle(new LogoutCommand { RefreshToken = "rt-revoked" }, CancellationToken.None);

        resp.Data.Should().Be("AlreadyInactive");
    }
}
