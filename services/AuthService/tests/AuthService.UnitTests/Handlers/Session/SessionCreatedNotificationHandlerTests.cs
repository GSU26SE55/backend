using AuthService.Application.Configuration;
using AuthService.Application.CQRS.Handler.Session;
using AuthService.Application.CQRS.Notification.Session;
using AuthService.Domain.Entities;
using AuthService.Domain.Enums;
using AuthService.UnitTests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AuthService.UnitTests.Handlers.Session;

public class SessionCreatedNotificationHandlerTests
{
    private static SessionCreatedNotificationHandler CreateHandler(
        SessionOptions options,
        IEnumerable<RefreshToken>? existingTokens = null,
        Mock<MediatR.IPublisher>? publisher = null)
    {
        var (uow, _, _, _) = MockUnitOfWork.Build(tokenSeed: existingTokens);
        return new SessionCreatedNotificationHandler(
            uow.Object,
            (publisher ?? MockPublisher.NoOp()).Object,
            Options.Create(options),
            NullLogger<SessionCreatedNotificationHandler>.Instance);
    }

    private static RefreshToken Active(Guid accountId, DateTime issuedAt)
    {
        return new RefreshToken
        {
            Id = Guid.NewGuid(),
            AccountId = accountId,
            Token = Guid.NewGuid().ToString("N"),
            IssuedAt = issuedAt,
            ExpiredAt = issuedAt.AddDays(7),
            Status = RefreshTokenStatus.Active
        };
    }

    [Fact]
    public async Task Handle_BelowLimit_DoesNothing()
    {
        var accountId = Guid.NewGuid();
        var existing = new[]
        {
            Active(accountId, DateTime.UtcNow.AddHours(-3)),
            Active(accountId, DateTime.UtcNow.AddHours(-2))
        };
        // persistedCount=2, new=1, total=3, max=5 → no revoke
        var handler = CreateHandler(new SessionOptions { MaxConcurrentSessions = 5 }, existing);

        await handler.Handle(new SessionCreatedNotification(accountId), CancellationToken.None);

        existing.Should().AllSatisfy(t => t.Status.Should().Be(RefreshTokenStatus.Active));
    }

    [Fact]
    public async Task Handle_ExceedsLimit_RevokesOldestSession_FIFO()
    {
        var accountId = Guid.NewGuid();
        var oldest = Active(accountId, DateTime.UtcNow.AddHours(-10));
        var middle = Active(accountId, DateTime.UtcNow.AddHours(-5));
        var newer = Active(accountId, DateTime.UtcNow.AddHours(-1));
        var existing = new[] { newer, oldest, middle }; // unordered input
        // persistedCount=3, new=1, total=4, max=3 → revoke 1 oldest
        var handler = CreateHandler(new SessionOptions { MaxConcurrentSessions = 3 }, existing);

        await handler.Handle(new SessionCreatedNotification(accountId), CancellationToken.None);

        oldest.Status.Should().Be(RefreshTokenStatus.Revoked);
        oldest.RevokedAt.Should().NotBeNull();
        oldest.RevokedReason.Should().Contain("session limit");
        middle.Status.Should().Be(RefreshTokenStatus.Active);
        newer.Status.Should().Be(RefreshTokenStatus.Active);
    }

    [Fact]
    public async Task Handle_ExceedsLimitByMultiple_RevokesAllNeeded_OldestFirst()
    {
        var accountId = Guid.NewGuid();
        var tokens = Enumerable.Range(0, 5)
            .Select(i => Active(accountId, DateTime.UtcNow.AddHours(-10 + i)))
            .ToArray();
        // persistedCount=5, new=1, total=6, max=2 → revoke 4 oldest
        var handler = CreateHandler(new SessionOptions { MaxConcurrentSessions = 2 }, tokens);

        await handler.Handle(new SessionCreatedNotification(accountId), CancellationToken.None);

        var sortedByIssued = tokens.OrderBy(t => t.IssuedAt).ToArray();
        sortedByIssued.Take(4).Should().AllSatisfy(t => t.Status.Should().Be(RefreshTokenStatus.Revoked));
        sortedByIssued.Skip(4).Should().AllSatisfy(t => t.Status.Should().Be(RefreshTokenStatus.Active));
    }

    [Fact]
    public async Task Handle_OnlyAffectsOwnAccount_NotOtherUsers()
    {
        var ownerId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        var ownerTokens = new[]
        {
            Active(ownerId, DateTime.UtcNow.AddHours(-3)),
            Active(ownerId, DateTime.UtcNow.AddHours(-2))
        };
        var otherTokens = new[]
        {
            Active(otherId, DateTime.UtcNow.AddHours(-10)),
            Active(otherId, DateTime.UtcNow.AddHours(-9))
        };
        var handler = CreateHandler(new SessionOptions { MaxConcurrentSessions = 2 },
            ownerTokens.Concat(otherTokens));

        // Owner: persisted 2, new 1, total 3 > 2 → revoke 1 oldest owner.
        await handler.Handle(new SessionCreatedNotification(ownerId), CancellationToken.None);

        ownerTokens.OrderBy(t => t.IssuedAt).First().Status.Should().Be(RefreshTokenStatus.Revoked);
        otherTokens.Should().AllSatisfy(t => t.Status.Should().Be(RefreshTokenStatus.Active));
    }

    [Fact]
    public async Task Handle_LimitDisabled_WithZeroOrNegative_DoesNothing()
    {
        var accountId = Guid.NewGuid();
        var existing = Enumerable.Range(0, 10)
            .Select(i => Active(accountId, DateTime.UtcNow.AddHours(-i)))
            .ToArray();
        var handler = CreateHandler(new SessionOptions { MaxConcurrentSessions = 0 }, existing);

        await handler.Handle(new SessionCreatedNotification(accountId), CancellationToken.None);

        existing.Should().AllSatisfy(t => t.Status.Should().Be(RefreshTokenStatus.Active));
    }

    [Fact]
    public async Task Handle_DoesNotCount_AlreadyRevokedOrUsedSessions()
    {
        var accountId = Guid.NewGuid();
        var active1 = Active(accountId, DateTime.UtcNow.AddHours(-2));
        var revoked = Active(accountId, DateTime.UtcNow.AddHours(-5));
        revoked.Status = RefreshTokenStatus.Revoked;
        var used = Active(accountId, DateTime.UtcNow.AddHours(-3));
        used.Status = RefreshTokenStatus.Used;
        // persistedActive=1 (revoked+used không tính), new=1, total=2, max=3 → no revoke
        var handler = CreateHandler(new SessionOptions { MaxConcurrentSessions = 3 },
            new[] { active1, revoked, used });

        await handler.Handle(new SessionCreatedNotification(accountId), CancellationToken.None);

        active1.Status.Should().Be(RefreshTokenStatus.Active);
    }

    [Fact]
    public async Task Handle_DoesNotThrow_WhenRepositoryFails()
    {
        var publisher = MockPublisher.NoOp();
        var uow = new Mock<AuthService.Application.Interfaces.Repositories.IAuthUnitOfWork>();
        var refreshTokens = new Mock<SharedKernels.Interfaces.IGenericRepository<RefreshToken>>();
        refreshTokens.Setup(r => r.GetAllAsync()).Throws(new InvalidOperationException("DB down"));
        uow.SetupGet(u => u.RefreshTokens).Returns(refreshTokens.Object);

        var handler = new SessionCreatedNotificationHandler(
            uow.Object,
            publisher.Object,
            Options.Create(new SessionOptions { MaxConcurrentSessions = 3 }),
            NullLogger<SessionCreatedNotificationHandler>.Instance);

        var action = async () => await handler.Handle(
            new SessionCreatedNotification(Guid.NewGuid()), CancellationToken.None);

        await action.Should().NotThrowAsync();
    }
}
