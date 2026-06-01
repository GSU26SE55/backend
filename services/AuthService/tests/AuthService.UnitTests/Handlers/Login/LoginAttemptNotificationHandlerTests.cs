using AuthService.Application.CQRS.Handler.Login;
using AuthService.Application.CQRS.Notification.Login;
using AuthService.Domain.Entities;
using AuthService.Domain.Enums;
using AuthService.UnitTests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;

namespace AuthService.UnitTests.Handlers.Login;

public class LoginAttemptNotificationHandlerTests
{
    private LoginAttemptNotificationHandler CreateHandler(
        Mock<SharedKernels.Interfaces.IGenericRepository<LoginAttempt>>? loginAttemptsMock = null)
    {
        var (uow, _, _, _) = MockUnitOfWork.Build();
        if (loginAttemptsMock != null)
            uow.SetupGet(u => u.LoginAttempts).Returns(loginAttemptsMock.Object);

        return new LoginAttemptNotificationHandler(
            uow.Object,
            NullLogger<LoginAttemptNotificationHandler>.Instance,
            httpContextAccessor: null);
    }

    [Fact]
    public async Task Handle_AppendsEntry_WithAllFields()
    {
        LoginAttempt? captured = null;
        var loginAttempts = new Mock<SharedKernels.Interfaces.IGenericRepository<LoginAttempt>>();
        loginAttempts.Setup(r => r.AddAsync(It.IsAny<LoginAttempt>()))
            .Callback<LoginAttempt>(e => captured = e)
            .Returns(Task.CompletedTask);

        var handler = CreateHandler(loginAttempts);
        var accountId = Guid.NewGuid();
        await handler.Handle(new LoginAttemptNotification(
            accountId, "user@example.com", LoginAttemptResult.Success, "Password", Note: "ok"),
            CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.AccountId.Should().Be(accountId);
        captured.AttemptedEmail.Should().Be("user@example.com");
        captured.Result.Should().Be(LoginAttemptResult.Success);
        captured.Method.Should().Be("Password");
        captured.Note.Should().Be("ok");
    }

    [Fact]
    public async Task Handle_AccountNotFound_AccountIdIsNull()
    {
        LoginAttempt? captured = null;
        var loginAttempts = new Mock<SharedKernels.Interfaces.IGenericRepository<LoginAttempt>>();
        loginAttempts.Setup(r => r.AddAsync(It.IsAny<LoginAttempt>()))
            .Callback<LoginAttempt>(e => captured = e)
            .Returns(Task.CompletedTask);

        var handler = CreateHandler(loginAttempts);
        await handler.Handle(new LoginAttemptNotification(
            null, "ghost@example.com", LoginAttemptResult.AccountNotFound, "Password"),
            CancellationToken.None);

        captured!.AccountId.Should().BeNull();
        captured.AttemptedEmail.Should().Be("ghost@example.com");
        captured.Result.Should().Be(LoginAttemptResult.AccountNotFound);
    }

    [Fact]
    public async Task Handle_TruncatesNote_To500Chars()
    {
        LoginAttempt? captured = null;
        var loginAttempts = new Mock<SharedKernels.Interfaces.IGenericRepository<LoginAttempt>>();
        loginAttempts.Setup(r => r.AddAsync(It.IsAny<LoginAttempt>()))
            .Callback<LoginAttempt>(e => captured = e)
            .Returns(Task.CompletedTask);

        var handler = CreateHandler(loginAttempts);
        var longNote = new string('x', 1000);
        await handler.Handle(new LoginAttemptNotification(
            null, "u@e.com", LoginAttemptResult.WrongPassword, "Password", Note: longNote),
            CancellationToken.None);

        captured!.Note!.Length.Should().Be(500);
    }

    [Fact]
    public async Task Handle_DoesNotThrow_WhenRepositoryFails()
    {
        var loginAttempts = new Mock<SharedKernels.Interfaces.IGenericRepository<LoginAttempt>>();
        loginAttempts.Setup(r => r.AddAsync(It.IsAny<LoginAttempt>()))
            .ThrowsAsync(new InvalidOperationException("DB unavailable"));

        var handler = CreateHandler(loginAttempts);
        var action = async () => await handler.Handle(new LoginAttemptNotification(
            Guid.NewGuid(), "u@e.com", LoginAttemptResult.Success, "Password"),
            CancellationToken.None);

        await action.Should().NotThrowAsync();
    }
}

public class GetLoginHistoryQueryHandlerTests
{
    private static LoginAttempt NewAttempt(
        Guid accountId,
        LoginAttemptResult result = LoginAttemptResult.Success,
        DateTime? createdAt = null)
    {
        return new LoginAttempt
        {
            Id = Guid.NewGuid(),
            AccountId = accountId,
            AttemptedEmail = "u@e.com",
            Result = result,
            Method = "Password",
            CreatedAt = createdAt ?? DateTime.UtcNow
        };
    }

    [Fact]
    public async Task GetHistory_ReturnsOnlyTargetAccount_OrderedDesc()
    {
        var target = Guid.NewGuid();
        var other = Guid.NewGuid();
        var attempts = new[]
        {
            NewAttempt(target, createdAt: DateTime.UtcNow.AddDays(-1)),
            NewAttempt(target, createdAt: DateTime.UtcNow),
            NewAttempt(other, createdAt: DateTime.UtcNow)
        };
        var (uow, _, _, _) = MockUnitOfWork.Build();
        var loginAttempts = new Mock<SharedKernels.Interfaces.IGenericRepository<LoginAttempt>>();
        loginAttempts.Setup(r => r.GetAllAsync()).Returns(attempts.AsQueryable().BuildMock());
        uow.SetupGet(u => u.LoginAttempts).Returns(loginAttempts.Object);

        var handler = new GetLoginHistoryQueryHandler(uow.Object);
        var resp = await handler.Handle(new Application.CQRS.Query.Login.GetLoginHistoryQuery
        {
            AccountId = target,
            PageNumber = 1,
            PageSize = 10
        }, CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        resp.Data!.TotalItems.Should().Be(2);
        resp.Data.Items.Should().HaveCount(2);
        resp.Data.Items[0].CreatedAt.Should().BeAfter(resp.Data.Items[1].CreatedAt);
    }

    [Fact]
    public async Task OnlyFailed_ExcludesSuccessAttempts()
    {
        var target = Guid.NewGuid();
        var attempts = new[]
        {
            NewAttempt(target, LoginAttemptResult.Success),
            NewAttempt(target, LoginAttemptResult.WrongPassword),
            NewAttempt(target, LoginAttemptResult.AccountLocked)
        };
        var (uow, _, _, _) = MockUnitOfWork.Build();
        var loginAttempts = new Mock<SharedKernels.Interfaces.IGenericRepository<LoginAttempt>>();
        loginAttempts.Setup(r => r.GetAllAsync()).Returns(attempts.AsQueryable().BuildMock());
        uow.SetupGet(u => u.LoginAttempts).Returns(loginAttempts.Object);

        var handler = new GetLoginHistoryQueryHandler(uow.Object);
        var resp = await handler.Handle(new Application.CQRS.Query.Login.GetLoginHistoryQuery
        {
            AccountId = target,
            PageNumber = 1,
            PageSize = 10,
            OnlyFailed = true
        }, CancellationToken.None);

        resp.Data!.TotalItems.Should().Be(2);
        resp.Data.Items.Should().AllSatisfy(i => i.Result.Should().NotBe(LoginAttemptResult.Success));
    }

    [Fact]
    public async Task EmptyAccountId_Returns400()
    {
        var (uow, _, _, _) = MockUnitOfWork.Build();
        var handler = new GetLoginHistoryQueryHandler(uow.Object);

        var resp = await handler.Handle(new Application.CQRS.Query.Login.GetLoginHistoryQuery
        {
            AccountId = Guid.Empty
        }, CancellationToken.None);

        resp.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task InvalidTimeRange_Returns400()
    {
        var (uow, _, _, _) = MockUnitOfWork.Build();
        var handler = new GetLoginHistoryQueryHandler(uow.Object);

        var resp = await handler.Handle(new Application.CQRS.Query.Login.GetLoginHistoryQuery
        {
            AccountId = Guid.NewGuid(),
            FromUtc = DateTime.UtcNow,
            ToUtc = DateTime.UtcNow.AddDays(-1)
        }, CancellationToken.None);

        resp.StatusCode.Should().Be(400);
    }
}
