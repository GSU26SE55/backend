using AuthService.Application.CQRS.Handler.Audit;
using AuthService.Application.CQRS.Query.Audit;
using AuthService.Domain.Entities;
using AuthService.Domain.Enums;
using AuthService.UnitTests.Helpers;

namespace AuthService.UnitTests.Handlers.Audit;

public class GetAuditLogsQueryHandlerTests
{
    private static AuditLog NewLog(
        AuditActionEnum action,
        Guid? targetAccountId = null,
        Guid? actorAccountId = null,
        bool isSuccess = true,
        DateTime? createdAt = null)
    {
        return new AuditLog
        {
            Id = Guid.NewGuid(),
            Action = action,
            TargetAccountId = targetAccountId,
            ActorAccountId = actorAccountId,
            IsSuccess = isSuccess,
            CreatedAt = createdAt ?? DateTime.UtcNow
        };
    }

    [Fact]
    public async Task NoFilter_ReturnsAll_OrderedByCreatedAtDesc()
    {
        var older = NewLog(AuditActionEnum.LoginSuccess, createdAt: DateTime.UtcNow.AddDays(-1));
        var newer = NewLog(AuditActionEnum.Logout, createdAt: DateTime.UtcNow);
        var (uow, _, _, _, _) = MockUnitOfWork.Build(auditLogSeed: new[] { older, newer });
        var handler = new GetAuditLogsQueryHandler(uow.Object);

        var resp = await handler.Handle(new GetAuditLogsQuery { PageNumber = 1, PageSize = 10 }, CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        resp.Data!.TotalItems.Should().Be(2);
        resp.Data.Items.Should().HaveCount(2);
        resp.Data.Items[0].Action.Should().Be(AuditActionEnum.Logout); // newest first
        resp.Data.Items[0].ActionName.Should().Be("Logout");
    }

    [Fact]
    public async Task FilterByAction_ReturnsOnlyMatching()
    {
        var login = NewLog(AuditActionEnum.LoginSuccess);
        var changePw = NewLog(AuditActionEnum.PasswordChanged);
        var (uow, _, _, _, _) = MockUnitOfWork.Build(auditLogSeed: new[] { login, changePw });
        var handler = new GetAuditLogsQueryHandler(uow.Object);

        var resp = await handler.Handle(new GetAuditLogsQuery
        {
            PageNumber = 1, PageSize = 10,
            Action = AuditActionEnum.PasswordChanged
        }, CancellationToken.None);

        resp.Data!.TotalItems.Should().Be(1);
        resp.Data.Items[0].Action.Should().Be(AuditActionEnum.PasswordChanged);
    }

    [Fact]
    public async Task FilterByTargetAccount_ReturnsOnlyMatching()
    {
        var targetId = Guid.NewGuid();
        var match = NewLog(AuditActionEnum.LoginSuccess, targetAccountId: targetId);
        var nonMatch = NewLog(AuditActionEnum.LoginSuccess, targetAccountId: Guid.NewGuid());
        var (uow, _, _, _, _) = MockUnitOfWork.Build(auditLogSeed: new[] { match, nonMatch });
        var handler = new GetAuditLogsQueryHandler(uow.Object);

        var resp = await handler.Handle(new GetAuditLogsQuery
        {
            PageNumber = 1, PageSize = 10,
            TargetAccountId = targetId
        }, CancellationToken.None);

        resp.Data!.TotalItems.Should().Be(1);
        resp.Data.Items[0].TargetAccountId.Should().Be(targetId);
    }

    [Fact]
    public async Task FilterByIsSuccess_False_ReturnsOnlyFailed()
    {
        var success = NewLog(AuditActionEnum.LoginSuccess, isSuccess: true);
        var failed = NewLog(AuditActionEnum.LoginFailedWrongPassword, isSuccess: false);
        var (uow, _, _, _, _) = MockUnitOfWork.Build(auditLogSeed: new[] { success, failed });
        var handler = new GetAuditLogsQueryHandler(uow.Object);

        var resp = await handler.Handle(new GetAuditLogsQuery
        {
            PageNumber = 1, PageSize = 10,
            IsSuccess = false
        }, CancellationToken.None);

        resp.Data!.TotalItems.Should().Be(1);
        resp.Data.Items[0].IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task FilterByTimeRange_ReturnsOnlyInWindow()
    {
        var inside = NewLog(AuditActionEnum.LoginSuccess, createdAt: new DateTime(2026, 5, 5, 12, 0, 0, DateTimeKind.Utc));
        var beforeWindow = NewLog(AuditActionEnum.LoginSuccess, createdAt: new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc));
        var afterWindow = NewLog(AuditActionEnum.LoginSuccess, createdAt: new DateTime(2026, 5, 10, 0, 0, 0, DateTimeKind.Utc));
        var (uow, _, _, _, _) = MockUnitOfWork.Build(auditLogSeed: new[] { inside, beforeWindow, afterWindow });
        var handler = new GetAuditLogsQueryHandler(uow.Object);

        var resp = await handler.Handle(new GetAuditLogsQuery
        {
            PageNumber = 1, PageSize = 10,
            FromUtc = new DateTime(2026, 5, 3, 0, 0, 0, DateTimeKind.Utc),
            ToUtc = new DateTime(2026, 5, 8, 0, 0, 0, DateTimeKind.Utc)
        }, CancellationToken.None);

        resp.Data!.TotalItems.Should().Be(1);
    }

    [Fact]
    public async Task InvalidTimeRange_FromGteTo_Returns400()
    {
        var (uow, _, _, _, _) = MockUnitOfWork.Build();
        var handler = new GetAuditLogsQueryHandler(uow.Object);

        var resp = await handler.Handle(new GetAuditLogsQuery
        {
            FromUtc = DateTime.UtcNow,
            ToUtc = DateTime.UtcNow.AddDays(-1)
        }, CancellationToken.None);

        resp.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task Pagination_ReturnsCorrectPage()
    {
        var logs = Enumerable.Range(0, 25)
            .Select(i => NewLog(AuditActionEnum.LoginSuccess, createdAt: DateTime.UtcNow.AddMinutes(-i)))
            .ToArray();
        var (uow, _, _, _, _) = MockUnitOfWork.Build(auditLogSeed: logs);
        var handler = new GetAuditLogsQueryHandler(uow.Object);

        var resp = await handler.Handle(new GetAuditLogsQuery { PageNumber = 2, PageSize = 10 }, CancellationToken.None);

        resp.Data!.TotalItems.Should().Be(25);
        resp.Data.Items.Should().HaveCount(10);
        resp.Data.TotalPages.Should().Be(3);
        resp.Data.HasNextPage.Should().BeTrue();
        resp.Data.HasPreviousPage.Should().BeTrue();
    }
}
