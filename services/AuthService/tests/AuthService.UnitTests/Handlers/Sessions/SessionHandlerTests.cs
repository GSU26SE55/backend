using AuthService.Application.CQRS.Command.Session;
using AuthService.Application.CQRS.Handler.Session;
using AuthService.Application.CQRS.Query.Session;
using AuthService.Domain.Entities;
using AuthService.Domain.Enums;
using AuthService.UnitTests.Helpers;
using SharedInfrastructure.Services;

namespace AuthService.UnitTests.Handlers.Sessions;

public class GetMySessionsQueryHandlerTests
{
    private readonly Mock<ICurrentUserService> _currentUser = new();

    [Fact]
    public async Task ActiveOnly_FiltersInactiveSessions()
    {
        var userId = Guid.NewGuid();
        _currentUser.SetupGet(c => c.UserId).Returns(userId.ToString());
        var tokens = new[]
        {
            new RefreshToken { Id = Guid.NewGuid(), AccountId = userId, Token = "a", Status = RefreshTokenStatus.Active, IssuedAt = DateTime.UtcNow.AddHours(-1), ExpiredAt = DateTime.UtcNow.AddDays(7) },
            new RefreshToken { Id = Guid.NewGuid(), AccountId = userId, Token = "b", Status = RefreshTokenStatus.Revoked, IssuedAt = DateTime.UtcNow.AddDays(-1), ExpiredAt = DateTime.UtcNow.AddDays(6) },
            new RefreshToken { Id = Guid.NewGuid(), AccountId = Guid.NewGuid(), Token = "c", Status = RefreshTokenStatus.Active, IssuedAt = DateTime.UtcNow, ExpiredAt = DateTime.UtcNow.AddDays(7) }
        };
        var (uow, _, _, _) = MockUnitOfWork.Build(tokenSeed: tokens);
        var handler = new GetMySessionsQueryHandler(uow.Object, _currentUser.Object);

        var resp = await handler.Handle(new GetMySessionsQuery { ActiveOnly = true }, CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        var sessions = resp.Data!;
        sessions.Should().HaveCount(1);
        sessions[0].Status.Should().Be(RefreshTokenStatus.Active);
    }

    [Fact]
    public async Task NoActiveOnly_IncludesAllUserSessions()
    {
        var userId = Guid.NewGuid();
        _currentUser.SetupGet(c => c.UserId).Returns(userId.ToString());
        var tokens = new[]
        {
            new RefreshToken { Id = Guid.NewGuid(), AccountId = userId, Token = "a", Status = RefreshTokenStatus.Active, IssuedAt = DateTime.UtcNow, ExpiredAt = DateTime.UtcNow.AddDays(7) },
            new RefreshToken { Id = Guid.NewGuid(), AccountId = userId, Token = "b", Status = RefreshTokenStatus.Revoked, IssuedAt = DateTime.UtcNow.AddDays(-1), ExpiredAt = DateTime.UtcNow.AddDays(6) }
        };
        var (uow, _, _, _) = MockUnitOfWork.Build(tokenSeed: tokens);
        var handler = new GetMySessionsQueryHandler(uow.Object, _currentUser.Object);

        var resp = await handler.Handle(new GetMySessionsQuery { ActiveOnly = false }, CancellationToken.None);

        resp.Data!.Should().HaveCount(2);
    }

    [Fact]
    public async Task NoUserId_Returns401()
    {
        _currentUser.SetupGet(c => c.UserId).Returns((string?)null);
        var (uow, _, _, _) = MockUnitOfWork.Build();
        var handler = new GetMySessionsQueryHandler(uow.Object, _currentUser.Object);

        var resp = await handler.Handle(new GetMySessionsQuery(), CancellationToken.None);

        resp.StatusCode.Should().Be(401);
    }
}

public class RevokeSessionCommandHandlerTests
{
    private readonly Mock<ICurrentUserService> _currentUser = new();

    [Fact]
    public async Task OwnSession_Revokes_Returns200_WithCount1()
    {
        var userId = Guid.NewGuid();
        _currentUser.SetupGet(c => c.UserId).Returns(userId.ToString());
        var session = new RefreshToken
        {
            Id = Guid.NewGuid(),
            AccountId = userId,
            Token = "rt",
            Status = RefreshTokenStatus.Active,
            IssuedAt = DateTime.UtcNow,
            ExpiredAt = DateTime.UtcNow.AddDays(7)
        };
        var (uow, _, _, _) = MockUnitOfWork.Build(tokenSeed: new[] { session });
        var handler = new RevokeSessionCommandHandler(uow.Object, _currentUser.Object, Moq.Mock.Of<MediatR.IPublisher>());

        var resp = await handler.Handle(new RevokeSessionCommand { SessionId = session.Id }, CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        resp.Data.Should().Be(1);
        session.Status.Should().Be(RefreshTokenStatus.Revoked);
        session.RevokedReason.Should().Be("User revoked");
    }

    [Fact]
    public async Task SessionBelongsToOtherUser_Returns403()
    {
        var userId = Guid.NewGuid();
        _currentUser.SetupGet(c => c.UserId).Returns(userId.ToString());
        var session = new RefreshToken
        {
            Id = Guid.NewGuid(),
            AccountId = Guid.NewGuid(),
            Token = "rt",
            Status = RefreshTokenStatus.Active,
            IssuedAt = DateTime.UtcNow,
            ExpiredAt = DateTime.UtcNow.AddDays(7)
        };
        var (uow, _, _, _) = MockUnitOfWork.Build(tokenSeed: new[] { session });
        var handler = new RevokeSessionCommandHandler(uow.Object, _currentUser.Object, Moq.Mock.Of<MediatR.IPublisher>());

        var resp = await handler.Handle(new RevokeSessionCommand { SessionId = session.Id }, CancellationToken.None);

        resp.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task NotFound_Returns404()
    {
        _currentUser.SetupGet(c => c.UserId).Returns(Guid.NewGuid().ToString());
        var (uow, _, _, _) = MockUnitOfWork.Build();
        var handler = new RevokeSessionCommandHandler(uow.Object, _currentUser.Object, Moq.Mock.Of<MediatR.IPublisher>());

        var resp = await handler.Handle(new RevokeSessionCommand { SessionId = Guid.NewGuid() }, CancellationToken.None);

        resp.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task SessionAlreadyInactive_Returns200_NoChange()
    {
        var userId = Guid.NewGuid();
        _currentUser.SetupGet(c => c.UserId).Returns(userId.ToString());
        var session = new RefreshToken
        {
            Id = Guid.NewGuid(),
            AccountId = userId,
            Token = "rt",
            Status = RefreshTokenStatus.Used,
            IssuedAt = DateTime.UtcNow,
            ExpiredAt = DateTime.UtcNow.AddDays(7)
        };
        var (uow, _, _, _) = MockUnitOfWork.Build(tokenSeed: new[] { session });
        var handler = new RevokeSessionCommandHandler(uow.Object, _currentUser.Object, Moq.Mock.Of<MediatR.IPublisher>());

        var resp = await handler.Handle(new RevokeSessionCommand { SessionId = session.Id }, CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        resp.Data.Should().Be(0);
    }
}

public class RevokeAllSessionsCommandHandlerTests
{
    private readonly Mock<ICurrentUserService> _currentUser = new();

    // #AUTH-01: DB lưu hash → seed luôn hash plaintext "val".
    private static RefreshToken Token(Guid accountId, string val) => new()
    {
        Id = Guid.NewGuid(),
        AccountId = accountId,
        Token = global::AuthService.Application.Interfaces.Helpers.RefreshTokenHasher.Hash(val),
        Status = RefreshTokenStatus.Active,
        IssuedAt = DateTime.UtcNow,
        ExpiredAt = DateTime.UtcNow.AddDays(7)
    };

    [Fact]
    public async Task ExceptCurrent_PreservesGivenToken_RevokesOthers()
    {
        var userId = Guid.NewGuid();
        _currentUser.SetupGet(c => c.UserId).Returns(userId.ToString());
        var current = Token(userId, "current-token");
        var other1 = Token(userId, "other-1");
        var other2 = Token(userId, "other-2");
        var (uow, _, _, _) = MockUnitOfWork.Build(tokenSeed: new[] { current, other1, other2 });
        var handler = new RevokeAllSessionsCommandHandler(uow.Object, _currentUser.Object, Moq.Mock.Of<MediatR.IPublisher>());

        var resp = await handler.Handle(new RevokeAllSessionsCommand { ExceptCurrent = true, CurrentRefreshToken = "current-token" }, CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        resp.Data.Should().Be(2);
        current.Status.Should().Be(RefreshTokenStatus.Active);
        other1.Status.Should().Be(RefreshTokenStatus.Revoked);
        other2.Status.Should().Be(RefreshTokenStatus.Revoked);
    }

    [Fact]
    public async Task NoExceptCurrent_RevokesAll()
    {
        var userId = Guid.NewGuid();
        _currentUser.SetupGet(c => c.UserId).Returns(userId.ToString());
        var t1 = Token(userId, "a");
        var t2 = Token(userId, "b");
        var (uow, _, _, _) = MockUnitOfWork.Build(tokenSeed: new[] { t1, t2 });
        var handler = new RevokeAllSessionsCommandHandler(uow.Object, _currentUser.Object, Moq.Mock.Of<MediatR.IPublisher>());

        var resp = await handler.Handle(new RevokeAllSessionsCommand { ExceptCurrent = false }, CancellationToken.None);

        resp.Data.Should().Be(2);
        t1.Status.Should().Be(RefreshTokenStatus.Revoked);
        t2.Status.Should().Be(RefreshTokenStatus.Revoked);
    }

    [Fact]
    public async Task NoUserId_Returns401()
    {
        _currentUser.SetupGet(c => c.UserId).Returns((string?)null);
        var (uow, _, _, _) = MockUnitOfWork.Build();
        var handler = new RevokeAllSessionsCommandHandler(uow.Object, _currentUser.Object, Moq.Mock.Of<MediatR.IPublisher>());

        var resp = await handler.Handle(new RevokeAllSessionsCommand(), CancellationToken.None);

        resp.StatusCode.Should().Be(401);
    }
}
