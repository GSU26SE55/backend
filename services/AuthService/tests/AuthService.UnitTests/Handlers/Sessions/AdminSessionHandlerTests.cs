using AuthService.Application.CQRS.Command.Session;
using AuthService.Application.CQRS.Handler.Session;
using AuthService.Application.CQRS.Query.Session;
using AuthService.Domain.Entities;
using AuthService.Domain.Enums;
using AuthService.UnitTests.Helpers;

namespace AuthService.UnitTests.Handlers.Sessions;

public class GetAccountSessionsQueryHandlerTests
{
    [Fact]
    public async Task ActiveOnly_ReturnsActiveSessionsOfTargetUserOnly()
    {
        var targetId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        var tokens = new[]
        {
            new RefreshToken { Id = Guid.NewGuid(), AccountId = targetId, Token = "a", Status = RefreshTokenStatus.Active, IssuedAt = DateTime.UtcNow.AddHours(-1), ExpiredAt = DateTime.UtcNow.AddDays(7) },
            new RefreshToken { Id = Guid.NewGuid(), AccountId = targetId, Token = "b", Status = RefreshTokenStatus.Revoked, IssuedAt = DateTime.UtcNow.AddDays(-1), ExpiredAt = DateTime.UtcNow.AddDays(6) },
            new RefreshToken { Id = Guid.NewGuid(), AccountId = otherId, Token = "c", Status = RefreshTokenStatus.Active, IssuedAt = DateTime.UtcNow, ExpiredAt = DateTime.UtcNow.AddDays(7) }
        };
        var (uow, _, _, _, _) = MockUnitOfWork.Build(tokenSeed: tokens);
        var handler = new GetAccountSessionsQueryHandler(uow.Object);

        var resp = await handler.Handle(new GetAccountSessionsQuery { AccountId = targetId, ActiveOnly = true }, CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        var sessions = resp.Data!;
        sessions.Should().HaveCount(1);
        sessions[0].Status.Should().Be(RefreshTokenStatus.Active);
    }

    [Fact]
    public async Task NoActiveOnly_IncludesAllStatusesOfTarget()
    {
        var targetId = Guid.NewGuid();
        var tokens = new[]
        {
            new RefreshToken { Id = Guid.NewGuid(), AccountId = targetId, Token = "a", Status = RefreshTokenStatus.Active, IssuedAt = DateTime.UtcNow, ExpiredAt = DateTime.UtcNow.AddDays(7) },
            new RefreshToken { Id = Guid.NewGuid(), AccountId = targetId, Token = "b", Status = RefreshTokenStatus.Revoked, IssuedAt = DateTime.UtcNow.AddDays(-1), ExpiredAt = DateTime.UtcNow.AddDays(6) }
        };
        var (uow, _, _, _, _) = MockUnitOfWork.Build(tokenSeed: tokens);
        var handler = new GetAccountSessionsQueryHandler(uow.Object);

        var resp = await handler.Handle(new GetAccountSessionsQuery { AccountId = targetId, ActiveOnly = false }, CancellationToken.None);

        resp.Data!.Should().HaveCount(2);
    }

    [Fact]
    public async Task EmptyAccountId_Returns400()
    {
        var (uow, _, _, _, _) = MockUnitOfWork.Build();
        var handler = new GetAccountSessionsQueryHandler(uow.Object);

        var resp = await handler.Handle(new GetAccountSessionsQuery { AccountId = Guid.Empty }, CancellationToken.None);

        resp.StatusCode.Should().Be(400);
    }
}

public class AdminRevokeAccountSessionsCommandHandlerTests
{
    [Fact]
    public async Task RevokeAll_RevokesActiveTokensOfTargetUser_Only()
    {
        var account = new global::AuthService.Domain.Entities.Account
        {
            Id = Guid.NewGuid(),
            Email = "u@e.com",
            PasswordHash = "x",
            FullName = "U",
            Status = AccountStatusEnum.Active
        };
        var otherUserToken = new RefreshToken
        {
            Id = Guid.NewGuid(),
            AccountId = Guid.NewGuid(),
            Token = "other",
            Status = RefreshTokenStatus.Active,
            IssuedAt = DateTime.UtcNow,
            ExpiredAt = DateTime.UtcNow.AddDays(7)
        };
        var t1 = new RefreshToken { Id = Guid.NewGuid(), AccountId = account.Id, Token = "a", Status = RefreshTokenStatus.Active, IssuedAt = DateTime.UtcNow, ExpiredAt = DateTime.UtcNow.AddDays(7) };
        var t2 = new RefreshToken { Id = Guid.NewGuid(), AccountId = account.Id, Token = "b", Status = RefreshTokenStatus.Active, IssuedAt = DateTime.UtcNow, ExpiredAt = DateTime.UtcNow.AddDays(7) };

        var (uow, _, _, _, _) = MockUnitOfWork.Build(accountSeed: new[] { account }, tokenSeed: new[] { t1, t2, otherUserToken });
        var handler = new AdminRevokeAccountSessionsCommandHandler(uow.Object, MockPublisher.NoOp().Object);

        var resp = await handler.Handle(new AdminRevokeAccountSessionsCommand { AccountId = account.Id, Reason = "Suspected breach" }, CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        resp.Data.Should().Be(2);
        t1.Status.Should().Be(RefreshTokenStatus.Revoked);
        t2.Status.Should().Be(RefreshTokenStatus.Revoked);
        t1.RevokedReason.Should().Contain("Suspected breach");
        otherUserToken.Status.Should().Be(RefreshTokenStatus.Active); // KHÔNG bị thu hồi
    }

    [Fact]
    public async Task AccountNotFound_Returns404()
    {
        var (uow, _, _, _, _) = MockUnitOfWork.Build();
        var handler = new AdminRevokeAccountSessionsCommandHandler(uow.Object, MockPublisher.NoOp().Object);

        var resp = await handler.Handle(new AdminRevokeAccountSessionsCommand { AccountId = Guid.NewGuid() }, CancellationToken.None);

        resp.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task NoActiveTokens_Returns200_WithCount0()
    {
        var account = new global::AuthService.Domain.Entities.Account
        {
            Id = Guid.NewGuid(),
            Email = "u@e.com",
            PasswordHash = "x",
            FullName = "U",
            Status = AccountStatusEnum.Active
        };
        var (uow, _, _, _, _) = MockUnitOfWork.Build(accountSeed: new[] { account });
        var handler = new AdminRevokeAccountSessionsCommandHandler(uow.Object, MockPublisher.NoOp().Object);

        var resp = await handler.Handle(new AdminRevokeAccountSessionsCommand { AccountId = account.Id }, CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        resp.Data.Should().Be(0);
    }
}
